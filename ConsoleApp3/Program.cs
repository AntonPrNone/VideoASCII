using OpenCvSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Collections.Generic;

class Program
{
    static string asciiChars = "@#S%?*+;:,. "; // Набор символов по умолчанию
    static string customSymbol = null; // Пользовательский символ (если выбран)
    static int width = 220; // Ширина по умолчанию
    static double fps; // FPS для воспроизведения (заданный)
    static double frameDuration; // Длительность кадра в миллисекундах
    static double videoLength; // Длительность видео в секундах
    static bool useColors = true; // По умолчанию цветной режим
    static bool ansiSupported = false; // Флаг поддержки ANSI-кодов
    static bool preprocessFrames = true; // По умолчанию подготавливаем кадры

    // P/Invoke для работы с консолью
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool SetWindowText(IntPtr hWnd, string text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    const int STD_OUTPUT_HANDLE = -11;
    const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    const int SW_MAXIMIZE = 3;

    struct PixelInfo
    {
        public char Symbol;
        public (byte r, byte g, byte b) Color;
    }

    static void Main()
    {
        // Включаем поддержку ANSI-кодов в консоли
        EnableAnsiSupport();

        MaximizeConsole();
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
        frameDuration = 1000.0 / fps;
        videoLength = capture.Get(VideoCaptureProperties.FrameCount) / fps;

        // Вычисляем пропорции видео
        double videoWidth = capture.Get(VideoCaptureProperties.FrameWidth);
        double videoHeight = capture.Get(VideoCaptureProperties.FrameHeight);
        double aspectRatio = videoWidth / videoHeight; // Например, 16:9 = 1.777

        // Коррекция высоты для символов консоли (они примерно 2:1)
        int asciiHeight = (int)(width / aspectRatio / 2); // Делим на 2, чтобы учесть высоту символов

        // Ограничиваем размеры консолью
        if (asciiHeight > Console.WindowHeight - 3)
        {
            asciiHeight = Console.WindowHeight - 3;
            width = (int)(asciiHeight * aspectRatio * 2); // Пересчитываем ширину
        }

        Console.SetWindowSize(width, asciiHeight + 3);

        // В зависимости от выбора пользователя
        if (preprocessFrames)
        {
            // Подготавливаем кадры
            Console.WriteLine("Подготовка кадров, пожалуйста, подождите...");
            List<StringBuilder> frames = PreprocessFrames(capture, width, asciiHeight);
            Console.WriteLine("\nКадры подготовлены, начинаем воспроизведение...");
            PlayFrames(frames);
        }
        else
        {
            // Воспроизведение в реальном времени
            PlayFramesInRealTime(capture, width, asciiHeight);
        }
    }

    static List<StringBuilder> PreprocessFrames(VideoCapture capture, int width, int asciiHeight)
    {
        List<StringBuilder> frames = new List<StringBuilder>();
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
                    if (customSymbol != null && useColors) // Учитываем пользовательский символ только в цветном режиме
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
            for (int y = 0; y < asciiHeight; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = output[y, x];
                    if (useColors && ansiSupported)
                        sb.Append($"\x1b[38;2;{pixel.Color.r};{pixel.Color.g};{pixel.Color.b}m{pixel.Symbol}");
                    else
                        sb.Append(pixel.Symbol);
                }
                sb.AppendLine();
            }

            frames.Add(sb);

            // Обновляем прогресс
            processedFrames++;
            double progress = (double)processedFrames / totalFrames * 100;
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write($"Обработано {processedFrames}/{totalFrames} кадров ({progress:F1}%)");
        }

        return frames;
    }

    static void PlayFrames(List<StringBuilder> frames)
    {
        Stopwatch totalWatch = new Stopwatch();
        totalWatch.Start();
        double lastTimecodeUpdate = 0;
        double lastFpsUpdate = 0;
        int framesRendered = 0;

        for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            double currentTime = totalWatch.Elapsed.TotalMilliseconds;
            double expectedTime = frameIndex * frameDuration;

            // Если отстаем больше, чем на кадр, пропускаем кадры
            if (currentTime > expectedTime + frameDuration)
            {
                int framesToSkip = (int)((currentTime - expectedTime) / frameDuration);
                frameIndex += framesToSkip;
                if (frameIndex >= frames.Count) break;
                continue;
            }
            // Если опережаем, ждем
            else if (currentTime < expectedTime)
            {
                Thread.Sleep((int)(expectedTime - currentTime));
            }

            // Выводим кадр
            Console.SetCursorPosition(0, 0);
            Console.Write(frames[frameIndex].ToString());
            if (useColors && ansiSupported) Console.Write("\x1b[0m");

            framesRendered++;

            // Обновляем таймкод и фактический FPS
            double videoTime = frameIndex / fps;
            double elapsedSinceLastFpsUpdate = totalWatch.Elapsed.TotalSeconds - lastFpsUpdate;
            if (videoTime - lastTimecodeUpdate >= 1)
            {
                lastTimecodeUpdate = videoTime;

                // Вычисляем фактический FPS
                double actualFps = framesRendered / elapsedSinceLastFpsUpdate;
                framesRendered = 0; // Сбрасываем счётчик
                lastFpsUpdate = totalWatch.Elapsed.TotalSeconds;

                string currentTimeFormatted = TimeSpan.FromSeconds(videoTime).ToString(@"hh\:mm\:ss");
                string videoLengthFormatted = TimeSpan.FromSeconds(videoLength).ToString(@"hh\:mm\:ss");
                double progress = videoTime / videoLength * 100;
                SetWindowText(GetConsoleWindow(), $"Фактический FPS: {actualFps:F1} | Заданный FPS: {fps:F1} | Таймкод: {currentTimeFormatted} / {videoLengthFormatted} | Прогресс: {progress:F1}%");
            }
        }
    }

    static void PlayFramesInRealTime(VideoCapture capture, int width, int asciiHeight)
    {
        Stopwatch totalWatch = new Stopwatch();
        totalWatch.Start();
        double lastTimecodeUpdate = 0;
        double lastFpsUpdate = 0;
        int framesRendered = 0;

        PixelInfo[,] output = new PixelInfo[asciiHeight, width];
        StringBuilder sb = new StringBuilder();
        using var frame = new Mat();

        while (capture.Read(frame))
        {
            double currentTime = totalWatch.Elapsed.TotalMilliseconds;
            double expectedTime = capture.Get(VideoCaptureProperties.PosFrames) * frameDuration;

            // Если отстаем больше, чем на кадр, пропускаем кадры
            if (currentTime > expectedTime + frameDuration)
            {
                int framesToSkip = (int)((currentTime - expectedTime) / frameDuration);
                capture.Set(VideoCaptureProperties.PosFrames, capture.Get(VideoCaptureProperties.PosFrames) + framesToSkip);
                continue;
            }
            // Если опережаем, ждем
            else if (currentTime < expectedTime)
            {
                Thread.Sleep((int)(expectedTime - currentTime));
            }

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
                    if (customSymbol != null && useColors) // Учитываем пользовательский символ только в цветном режиме
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

            // Формируем вывод
            sb.Clear();
            for (int y = 0; y < asciiHeight; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = output[y, x];
                    if (useColors && ansiSupported)
                        sb.Append($"\x1b[38;2;{pixel.Color.r};{pixel.Color.g};{pixel.Color.b}m{pixel.Symbol}");
                    else
                        sb.Append(pixel.Symbol);
                }
                sb.AppendLine();
            }

            Console.SetCursorPosition(0, 0);
            Console.Write(sb.ToString());
            if (useColors && ansiSupported) Console.Write("\x1b[0m");

            framesRendered++;

            // Обновляем таймкод и фактический FPS
            double videoTime = capture.Get(VideoCaptureProperties.PosFrames) / fps;
            double elapsedSinceLastFpsUpdate = totalWatch.Elapsed.TotalSeconds - lastFpsUpdate;
            if (videoTime - lastTimecodeUpdate >= 1)
            {
                lastTimecodeUpdate = videoTime;

                // Вычисляем фактический FPS
                double actualFps = framesRendered / elapsedSinceLastFpsUpdate;
                framesRendered = 0; // Сбрасываем счётчик
                lastFpsUpdate = totalWatch.Elapsed.TotalSeconds;

                string currentTimeFormatted = TimeSpan.FromSeconds(videoTime).ToString(@"hh\:mm\:ss");
                string videoLengthFormatted = TimeSpan.FromSeconds(videoLength).ToString(@"hh\:mm\:ss");
                double progress = videoTime / videoLength * 100;
                SetWindowText(GetConsoleWindow(), $"Фактический FPS: {actualFps:F1} | Заданный FPS: {fps:F1} | Таймкод: {currentTimeFormatted} / {videoLengthFormatted} | Прогресс: {progress:F1}%");
            }
        }
    }

    static void EnableAnsiSupport()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ansiSupported = true; // Предполагаем, что на не-Windows системах ANSI поддерживается
            return;
        }

        IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
        if (handle == IntPtr.Zero)
        {
            ansiSupported = false;
            return;
        }

        if (!GetConsoleMode(handle, out uint mode))
        {
            ansiSupported = false;
            return;
        }

        mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
        ansiSupported = SetConsoleMode(handle, mode);
    }

    static void MaximizeConsole()
    {
        IntPtr consoleHandle = GetConsoleWindow();
        ShowWindow(consoleHandle, SW_MAXIMIZE);
    }
}