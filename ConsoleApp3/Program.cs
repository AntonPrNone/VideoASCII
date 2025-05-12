using OpenCvSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Text.Json;

class Program
{
    static string asciiChars = "@#S%?*+;:,. "; // Набор символов по умолчанию
    static string customSymbol = null; // Пользовательский символ (если выбран)
    static int width = 220; // Ширина по умолчанию (в символах)
    static double fps; // FPS для воспроизведения (заданный)
    static double videoLength; // Длительность видео в секундах
    static bool useColors = true; // По умолчанию цветной режим
    static bool preprocessFrames = true; // По умолчанию подготавливаем кадры

    struct PixelInfo
    {
        public char Symbol;
        public (byte r, byte g, byte b) Color;
    }

    class FrameData
    {
        public string Text { get; set; }
        public List<List<string>> Colors { get; set; } // Цвета в формате #RRGGBB
    }

    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Запрашиваем путь к видео
        Console.Write("Введи путь к видеофайлу: ");
        string videoPath = Console.ReadLine()?.Trim() ?? "";
        if (!File.Exists(videoPath))
        {
            Console.WriteLine("Файл не найден.");
            return;
        }

        // Запрашиваем FPS
        Console.Write("Выберите FPS (по умолчанию - FPS видео): ");
        string fpsInput = Console.ReadLine()?.Trim();
        double? userFps = null;
        if (!string.IsNullOrWhiteSpace(fpsInput) && double.TryParse(fpsInput, out double parsedFps) && parsedFps > 0)
        {
            userFps = parsedFps;
        }

        // Запрашиваем ширину
        Console.Write("Выберите ширину (по умолчанию 220): ");
        string widthInput = Console.ReadLine()?.Trim();
        if (!int.TryParse(widthInput, out width) || width <= 0)
        {
            width = 220;
        }

        // Запрашиваем режим (цветной или чёрно-белый)
        Console.Write("Выберите режим: 1 - цветной (по умолчанию), 2 - чёрно-белый: ");
        string modeInput = Console.ReadLine()?.Trim();
        useColors = modeInput != "2"; // Цветной режим, если не введено "2"

        // Запрашиваем пользовательский символ только в цветном режиме
        if (useColors)
        {
            Console.Write("Введите символ для отображения (или Enter для набора по умолчанию): ");
            string symbolInput = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(symbolInput) && symbolInput.Length == 1)
            {
                customSymbol = symbolInput; // Сохраняем пользовательский символ
            }
        }

        // Запрашиваем режим обработки
        Console.Write("Подготовить кадры заранее? 1 - да (по умолчанию), 2 - нет: ");
        string preprocessInput = Console.ReadLine()?.Trim();
        preprocessFrames = preprocessInput != "2"; // Подготовка, если не введено "2"

        // Открываем видео
        using var capture = new VideoCapture(videoPath);
        if (!capture.IsOpened())
        {
            Console.WriteLine("Не удалось открыть видео.");
            return;
        }

        // Устанавливаем FPS
        fps = userFps ?? capture.Get(VideoCaptureProperties.Fps);
        videoLength = capture.Get(VideoCaptureProperties.FrameCount) / fps;

        // Вычисляем пропорции видео
        double videoWidth = capture.Get(VideoCaptureProperties.FrameWidth);
        double videoHeight = capture.Get(VideoCaptureProperties.FrameHeight);
        double aspectRatio = videoWidth / videoHeight; // Например, 16:9 = 1.777

        // Коррекция высоты для символов (примерно 2:1)
        int asciiHeight = (int)(width / aspectRatio / 2);

        // Подготавливаем кадры
        Console.WriteLine("Подготовка кадров, пожалуйста, подождите...");
        List<FrameData> frames = PreprocessFrames(capture, width, asciiHeight);

        // Сохраняем кадры в JSON
        string jsonPath = "frames.json";
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(frames, new JsonSerializerOptions { WriteIndented = true }));

        // Генерируем HTML-файл
        GenerateHtmlFile(fps);

        Console.WriteLine($"\nКадры подготовлены. Открой файл 'ascii_video.html' в браузере для воспроизведения.");
    }

    static List<FrameData> PreprocessFrames(VideoCapture capture, int width, int asciiHeight)
    {
        List<FrameData> frames = new List<FrameData>();
        PixelInfo[,] output = new PixelInfo[asciiHeight, width];
        using var frame = new Mat();

        int totalFrames = (int)capture.Get(VideoCaptureProperties.FrameCount);
        int processedFrames = 0;

        while (capture.Read(frame))
        {
            // Изменяем размер кадра
            var resized = frame.Resize(new OpenCvSharp.Size(width, asciiHeight));

            // Обрабатываем кадр параллельно
            Parallel.For(0, resized.Rows, y =>
            {
                for (int x = 0; x < resized.Cols; x++)
                {
                    var color = resized.At<Vec3b>(y, x);
                    byte b = color.Item0, g = color.Item1, r = color.Item2;
                    double brightness = 0.299 * r + 0.587 * g + 0.114 * b;

                    // Выбираем символ
                    char symbol;
                    if (customSymbol != null && useColors)
                    {
                        symbol = customSymbol[0]; // Используем пользовательский символ
                    }
                    else
                    {
                        int index = (int)(brightness * (asciiChars.Length - 1) / 255);
                        symbol = asciiChars[index]; // Используем набор символов
                    }

                    output[y, x] = new PixelInfo { Symbol = symbol, Color = (r, g, b) };
                }
            });

            // Формируем кадр
            StringBuilder sb = new StringBuilder();
            List<List<string>> colors = new List<List<string>>();
            for (int y = 0; y < asciiHeight; y++)
            {
                List<string> rowColors = new List<string>();
                for (int x = 0; x < width; x++)
                {
                    var pixel = output[y, x];
                    sb.Append(pixel.Symbol);
                    if (useColors)
                    {
                        // Преобразуем цвет в формат #RRGGBB
                        string hexColor = $"#{pixel.Color.r:X2}{pixel.Color.g:X2}{pixel.Color.b:X2}";
                        rowColors.Add(hexColor);
                    }
                }
                sb.AppendLine();
                if (useColors) colors.Add(rowColors);
            }

            frames.Add(new FrameData { Text = sb.ToString(), Colors = colors });

            // Обновляем прогресс
            processedFrames++;
            double progress = (double)processedFrames / totalFrames * 100;
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write($"Обработано {processedFrames}/{totalFrames} кадров ({progress:F1}%)");
        }

        return frames;
    }

    static void GenerateHtmlFile(double fps)
    {
        string htmlContent = @"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>ASCII Art Video</title>
    <style>
        body {
            background-color: black;
            color: white;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            font-family: 'Courier New', Courier, monospace;
        }
        #ascii-art {
            white-space: pre;
            font-size: 12px;
            line-height: 1.2;
        }
        #controls {
            position: fixed;
            top: 10px;
            left: 10px;
            color: white;
        }
    </style>
</head>
<body>
    <div id='controls'>
        <button id='playPauseBtn'>Pause</button>
        <input type='range' id='seekBar' min='0' value='0'>
        <span id='timecode'>00:00:00 / 00:00:00</span>
        <span id='fps'>FPS: 0</span>
    </div>
    <div id='ascii-art'></div>
    <script>
        let frames = [];
        let currentFrame = 0;
        let isPlaying = true;
        let lastFrameTime = 0;
        let frameInterval = 1000 / " + fps.ToString("F1") + @";
        let fontSize = 12;
        let fpsCounter = 0;
        let lastFpsUpdate = 0;
        let framesRendered = 0;

        const asciiArt = document.getElementById('ascii-art');
        const playPauseBtn = document.getElementById('playPauseBtn');
        const seekBar = document.getElementById('seekBar');
        const timecode = document.getElementById('timecode');
        const fpsDisplay = document.getElementById('fps');

        // Загружаем кадры
        fetch('frames.json')
            .then(response => response.json())
            .then(data => {
                frames = data;
                seekBar.max = frames.length - 1;
                updateTimecode();
                requestAnimationFrame(renderFrame);
            });

        // Воспроизведение кадров
        function renderFrame(timestamp) {
            if (!isPlaying) {
                requestAnimationFrame(renderFrame);
                return;
            }

            if (timestamp - lastFrameTime >= frameInterval) {
                if (currentFrame < frames.length) {
                    const frame = frames[currentFrame];
                    if (" + (useColors ? "true" : "false") + @") {
                        // Цветной режим
                        let html = '';
                        const lines = frame.text.split('\n').filter(line => line.length > 0);
                        for (let y = 0; y < lines.length; y++) {
                            const line = lines[y];
                            for (let x = 0; x < line.length; x++) {
                                const color = frame.colors[y][x];
                                html += `<span style='color: ${color}'>${line[x]}</span>`;
                            }
                            html += '\n';
                        }
                        asciiArt.innerHTML = html;
                    } else {
                        // Монохромный режим
                        asciiArt.textContent = frame.text;
                    }

                    currentFrame++;
                    lastFrameTime = timestamp;
                    framesRendered++;

                    // Обновляем ползунок
                    seekBar.value = currentFrame;
                    updateTimecode();

                    // Обновляем FPS
                    const elapsedSinceLastFpsUpdate = (timestamp - lastFpsUpdate) / 1000;
                    if (elapsedSinceLastFpsUpdate >= 1) {
                        const actualFps = framesRendered / elapsedSinceLastFpsUpdate;
                        fpsDisplay.textContent = `FPS: ${actualFps.toFixed(1)}`;
                        framesRendered = 0;
                        lastFpsUpdate = timestamp;
                    }
                } else {
                    isPlaying = false;
                    playPauseBtn.textContent = 'Play';
                }
            }

            requestAnimationFrame(renderFrame);
        }

        // Обновление таймкода
        function updateTimecode() {
            const currentTime = currentFrame / " + fps.ToString("F1") + @";
            const totalTime = " + videoLength.ToString("F1") + @";
            const currentFormatted = new Date(currentTime * 1000).toISOString().substr(11, 8);
            const totalFormatted = new Date(totalTime * 1000).toISOString().substr(11, 8);
            timecode.textContent = `${currentFormatted} / ${totalFormatted}`;
        }

        // Пауза/воспроизведение
        playPauseBtn.addEventListener('click', () => {
            isPlaying = !isPlaying;
            playPauseBtn.textContent = isPlaying ? 'Pause' : 'Play';
        });

        // Перемотка
        seekBar.addEventListener('input', () => {
            currentFrame = parseInt(seekBar.value);
            lastFrameTime = performance.now();
            updateTimecode();
        });

        // Масштабирование с помощью Ctrl + прокрутка колёсика
        document.addEventListener('wheel', (e) => {
            if (e.ctrlKey) {
                e.preventDefault();
                fontSize += e.deltaY > 0 ? -2 : 2;
                if (fontSize < 6) fontSize = 6;
                if (fontSize > 48) fontSize = 48;
                asciiArt.style.fontSize = `${fontSize}px`;
            }
        });
    </script>
</body>
</html>";

        File.WriteAllText("ascii_video.html", htmlContent);
    }
}