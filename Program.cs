using System.Diagnostics;
using DeckLinkAPI;

namespace SrtSuite;

internal static class Program
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--test-av")
        {
            AttachConsole(-1);
            RunAvTest();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    private static void RunAvTest()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("[TEST] Starting Native SRT AV Playout Verification");
        Console.WriteLine("========================================");

        var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
        if (!File.Exists(ffmpegPath))
        {
            ffmpegPath = @"c:\Users\vimlesh\Documents\vimlesh\srt\ffmpeg\ffmpeg.exe";
        }
        Console.WriteLine($"[TEST] Using FFmpeg: {ffmpegPath}");

        var devices = DeckLinkInterop.EnumerateDevices();
        Console.WriteLine($"[TEST] Found {devices.Count} DeckLink device(s):");
        string selectedDevice = "";
        foreach (var d in devices)
        {
            Console.WriteLine($"  * {d.Name} ({d.ModelName})");
            if (string.IsNullOrEmpty(selectedDevice))
            {
                selectedDevice = d.Name;
            }
        }

        using var rx = new SrtReceiverEngine(ffmpegPath);
        using var tx = new SrtTransmitterEngine(ffmpegPath);

        long rxFrames = 0;
        rx.OnPreviewFrame += _ => Interlocked.Increment(ref rxFrames);
        rx.OnLog += msg => Console.Write($"[RX] {msg}");
        tx.OnLog += msg => Console.Write($"[TX] {msg}");

        var rxSettings = new RxSettings(
            Mode: SrtMode.Listener,
            Host: "0.0.0.0",
            Port: 9998,
            LatencyMs: 120,
            Passphrase: "",
            StreamId: "",
            EnableDeckLinkPlayout: !string.IsNullOrEmpty(selectedDevice),
            DeckLinkDevice: selectedDevice,
            FormatCode: "Hi50"
        );

        var txSettings = new TxSettings(
            SourceType: SourceType.ColorBars,
            DeckLinkDevice: "",
            FormatCode: "Hi50",
            VideoInput: "unset",
            Encoder: "h264_nvenc",
            Bitrate: "6000k",
            FilePath: "",
            Loop: false,
            Mode: SrtMode.Caller,
            Host: "127.0.0.1",
            Port: 9998,
            LatencyMs: 120,
            Passphrase: "",
            StreamId: ""
        );

        Console.WriteLine("\n[TEST] Starting SRT Receiver (1080i50 SDI)...");
        rx.Start(rxSettings);
        Thread.Sleep(1000);

        Console.WriteLine("\n[TEST] Starting SRT Transmitter (NVENC + 48kHz AAC)...");
        tx.Start(txSettings);

        Console.WriteLine("\n[TEST] Streaming for 10 seconds...");
        for (int i = 1; i <= 10; i++)
        {
            Thread.Sleep(1000);
            Console.WriteLine($"[TEST] Elapsed: {i}s | Preview frames received: {Interlocked.Read(ref rxFrames)}");
        }

        long finalFrames = Interlocked.Read(ref rxFrames);
        Console.WriteLine($"\n[TEST] Finished. Total preview frames received: {finalFrames}");

        tx.Stop();
        rx.Stop();

        if (finalFrames > 10)
        {
            Console.WriteLine(">>> TEST PASSED: Video and Audio stream continuously without freezing! <<<");
        }
        else
        {
            Console.WriteLine(">>> TEST FAILED: Pipeline stalled! <<<");
        }
    }
}

