using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SrtSuite;

public sealed partial class SrtTransmitterEngine : IDisposable
{
    private Process? _process;
    private readonly string _ffmpegPath;
    private readonly string _logFilePath;
    private CancellationTokenSource? _cts;
    private Thread? _previewThread;

    public event Action<string>? OnLog;
    public event Action<StreamStats>? OnStats;
    public event Action<bool>? OnStatusChanged;
    public event Action<Bitmap>? OnPreviewFrame;

    public bool IsTransmitting => _process is not null && !_process.HasExited;

    public SrtTransmitterEngine(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
        var root = @"c:\Users\vimlesh\Documents\vimlesh\srt";
        _logFilePath = Directory.Exists(root) 
            ? Path.Combine(root, "tx.log") 
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tx.log");
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(_logFilePath, message);
        }
        catch { }
        OnLog?.Invoke(message);
    }

    public string BuildSrtUrl(TxSettings settings)
    {
        var host = string.IsNullOrWhiteSpace(settings.Host) ? "127.0.0.1" : settings.Host.Trim();
        var port = settings.Port <= 0 ? 9998 : settings.Port;
        var mode = settings.Mode == SrtMode.Listener ? "listener" : "caller";
        var query = new List<string>
        {
            $"mode={mode}",
            $"latency={settings.LatencyMs * 1000}",
            "pkt_size=1316",
            "transtype=live",
            "rcvbuf=67108864",
            "sndbuf=67108864",
            "tlpktdrop=1"
        };

        if (!string.IsNullOrWhiteSpace(settings.Passphrase))
            query.Add($"passphrase={Uri.EscapeDataString(settings.Passphrase.Trim())}");

        if (!string.IsNullOrWhiteSpace(settings.StreamId))
            query.Add($"streamid={Uri.EscapeDataString(settings.StreamId.Trim())}");

        return $"srt://{host}:{port}?{string.Join("&", query)}";
    }

    public bool Start(TxSettings settings)
    {
        if (IsTransmitting)
        {
            Log("[TX ERROR] Transmission is already active.\n");
            return false;
        }

        var srtUrl = BuildSrtUrl(settings);
        var args = new List<string> { "-hide_banner" };

        if (settings.SourceType == SourceType.DeckLink)
        {
            args.Add("-f"); args.Add("decklink");
            if (!string.IsNullOrWhiteSpace(settings.VideoInput) && settings.VideoInput != "unset")
            {
                args.Add("-video_input"); args.Add(settings.VideoInput);
            }
            args.Add("-audio_input"); args.Add("embedded");
            args.Add("-format_code"); args.Add(string.IsNullOrWhiteSpace(settings.FormatCode) ? "Hi50" : settings.FormatCode);
            args.Add("-signal_loss_action"); args.Add("bars");
            args.Add("-audio_depth"); args.Add("16");
            args.Add("-channels"); args.Add("2");
            args.Add("-i"); args.Add(settings.DeckLinkDevice);

            var encoder = settings.Encoder;
            var bitrate = string.IsNullOrWhiteSpace(settings.Bitrate) ? "6000k" : settings.Bitrate;

            if (encoder == "h264_nvenc")
            {
                args.AddRange(new[] { "-c:v", "h264_nvenc", "-preset", "ll", "-zerolatency", "1", "-g", "25", "-bf", "0", "-b:v", bitrate, "-pix_fmt", "yuv420p" });
            }
            else
            {
                args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-tune", "zerolatency", "-g", "25", "-bf", "0", "-b:v", bitrate, "-pix_fmt", "yuv420p" });
            }

            args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2" });
        }
        else if (settings.SourceType == SourceType.File && !string.IsNullOrWhiteSpace(settings.FilePath))
        {
            if (settings.Loop)
            {
                args.Add("-stream_loop"); args.Add("-1");
            }
            args.Add("-re");
            args.Add("-i"); args.Add(settings.FilePath);

            var encoder = settings.Encoder;
            var bitrate = string.IsNullOrWhiteSpace(settings.Bitrate) ? "6000k" : settings.Bitrate;
            if (encoder == "h264_nvenc")
            {
                args.AddRange(new[] { "-c:v", "h264_nvenc", "-preset", "ll", "-zerolatency", "1", "-g", "25", "-bf", "0", "-b:v", bitrate, "-pix_fmt", "yuv420p" });
            }
            else
            {
                args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-tune", "zerolatency", "-g", "25", "-bf", "0", "-b:v", bitrate, "-pix_fmt", "yuv420p" });
            }
            args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2" });
        }
        else
        {
            // Synthetic SMPTE Color Bars
            args.Add("-re");
            args.Add("-f"); args.Add("lavfi");
            args.Add("-i"); args.Add("smptebars=size=1920x1080:rate=25");
            args.Add("-f"); args.Add("lavfi");
            args.Add("-i"); args.Add("sine=frequency=1000:sample_rate=48000");

            var encoder = settings.Encoder;
            var bitrate = string.IsNullOrWhiteSpace(settings.Bitrate) ? "6000k" : settings.Bitrate;
            if (encoder == "h264_nvenc")
            {
                args.AddRange(new[] { "-c:v", "h264_nvenc", "-preset", "ll", "-zerolatency", "1", "-g", "25", "-bf", "0", "-b:v", bitrate, "-pix_fmt", "yuv420p" });
            }
            else
            {
                args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-tune", "zerolatency", "-g", "25", "-bf", "0", "-b:v", bitrate, "-pix_fmt", "yuv420p" });
            }
            args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2" });
        }

        // Output #0: Main MPEG-TS stream to SRT (with explicit video and audio maps)
        if (settings.SourceType == SourceType.ColorBars)
        {
            args.AddRange(new[] { "-map", "0:v:0", "-map", "1:a:0?", "-max_muxing_queue_size", "4096", "-f", "mpegts", srtUrl });
        }
        else
        {
            args.AddRange(new[] { "-map", "0:v:0", "-map", "0:a:0?", "-max_muxing_queue_size", "4096", "-f", "mpegts", srtUrl });
        }

        // Output #1: Lightweight BGR24 preview stream (480x270 @ 5 fps) to stdout pipe
        args.AddRange(new[] { "-map", "0:v:0", "-vf", "fps=5,scale=480:270,format=bgr24", "-f", "rawvideo", "pipe:1" });

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            Log($"[TX ENGINE] Launching: {_ffmpegPath} {string.Join(" ", args)}\n");
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                Log(e.Data + "\n");
                ParseProgress(e.Data);
            };

            _process.Exited += (_, _) =>
            {
                Log($"[TX ENGINE] Transmitter stopped (Exit code: {_process?.ExitCode ?? 0})\n");
                _process = null;
                OnStatusChanged?.Invoke(false);
            };

            _process.Start();
            _process.BeginErrorReadLine();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var stdout = _process.StandardOutput.BaseStream;

            _previewThread = new Thread(() =>
            {
                const int width = 480;
                const int height = 270;
                const int frameBytes = width * height * 3;
                var buffer = new byte[frameBytes];

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        int offset = 0;
                        while (offset < frameBytes)
                        {
                            if (token.IsCancellationRequested) return;
                            int read = stdout.Read(buffer, offset, frameBytes - offset);
                            if (read <= 0) return;
                            offset += read;
                        }

                        var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                        var bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                        System.Runtime.InteropServices.Marshal.Copy(buffer, 0, bmpData.Scan0, frameBytes);
                        bmp.UnlockBits(bmpData);

                        OnPreviewFrame?.Invoke(bmp);
                    }
                }
                catch { }
            })
            {
                Name = "TxPreviewPump",
                IsBackground = true
            };
            _previewThread.Start();

            OnStatusChanged?.Invoke(true);
            return true;
        }
        catch (Exception ex)
        {
            Log($"[TX ERROR] Failed to start transmitter: {ex.Message}\n");
            _process = null;
            OnStatusChanged?.Invoke(false);
            return false;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        if (_process is not null && !_process.HasExited)
        {
            try
            {
                _process.Kill(true);
            }
            catch { }
            _process = null;
        }
        _previewThread?.Join(500);
        _previewThread = null;
        OnStatusChanged?.Invoke(false);
    }

    private void ParseProgress(string line)
    {
        var fpsMatch = FpsRegex().Match(line);
        var bitrateMatch = BitrateRegex().Match(line);
        var timeMatch = TimeRegex().Match(line);
        var speedMatch = SpeedRegex().Match(line);

        if (fpsMatch.Success || bitrateMatch.Success || timeMatch.Success || speedMatch.Success)
        {
            OnStats?.Invoke(new StreamStats(
                fpsMatch.Success ? fpsMatch.Groups[1].Value : null,
                bitrateMatch.Success ? bitrateMatch.Groups[1].Value : null,
                timeMatch.Success ? timeMatch.Groups[1].Value : null,
                speedMatch.Success ? speedMatch.Groups[1].Value : null
            ));
        }
    }

    [GeneratedRegex(@"fps=\s*([\d\.]+)")]
    private static partial Regex FpsRegex();

    [GeneratedRegex(@"bitrate=\s*([\d\.]+\s*\w+\/s)")]
    private static partial Regex BitrateRegex();

    [GeneratedRegex(@"time=\s*([\d:\.]+)")]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"speed=\s*([\d\.]+x)")]
    private static partial Regex SpeedRegex();

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
