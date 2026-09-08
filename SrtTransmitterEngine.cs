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
        var port = settings.Port <= 0 ? 5000 : settings.Port;
        var mode = settings.Mode == SrtMode.Listener ? "listener" : "caller";
        var query = new List<string>
        {
            $"mode={mode}",
            $"latency={settings.LatencyMs * 1000}",
            "pkt_size=1316",
            "transtype=live",
            "rcvbuf=67108864",
            "sndbuf=67108864",
            "tlpktdrop=1",
            "linger=0"
        };

        if (mode == "caller")
        {
            query.Add("connect_timeout=10000");
        }

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

        string audioInputLabel;
        if (settings.SourceType == SourceType.ColorBars)
        {
            audioInputLabel = "[1:a]";
        }
        else if (settings.SourceType == SourceType.File && !string.IsNullOrWhiteSpace(settings.FilePath))
        {
            bool hasAudio = ProbeHasAudioStream(settings.FilePath, _ffmpegPath);
            if (hasAudio)
            {
                audioInputLabel = "[0:a]";
            }
            else
            {
                args.Add("-f"); args.Add("lavfi");
                args.Add("-i"); args.Add("anullsrc=channel_layout=stereo:sample_rate=48000");
                audioInputLabel = "[1:a]";
            }
        }
        else
        {
            audioInputLabel = "[0:a]";
        }

        // Preview filter complex: Left VU (20px), Center Video (440px), Right VU (20px) -> Total 480x270 BGR24
        var filterComplex = $"{audioInputLabel}aresample=48000,aformat=sample_fmts=s16:channel_layouts=stereo,asplit=2[l_src][r_src];" +
            "[0:v]scale=440:270:force_original_aspect_ratio=decrease,pad=440:270:(ow-iw)/2:(oh-ih)/2,fps=8,format=yuv420p[v_scaled];" +
            "[l_src]pan=mono|c0=c0,showvolume=r=8:w=80:h=270:f=0.92:b=1:t=0:v=1:dm=1:o=v:ds=log:p=0.18:m=r,scale=20:270,format=yuv420p,drawbox=x=0:y=0:w=iw:h=ih:color=0x56616d:t=2[left_bar];" +
            "[r_src]pan=mono|c0=c1,showvolume=r=8:w=80:h=270:f=0.92:b=1:t=0:v=1:dm=1:o=v:ds=log:p=0.18:m=r,scale=20:270,format=yuv420p,drawbox=x=0:y=0:w=iw:h=ih:color=0x56616d:t=2[right_bar];" +
            "[left_bar][v_scaled][right_bar]hstack=inputs=3,format=bgr24[tx_preview]";

        args.AddRange(new[] { "-filter_complex", filterComplex });

        // Output #0: Main MPEG-TS stream to SRT (with explicit video and audio maps)
        if (settings.SourceType == SourceType.ColorBars || audioInputLabel == "[1:a]")
        {
            args.AddRange(new[] { "-map", "0:v:0", "-map", "1:a:0?", "-max_muxing_queue_size", "4096", "-f", "mpegts", srtUrl });
        }
        else
        {
            args.AddRange(new[] { "-map", "0:v:0", "-map", "0:a:0?", "-max_muxing_queue_size", "4096", "-f", "mpegts", srtUrl });
        }

        // Output #1: Lightweight BGR24 preview stream (480x270 with left/right audio meters @ 8 fps) to stdout pipe
        args.AddRange(new[] { "-map", "[tx_preview]", "-f", "rawvideo", "pipe:1" });

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

    private static bool ProbeHasAudioStream(string filePath, string ffmpegPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(ffmpegPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            var probePath = Path.Combine(dir, "ffprobe.exe");
            if (!File.Exists(probePath)) return true;

            var psi = new ProcessStartInfo
            {
                FileName = probePath,
                Arguments = $"-v error -select_streams a -show_entries stream=codec_type -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1000);
                return !string.IsNullOrWhiteSpace(output);
            }
        }
        catch { }
        return true;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
