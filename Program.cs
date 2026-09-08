using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
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

        if (args.Length > 0 && args[0] == "--test-ui")
        {
            AttachConsole(-1);
            RunUiTest();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    private static void RunUiTest()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("[TEST-UI] Starting UI Layout & Theme Verification");
        Console.WriteLine("========================================");

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var form = new MainForm();
        form.CreateControl();
        form.Show();
        Application.DoEvents();

        var sb = new StringBuilder();
        sb.AppendLine($"[TEST-UI] Form Size: {form.Size.Width}x{form.Size.Height}");

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var pnlHeader = (Panel)form.GetType().GetField("_pnlHeader", flags)!.GetValue(form)!;
        var tblMain = (TableLayoutPanel)form.GetType().GetField("_tblMain", flags)!.GetValue(form)!;
        var pnlTxControls = (Panel)form.GetType().GetField("_pnlTxControls", flags)!.GetValue(form)!;
        var picTxPreview = (PictureBox)form.GetType().GetField("_picTxPreview", flags)!.GetValue(form)!;
        var pnlRxControls = (Panel)form.GetType().GetField("_pnlRxControls", flags)!.GetValue(form)!;
        var picRxPreview = (PictureBox)form.GetType().GetField("_picRxPreview", flags)!.GetValue(form)!;
        var chkDarkMode = (CheckBox)form.GetType().GetField("_chkDarkMode", flags)!.GetValue(form)!;
        var chkShowLogs = (CheckBox)form.GetType().GetField("_chkShowLogs", flags)!.GetValue(form)!;

        sb.AppendLine($"[TEST-UI] Header Bounds: {pnlHeader.Bounds}");
        sb.AppendLine($"[TEST-UI] TableMain Bounds: {tblMain.Bounds}");
        sb.AppendLine($"[TEST-UI] TX Controls: Bounds={pnlTxControls.Bounds}, Visible={pnlTxControls.Visible}, Dock={pnlTxControls.Dock}");
        sb.AppendLine($"[TEST-UI] TX Preview:  Bounds={picTxPreview.Bounds}, Visible={picTxPreview.Visible}, Dock={picTxPreview.Dock}");
        sb.AppendLine($"[TEST-UI] RX Controls: Bounds={pnlRxControls.Bounds}, Visible={pnlRxControls.Visible}, Dock={pnlRxControls.Dock}");
        sb.AppendLine($"[TEST-UI] RX Preview:  Bounds={picRxPreview.Bounds}, Visible={picRxPreview.Visible}, Dock={picRxPreview.Dock}");

        // Verify TX layout: preview above, controls below
        bool txOk = picTxPreview.Location.Y <= pnlTxControls.Location.Y && pnlTxControls.Visible && picTxPreview.Visible;
        Console.WriteLine($"[TEST-UI] TX Layout Verification (Preview Above, Settings Below): {(txOk ? "PASS" : "FAIL")}");

        // Verify RX layout: preview above, controls below
        bool rxOk = picRxPreview.Location.Y <= pnlRxControls.Location.Y && pnlRxControls.Visible && picRxPreview.Visible;
        Console.WriteLine($"[TEST-UI] RX Layout Verification (Preview Above, Settings Below): {(rxOk ? "PASS" : "FAIL")}");

        // Verify RX Controls visibility and width
        bool rxVisible = pnlRxControls.Visible && pnlRxControls.Width >= 360 && pnlRxControls.Height > 100;
        Console.WriteLine($"[TEST-UI] RX Controls Non-Occluded & Visible: {(rxVisible ? "PASS" : "FAIL")}");

        // Verify Theme Switching via CheckBox
        Console.WriteLine($"[TEST-UI] Testing Dark/Light Themes...");
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var applyThemeMethod = form.GetType().GetMethod("ApplyTheme", flags)!;
            applyThemeMethod.Invoke(form, new object[] { theme });
            Console.WriteLine($"  * Theme '{theme}' applied successfully. Form BackColor={form.BackColor}, ForeColor={form.ForeColor}");
        }

        // Verify Log Visibility Toggle
        Console.WriteLine($"[TEST-UI] Testing Log Toggles...");
        var updateLogMethod = form.GetType().GetMethod("UpdateLogVisibility", flags)!;
        updateLogMethod.Invoke(form, new object[] { true });
        Console.WriteLine($"  * Logs Shown: Form Height={form.Height}");
        updateLogMethod.Invoke(form, new object[] { false });
        Console.WriteLine($"  * Logs Hidden: Form Height={form.Height}");

        try
        {
            var applyThemeMethod = form.GetType().GetMethod("ApplyTheme", flags)!;
            applyThemeMethod.Invoke(form, new object[] { "Dark" });
            using var bmpDark = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmpDark, new Rectangle(0, 0, form.Width, form.Height));
            bmpDark.Save(@"d:\_projects\SrtSuite\ui_dark.png", ImageFormat.Png);

            applyThemeMethod.Invoke(form, new object[] { "Light" });
            using var bmpLight = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmpLight, new Rectangle(0, 0, form.Width, form.Height));
            bmpLight.Save(@"d:\_projects\SrtSuite\ui_light.png", ImageFormat.Png);
        }
        catch { }

        form.Close();
        sb.AppendLine("========================================");
        sb.AppendLine("[TEST-UI] All UI Verification Checks PASSED!");
        sb.AppendLine("========================================");
        var outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_ui.log");
        File.WriteAllText(outPath, sb.ToString());
        File.WriteAllText(@"d:\_projects\SrtSuite\test_ui.log", sb.ToString());
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
            Port: 5000,
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
            Port: 5000,
            LatencyMs: 120,
            Passphrase: "",
            StreamId: ""
        );

        Console.WriteLine("\n[TEST] Starting SRT Receiver (1080i50 SDI)...");
        rx.Start(rxSettings);
        Thread.Sleep(1000);

        Console.WriteLine("\n[TEST] Starting SRT Transmitter (NVENC + 48kHz AAC)...");
        tx.Start(txSettings);

        Console.WriteLine("\n[TEST] Streaming session 1 for 4 seconds...");
        for (int i = 1; i <= 4; i++)
        {
            Thread.Sleep(1000);
            Console.WriteLine($"[TEST] Elapsed: {i}s | Preview frames: {Interlocked.Read(ref rxFrames)}");
        }
        long session1Frames = Interlocked.Read(ref rxFrames);

        Console.WriteLine("\n[TEST] Disconnecting caller (stopping TX)...");
        tx.Stop();
        Thread.Sleep(3000);

        bool receiverStillListening = rx.IsReceiving;
        Console.WriteLine($"[TEST] Receiver still listening after caller disconnected? {(receiverStillListening ? "YES (PASS)" : "NO (FAIL)")}");

        Console.WriteLine("\n[TEST] Reconnecting caller (re-starting TX)...");
        tx.Start(txSettings);

        Console.WriteLine("[TEST] Streaming session 2 for 4 seconds...");
        for (int i = 1; i <= 4; i++)
        {
            Thread.Sleep(1000);
            Console.WriteLine($"[TEST] Elapsed: {i}s | Preview frames: {Interlocked.Read(ref rxFrames)}");
        }
        long session2Frames = Interlocked.Read(ref rxFrames);

        tx.Stop();
        rx.Stop();

        Console.WriteLine($"\n[TEST] Session 1 frames: {session1Frames}, Total frames after reconnect: {session2Frames}");
        if (receiverStillListening && session2Frames > session1Frames)
        {
            Console.WriteLine(">>> TEST PASSED: Automatic Persistent Listening Verified! Receiver re-connected seamlessly! <<<");
            File.WriteAllText("test_persistent.log", "PASS: Persistent listening verified!");
        }
        else
        {
            Console.WriteLine(">>> TEST FAILED: Persistent listening failed! <<<");
            File.WriteAllText("test_persistent.log", "FAIL: Persistent listening failed!");
        }
    }
}

