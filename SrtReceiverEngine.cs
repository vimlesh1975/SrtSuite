using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace SrtSuite;

public sealed partial class SrtReceiverEngine : IDisposable
{
    private readonly string _ffmpegPath;
    private readonly string _logFilePath;
    private Process? _process;
    private CancellationTokenSource? _cts;
    private Thread? _videoPumpThread;
    private Thread? _audioPumpThread;
    private TcpListener? _audioListener;
    private DeckLinkOutputEngine? _deckLinkEngine;

    public event Action<string>? OnLog;
    public event Action<StreamStats>? OnStats;
    public event Action<bool>? OnStatusChanged;
    public event Action<Bitmap>? OnPreviewFrame;

    public bool IsReceiving => _process is not null && !_process.HasExited;

    public SrtReceiverEngine(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
        var root = @"c:\Users\vimlesh\Documents\vimlesh\srt";
        _logFilePath = Directory.Exists(root)
            ? Path.Combine(root, "rx.log")
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rx.log");
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

    public string BuildSrtUrl(RxSettings settings)
    {
        var host = string.IsNullOrWhiteSpace(settings.Host) ? "0.0.0.0" : settings.Host.Trim();
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

    public bool Start(RxSettings settings)
    {
        if (IsReceiving)
        {
            Log("[RX ERROR] Receiver is already active.\n");
            return false;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // 1. Setup local TCP loopback for low-latency 48kHz 32-bit PCM audio
        int audioPort = 0;
        try
        {
            _audioListener = new TcpListener(IPAddress.Loopback, 0);
            _audioListener.Start();
            audioPort = ((IPEndPoint)_audioListener.LocalEndpoint).Port;
        }
        catch (Exception ex)
        {
            Log($"[RX AUDIO] Warning: could not bind audio listener: {ex.Message}\n");
            _audioListener = null;
        }

        // 2. Prepare FFmpeg decode arguments
        var srtUrl = BuildSrtUrl(settings);
        var args = new List<string>
        {
            "-hide_banner",
            "-fflags", "+genpts+nobuffer",
            "-flags", "low_delay",
            "-thread_queue_size", "4096",
            "-i", srtUrl,
            "-map", "0:v:0",
            "-vf", "scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,format=uyvy422,fps=25",
            "-pix_fmt", "uyvy422",
            "-max_muxing_queue_size", "4096",
            "-f", "rawvideo",
            "pipe:1"
        };

        if (audioPort > 0)
        {
            args.AddRange(new[]
            {
                "-map", "0:a:0?",
                "-c:a", "pcm_s16le",
                "-ar", "48000",
                "-ac", "2",
                "-max_muxing_queue_size", "4096",
                "-f", "s16le",
                $"tcp://127.0.0.1:{audioPort}"
            });
        }

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            Log($"[RX ENGINE] Launching: {_ffmpegPath} {string.Join(" ", args)}\n");
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                Log(e.Data + "\n");
                ParseProgress(e.Data);
            };

            _process.Exited += (_, _) =>
            {
                Log($"[RX ENGINE] Receiver closed (Exit code: {_process?.ExitCode ?? 0})\n");
                Stop();
            };

            _process.Start();
            _process.BeginErrorReadLine();

            // 3. Start dedicated Video Pump Thread (MTA)
            var stdout = _process.StandardOutput.BaseStream;
            var deckLinkReady = new ManualResetEventSlim(false);

            _videoPumpThread = new Thread(() => DedicatedVideoPump(settings, stdout, deckLinkReady, token))
            {
                Name = "DeckLinkVideoPump",
                IsBackground = true
            };
            _videoPumpThread.SetApartmentState(ApartmentState.MTA);
            _videoPumpThread.Start();

            // 4. Start dedicated Audio Pump Thread (MTA)
            if (_audioListener is not null)
            {
                _audioPumpThread = new Thread(() => DedicatedAudioPump(_audioListener, deckLinkReady, token))
                {
                    Name = "DeckLinkAudioPump",
                    IsBackground = true
                };
                _audioPumpThread.SetApartmentState(ApartmentState.MTA);
                _audioPumpThread.Start();
            }

            OnStatusChanged?.Invoke(true);
            return true;
        }
        catch (Exception ex)
        {
            Log($"[RX ERROR] Failed to start receiver: {ex.Message}\n");
            Stop();
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

        try { _audioListener?.Stop(); } catch { }
        _audioListener = null;

        _videoPumpThread?.Join(1000);
        _videoPumpThread = null;

        _audioPumpThread?.Join(1000);
        _audioPumpThread = null;

        OnStatusChanged?.Invoke(false);
    }

    private void DedicatedVideoPump(RxSettings settings, Stream videoStream, ManualResetEventSlim deckLinkReady, CancellationToken token)
    {
        if (settings.EnableDeckLinkPlayout && !string.IsNullOrWhiteSpace(settings.DeckLinkDevice))
        {
            try
            {
                _deckLinkEngine = new DeckLinkOutputEngine();
                var format = string.IsNullOrWhiteSpace(settings.FormatCode) ? "Hi50" : settings.FormatCode;
                _deckLinkEngine.Initialize(settings.DeckLinkDevice, format, enableAudio: true);
                Log($"[DECKLINK] Initialized SDI Output: {settings.DeckLinkDevice} ({format} 1080i50)\n");
                Log("[DECKLINK] Standby colorbars active on SDI out (locking monitor sync)\n");
            }
            catch (Exception ex)
            {
                Log($"[DECKLINK ERROR] Failed to initialize {settings.DeckLinkDevice}: {ex.Message}\n");
                _deckLinkEngine?.Dispose();
                _deckLinkEngine = null;
            }
        }

        deckLinkReady.Set();

        const int width = 1920;
        const int height = 1080;
        const int frameBytes = width * height * 2; // UYVY422 = 4,147,200 bytes
        var frameBuffer = new byte[frameBytes];
        var frameNumber = 0L;
        var stopwatch = new Stopwatch();
        var frameTicks = Stopwatch.Frequency / 25L; // 25.00 fps reference clock

        Log("[RX ENGINE] Waiting for incoming SRT stream packets...\n");

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!ReadExactBuffer(videoStream, frameBuffer, frameBytes, token))
                {
                    Log("[RX ENGINE] Video stream ended or decoder disconnected.\n");
                    break;
                }

                frameNumber++;

                if (frameNumber == 1)
                {
                    stopwatch.Start();
                    Log("[RX ENGINE] First video frame received! Live stream active on DeckLink SDI.\n");
                }

                // 1. Output to physical DeckLink SDI hardware
                if (_deckLinkEngine is not null)
                {
                    bool ok = _deckLinkEngine.DisplayVideoFrame(frameBuffer);
                    if (!ok && frameNumber % 50 == 1)
                    {
                        Log($"[DECKLINK ERROR] Frame {frameNumber} failed: {_deckLinkEngine.LastErrorMessage}\n");
                    }
                }

                // 2. In-App Preview (frame 1 immediately, then every 4th frame = ~6 fps preview to save UI CPU)
                if (frameNumber == 1 || frameNumber % 4 == 0)
                {
                    try
                    {
                        var bmp = ConvertUyvyToBitmapFast(frameBuffer, width, height, 480, 270);
                        OnPreviewFrame?.Invoke(bmp);
                    }
                    catch { }
                }

                if (frameNumber % 125 == 0)
                {
                    Log($"[RX SDI] Continuous live playout: {frameNumber} frames played to DeckLink ({frameNumber / 25.0:F1}s)\n");
                }

                // 3. Frame pacing (capped to prevent artificial pipeline stall)
                var targetTicks = frameNumber * frameTicks;
                var remainingTicks = targetTicks - stopwatch.ElapsedTicks;
                if (remainingTicks > 0)
                {
                    var delayMs = (int)Math.Min(remainingTicks * 1000 / Stopwatch.Frequency, 35);
                    if (delayMs > 0)
                    {
                        Thread.Sleep(delayMs);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[RX PUMP ERROR] {ex.Message}\n");
        }
        finally
        {
            _deckLinkEngine?.Dispose();
            _deckLinkEngine = null;
            Log("[DECKLINK] SDI Output closed.\n");
        }
    }

    private void DedicatedAudioPump(TcpListener listener, ManualResetEventSlim deckLinkReady, CancellationToken token)
    {
        try
        {
            deckLinkReady.Wait(token);
        }
        catch (OperationCanceledException) { return; }

        TcpClient? client = null;
        try
        {
            // Accept the incoming connection from FFmpeg asynchronously to honor cancellation
            var acceptTask = listener.AcceptTcpClientAsync(token).AsTask();
            acceptTask.Wait(token);
            client = acceptTask.Result;

            Log("[RX AUDIO] Connected to decoded PCM audio stream from FFmpeg!\n");
            var stream = client.GetStream();
            var audioBuffer = new byte[7680]; // ~40ms chunk of 48kHz stereo 16-bit PCM (1920 sample frames * 4 bytes)
            int remainder = 0;
            long totalAudioBytes = 0;

            while (!token.IsCancellationRequested)
            {
                int read = stream.Read(audioBuffer, remainder, audioBuffer.Length - remainder);
                if (read <= 0) break;

                int totalBytes = remainder + read;
                int usableBytes = (totalBytes / 4) * 4; // exact 4-byte frames for 16-bit stereo
                remainder = totalBytes - usableBytes;

                if (usableBytes > 0)
                {
                    _deckLinkEngine?.WriteAudioPcm(audioBuffer, usableBytes, token);
                    totalAudioBytes += usableBytes;

                    if (totalAudioBytes % 960000 < usableBytes)
                    {
                        Log($"[RX AUDIO] Received embedded SDI audio: {totalAudioBytes / 192000.0:F1}s | SDI samples played: {_deckLinkEngine?.TotalAudioSampleFramesWritten ?? 0}\n");
                    }
                }

                // If any odd 1..3 bytes remain, copy them to start of buffer for next read
                if (remainder > 0)
                {
                    Buffer.BlockCopy(audioBuffer, usableBytes, audioBuffer, 0, remainder);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        catch (Exception ex)
        {
            Log($"[RX AUDIO] Notice: {ex.Message}\n");
        }
        finally
        {
            client?.Dispose();
        }
    }

    private static bool ReadExactBuffer(Stream stream, byte[] buffer, int count, CancellationToken token)
    {
        int offset = 0;
        while (offset < count)
        {
            if (token.IsCancellationRequested) return false;
            int read = stream.Read(buffer, offset, count - offset);
            if (read <= 0) return false;
            offset += read;
        }
        return true;
    }

    private static Bitmap ConvertUyvyToBitmapFast(byte[] uyvy, int srcW, int srcH, int dstW, int dstH)
    {
        var bmp = new Bitmap(dstW, dstH, PixelFormat.Format24bppRgb);
        var bmpData = bmp.LockBits(new Rectangle(0, 0, dstW, dstH), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        unsafe
        {
            byte* dstScan0 = (byte*)bmpData.Scan0;
            int dstStride = bmpData.Stride;

            for (int y = 0; y < dstH; y++)
            {
                int srcY = y * srcH / dstH;
                int srcRowOffset = srcY * srcW * 2;
                byte* dstRow = dstScan0 + (y * dstStride);

                for (int x = 0; x < dstW; x++)
                {
                    int srcX = x * srcW / dstW;
                    int pixelPair = srcX / 2;
                    int uyvyIdx = srcRowOffset + (pixelPair * 4);

                    int u = uyvy[uyvyIdx];
                    int yVal = (srcX % 2 == 0) ? uyvy[uyvyIdx + 1] : uyvy[uyvyIdx + 3];
                    int v = uyvy[uyvyIdx + 2];

                    int c = yVal - 16;
                    int d = u - 128;
                    int e = v - 128;

                    int r = Math.Clamp((298 * c + 409 * e + 128) >> 8, 0, 255);
                    int g = Math.Clamp((298 * c - 100 * d - 208 * e + 128) >> 8, 0, 255);
                    int b = Math.Clamp((298 * c + 516 * d + 128) >> 8, 0, 255);

                    dstRow[x * 3] = (byte)b;
                    dstRow[x * 3 + 1] = (byte)g;
                    dstRow[x * 3 + 2] = (byte)r;
                }
            }
        }

        bmp.UnlockBits(bmpData);
        return bmp;
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
