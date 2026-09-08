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
    private Process? _currentProcess;
    private CancellationTokenSource? _cts;
    private Thread? _supervisorThread;
    private TcpListener? _audioListener;
    private DeckLinkOutputEngine? _deckLinkEngine;
    private readonly AudioFifo _audioFifo = new();
    private volatile int _audioDelayMs;
    private volatile bool _isRunning;
    private volatile bool _enableSystemAudio = true;
    private SystemAudioOutput? _systemAudioOutput;
    private double _leftDbfs = -90.0;
    private double _rightDbfs = -90.0;

    public int AudioDelayMs
    {
        get => _audioDelayMs;
        set => _audioDelayMs = value;
    }

    public bool EnableSystemAudio
    {
        get => _enableSystemAudio;
        set
        {
            _enableSystemAudio = value;
            if (!value)
            {
                _systemAudioOutput?.Flush();
            }
        }
    }

    public event Action<string>? OnLog;
    public event Action<StreamStats>? OnStats;
    public event Action<bool>? OnStatusChanged;
    public event Action<Bitmap>? OnPreviewFrame;

    public bool IsReceiving => _isRunning;

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

    public bool Start(RxSettings settings)
    {
        if (_isRunning)
        {
            Log("[RX ERROR] Receiver is already active.\n");
            return false;
        }

        _isRunning = true;
        _enableSystemAudio = settings.EnableSystemAudio;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _supervisorThread = new Thread(() => SupervisorLoop(settings, token))
        {
            Name = "RxSupervisor",
            IsBackground = true
        };
        _supervisorThread.SetApartmentState(ApartmentState.MTA);
        _supervisorThread.Start();

        OnStatusChanged?.Invoke(true);
        return true;
    }

    public void Stop()
    {
        if (!_isRunning && _cts == null) return;

        _isRunning = false;
        _cts?.Cancel();

        if (_currentProcess is not null && !_currentProcess.HasExited)
        {
            try
            {
                _currentProcess.Kill(true);
            }
            catch { }
            _currentProcess = null;
        }

        try { _audioListener?.Stop(); } catch { }
        _audioListener = null;

        _supervisorThread?.Join(1500);
        _supervisorThread = null;

        _deckLinkEngine?.Dispose();
        _deckLinkEngine = null;

        _systemAudioOutput?.Dispose();
        _systemAudioOutput = null;

        _audioFifo.Clear();

        OnStatusChanged?.Invoke(false);
    }

    private void SupervisorLoop(RxSettings settings, CancellationToken token)
    {
        int width = 1920;
        int height = 1080;
        double frameRate = 25.0;
        var format = string.IsNullOrWhiteSpace(settings.FormatCode) ? "Hi50" : settings.FormatCode;
        DeckLinkInterop.ResolveDisplayMode(format, out width, out height, out frameRate);

        // 1. Initialize DeckLink SDI Output ONCE so sync is preserved across caller connects/disconnects
        if (settings.EnableDeckLinkPlayout && !string.IsNullOrWhiteSpace(settings.DeckLinkDevice))
        {
            try
            {
                _deckLinkEngine = new DeckLinkOutputEngine();
                _deckLinkEngine.Initialize(settings.DeckLinkDevice, format, enableAudio: true);
                Log($"[DECKLINK] Initialized SDI Output: {settings.DeckLinkDevice} ({format} {width}x{height} @ {frameRate:F2}fps)\n");
                Log("[DECKLINK] Standby sync active on SDI out\n");
            }
            catch (Exception ex)
            {
                Log($"[DECKLINK ERROR] Failed to initialize {settings.DeckLinkDevice}: {ex.Message}\n");
                _deckLinkEngine?.Dispose();
                _deckLinkEngine = null;
            }
        }

        int sessionIndex = 0;
        while (!token.IsCancellationRequested && _isRunning)
        {
            sessionIndex++;
            if (sessionIndex > 1)
            {
                if (settings.Mode == SrtMode.Listener)
                {
                    Log($"[RX LISTENER] Caller disconnected. Persistent listening active on port {(settings.Port <= 0 ? 5000 : settings.Port)}. Awaiting next connection...\n");
                    OnStats?.Invoke(new StreamStats(null, null, null, "Listening (waiting for caller)..."));
                }
                else
                {
                    Log($"[RX CALLER] Connection lost. Persistent mode active: reconnecting to {settings.Host}:{(settings.Port <= 0 ? 5000 : settings.Port)}...\n");
                    OnStats?.Invoke(new StreamStats(null, null, null, "Reconnecting..."));
                }
            }

            RunReceiveSession(settings, width, height, frameRate, token);

            if (token.IsCancellationRequested || !_isRunning)
            {
                break;
            }

            try
            {
                Thread.Sleep(500);
            }
            catch { }
        }

        _deckLinkEngine?.Dispose();
        _deckLinkEngine = null;
        _systemAudioOutput?.Dispose();
        _systemAudioOutput = null;
        Log("[DECKLINK] SDI Output closed.\n");

        _isRunning = false;
        OnStatusChanged?.Invoke(false);
    }

    private void RunReceiveSession(RxSettings settings, int width, int height, double frameRate, CancellationToken token)
    {
        int audioPort = 0;
        TcpListener? audioListener = null;
        try
        {
            audioListener = new TcpListener(IPAddress.Loopback, 0);
            audioListener.Start();
            audioPort = ((IPEndPoint)audioListener.LocalEndpoint).Port;
            _audioListener = audioListener;
        }
        catch (Exception ex)
        {
            Log($"[RX AUDIO] Warning: could not bind audio listener: {ex.Message}\n");
            _audioListener = null;
        }

        var srtUrl = BuildSrtUrl(settings);
        var args = new List<string>
        {
            "-hide_banner",
            "-fflags", "nobuffer",
            "-flags", "low_delay",
            "-probesize", "500000",
            "-analyzeduration", "1000000",
            "-thread_queue_size", "4096",
            "-i", srtUrl,
            "-map", "0:v:0",
            "-vf", $"setpts=PTS-STARTPTS,scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,format=uyvy422,fps=fps={frameRate:0.##}:round=near",
            "-pix_fmt", "uyvy422",
            "-fps_mode", "passthrough",
            "-max_muxing_queue_size", "4096",
            "-f", "rawvideo",
            "pipe:1"
        };

        if (audioPort > 0)
        {
            args.AddRange(new[]
            {
                "-map", "0:a:0?",
                "-af", "asetpts=PTS-STARTPTS,aresample=async=1000:first_pts=0",
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
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var sessionToken = sessionCts.Token;

        Process? process = null;
        Thread? videoPump = null;
        Thread? audioPump = null;

        try
        {
            Log($"[RX ENGINE] Launching: {_ffmpegPath} {string.Join(" ", args)}\n");
            process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _currentProcess = process;

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                Log(e.Data + "\n");
                ParseProgress(e.Data);
            };

            process.Start();
            process.BeginErrorReadLine();

            var stdout = process.StandardOutput.BaseStream;

            videoPump = new Thread(() => DedicatedVideoPump(settings, width, height, frameRate, stdout, sessionToken))
            {
                Name = "DeckLinkVideoPump",
                IsBackground = true
            };
            videoPump.SetApartmentState(ApartmentState.MTA);
            videoPump.Start();

            if (audioListener is not null)
            {
                audioPump = new Thread(() => DedicatedAudioPump(audioListener, sessionToken))
                {
                    Name = "DeckLinkAudioPump",
                    IsBackground = true
                };
                audioPump.SetApartmentState(ApartmentState.MTA);
                audioPump.Start();
            }

            while (!process.HasExited && !sessionToken.IsCancellationRequested)
            {
                Thread.Sleep(100);
            }

            Log($"[RX ENGINE] Receiver session closed (Exit code: {process.ExitCode})\n");
        }
        catch (Exception ex)
        {
            Log($"[RX ERROR] Failed in receiver session: {ex.Message}\n");
        }
        finally
        {
            sessionCts.Cancel();

            if (process is not null && !process.HasExited)
            {
                try { process.Kill(true); } catch { }
            }
            _currentProcess = null;

            try { audioListener?.Stop(); } catch { }
            if (_audioListener == audioListener) _audioListener = null;

            videoPump?.Join(500);
            audioPump?.Join(500);

            _audioFifo.Clear();
        }
    }

    private void DedicatedVideoPump(RxSettings settings, int width, int height, double frameRate, Stream videoStream, CancellationToken token)
    {
        int frameBytes = width * height * 2; // UYVY422 = 2 bytes per pixel
        var frameBuffer = new byte[frameBytes];
        var frameNumber = 0L;
        var stopwatch = new Stopwatch();
        var frameTicks = (long)(Stopwatch.Frequency / frameRate);

        int samplesPerFrame = (int)Math.Round(48000.0 / frameRate);
        int audioBytesPerFrame = samplesPerFrame * 4;
        var audioFrameBuffer = new byte[audioBytesPerFrame];

        Log("[RX ENGINE] Waiting for incoming SRT stream packets...\n");

        _audioFifo.Clear();
        _audioDelayMs = settings.AudioDelayMs;

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
                    Log("[RX ENGINE] First video frame received! Live playout active on DeckLink SDI.\n");
                }

                if (_deckLinkEngine is not null)
                {
                    int delayOffsetBytes = (_audioDelayMs * 192);
                    int maxAllowedBuffer = Math.Max(audioBytesPerFrame * 8, delayOffsetBytes + audioBytesPerFrame * 4);
                    if (_audioFifo.Count > maxAllowedBuffer)
                    {
                        _audioFifo.TrimTo(maxAllowedBuffer);
                    }

                    int bytesToRead = audioBytesPerFrame;
                    int readAudio = 0;

                    if (delayOffsetBytes > 0)
                    {
                        if (_audioFifo.Count >= delayOffsetBytes + bytesToRead)
                        {
                            readAudio = _audioFifo.Read(audioFrameBuffer, 0, bytesToRead);
                        }
                    }
                    else if (delayOffsetBytes < 0)
                    {
                        int advanceDrop = Math.Min(-delayOffsetBytes, _audioFifo.Count);
                        if (advanceDrop > 0)
                        {
                            _audioFifo.Read(new byte[advanceDrop], 0, advanceDrop);
                        }
                        readAudio = _audioFifo.Read(audioFrameBuffer, 0, bytesToRead);
                    }
                    else
                    {
                        readAudio = _audioFifo.Read(audioFrameBuffer, 0, bytesToRead);
                    }

                    if (readAudio < bytesToRead)
                    {
                        Array.Clear(audioFrameBuffer, readAudio, bytesToRead - readAudio);
                    }

                    _deckLinkEngine.WriteAudioPcm(audioFrameBuffer, bytesToRead, token);

                    bool ok = _deckLinkEngine.DisplayVideoFrame(frameBuffer);
                    if (!ok && frameNumber % 50 == 1)
                    {
                        Log($"[DECKLINK ERROR] Frame {frameNumber} failed: {_deckLinkEngine.LastErrorMessage}\n");
                    }
                }

                if (frameNumber == 1 || frameNumber % 4 == 0)
                {
                    try
                    {
                        var bmp = CreateRxPreviewBitmapWithMeters(frameBuffer, width, height, 480, 270, _leftDbfs, _rightDbfs);
                        OnPreviewFrame?.Invoke(bmp);
                    }
                    catch { }
                }

                int logCadence = Math.Max((int)(frameRate * 5), 25);
                if (frameNumber % logCadence == 0)
                {
                    Log($"[RX SDI] Continuous live playout: {frameNumber} frames ({frameNumber / frameRate:F1}s) | Audio: {(_deckLinkEngine?.TotalAudioSampleFramesWritten ?? 0) / 48000.0:F1}s\n");
                }

                var targetTicks = frameNumber * frameTicks;
                var remainingTicks = targetTicks - stopwatch.ElapsedTicks;
                if (remainingTicks > 0)
                {
                    var delayMs = (int)Math.Min(remainingTicks * 1000 / Stopwatch.Frequency, 35);
                    if (delayMs > 0) Thread.Sleep(delayMs);
                }
                else if (remainingTicks < -frameTicks * 10)
                {
                    stopwatch.Restart();
                    frameNumber = 0;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[RX PUMP ERROR] {ex.Message}\n");
        }
    }

    private void DedicatedAudioPump(TcpListener listener, CancellationToken token)
    {
        TcpClient? client = null;
        try
        {
            var acceptTask = listener.AcceptTcpClientAsync(token).AsTask();
            acceptTask.Wait(token);
            client = acceptTask.Result;

            Log("[RX AUDIO] Connected to decoded PCM audio stream from FFmpeg!\n");
            var stream = client.GetStream();
            var audioBuffer = new byte[7680];
            int remainder = 0;
            long totalAudioBytes = 0;

            while (!token.IsCancellationRequested)
            {
                int read = stream.Read(audioBuffer, remainder, audioBuffer.Length - remainder);
                if (read <= 0) break;

                int totalBytes = remainder + read;
                int usableBytes = (totalBytes / 4) * 4;
                remainder = totalBytes - usableBytes;

                if (usableBytes > 0)
                {
                    // Calculate real-time left and right audio peak dBFS
                    double maxL = 0;
                    double maxR = 0;
                    for (int i = 0; i < usableBytes; i += 4)
                    {
                        short l = (short)(audioBuffer[i] | (audioBuffer[i + 1] << 8));
                        short r = (short)(audioBuffer[i + 2] | (audioBuffer[i + 3] << 8));
                        if (Math.Abs(l) > maxL) maxL = Math.Abs(l);
                        if (Math.Abs(r) > maxR) maxR = Math.Abs(r);
                    }

                    double lDb = (maxL > 0) ? 20.0 * Math.Log10(maxL / 32768.0) : -90.0;
                    double rDb = (maxR > 0) ? 20.0 * Math.Log10(maxR / 32768.0) : -90.0;

                    // Fast attack, smooth decay
                    _leftDbfs = (lDb > _leftDbfs) ? lDb : (_leftDbfs * 0.88 + lDb * 0.12);
                    _rightDbfs = (rDb > _rightDbfs) ? rDb : (_rightDbfs * 0.88 + rDb * 0.12);

                    _audioFifo.Write(audioBuffer, 0, usableBytes);
                    totalAudioBytes += usableBytes;

                    if (_enableSystemAudio)
                    {
                        _systemAudioOutput ??= new SystemAudioOutput();
                        _systemAudioOutput.WriteAudio(audioBuffer, usableBytes);
                    }

                    if (totalAudioBytes % 960000 < usableBytes)
                    {
                        Log($"[RX AUDIO] Received embedded SDI audio: {totalAudioBytes / 192000.0:F1}s | FIFO buffer: {_audioFifo.Count / 192.0:F0}ms\n");
                    }
                }

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
            if (!token.IsCancellationRequested)
            {
                Log($"[RX AUDIO] Notice: {ex.Message}\n");
            }
        }
        finally
        {
            _leftDbfs = -90.0;
            _rightDbfs = -90.0;
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

    private static Bitmap CreateRxPreviewBitmapWithMeters(byte[] uyvy, int srcW, int srcH, int dstW, int dstH, double leftDbfs, double rightDbfs)
    {
        const int meterW = 20;
        int videoW = dstW - (meterW * 2);
        int videoH = dstH;

        var bmp = new Bitmap(dstW, dstH, PixelFormat.Format24bppRgb);
        var bmpData = bmp.LockBits(new Rectangle(0, 0, dstW, dstH), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        unsafe
        {
            byte* dstScan0 = (byte*)bmpData.Scan0;
            int dstStride = bmpData.Stride;

            for (int y = 0; y < videoH; y++)
            {
                int srcY = y * srcH / videoH;
                int srcRowOffset = srcY * srcW * 2;
                byte* dstRow = dstScan0 + (y * dstStride);

                // Video rendered at offset X = meterW (20)
                for (int vx = 0; vx < videoW; vx++)
                {
                    int srcX = vx * srcW / videoW;
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

                    int dstX = meterW + vx;
                    dstRow[dstX * 3] = (byte)b;
                    dstRow[dstX * 3 + 1] = (byte)g;
                    dstRow[dstX * 3 + 2] = (byte)r;
                }
            }
        }

        bmp.UnlockBits(bmpData);

        using (var g = Graphics.FromImage(bmp))
        {
            DrawMeterRail(g, new Rectangle(0, 0, meterW, videoH), leftDbfs);
            DrawMeterRail(g, new Rectangle(meterW + videoW, 0, meterW, videoH), rightDbfs);
        }

        return bmp;
    }

    private static void DrawMeterRail(Graphics g, Rectangle bounds, double dbfs)
    {
        // Rail background
        using (var bgBrush = new SolidBrush(Color.FromArgb(20, 22, 28)))
        {
            g.FillRectangle(bgBrush, bounds);
        }

        // Normalize dBFS: -60 dB to 0 dB mapped to 0.0 .. 1.0
        double normalized = Math.Clamp((dbfs + 60.0) / 60.0, 0.0, 1.0);

        const int insetX = 3;
        const int insetY = 3;
        int barWidth = Math.Max(1, bounds.Width - (insetX * 2));
        int totalBarHeight = bounds.Height - (insetY * 2);

        if (normalized > 0.01)
        {
            int levelHeight = Math.Max(2, (int)Math.Round(totalBarHeight * normalized));
            int barTop = bounds.Bottom - insetY - levelHeight;
            var levelBounds = new Rectangle(bounds.X + insetX, barTop, barWidth, levelHeight);

            // Broadcast level color:
            // > -3 dBFS: Red (peak/clipping)
            // > -9 dBFS: Amber/Gold (high)
            // Normal: Green
            Color fillColor;
            if (dbfs > -3.0)
            {
                fillColor = Color.FromArgb(224, 82, 82);
            }
            else if (dbfs > -9.0)
            {
                fillColor = Color.FromArgb(232, 181, 105);
            }
            else
            {
                fillColor = Color.FromArgb(91, 190, 125);
            }

            using var levelBrush = new SolidBrush(fillColor);
            g.FillRectangle(levelBrush, levelBounds);

            // Subtle bright cap line at the peak
            using var capPen = new Pen(Color.White, 1f);
            g.DrawLine(capPen, levelBounds.Left, levelBounds.Top, levelBounds.Right - 1, levelBounds.Top);
        }

        // Subtle tick marks at -6dB, -12dB, -18dB, -24dB
        using (var tickPen = new Pen(Color.FromArgb(70, 78, 88), 1f))
        {
            double[] tickDbs = { -6.0, -12.0, -18.0, -24.0 };
            foreach (var tDb in tickDbs)
            {
                double tNorm = (tDb + 60.0) / 60.0;
                int tickY = bounds.Bottom - insetY - (int)Math.Round(totalBarHeight * tNorm);
                g.DrawLine(tickPen, bounds.X + 1, tickY, bounds.X + 4, tickY);
                g.DrawLine(tickPen, bounds.Right - 5, tickY, bounds.Right - 2, tickY);
            }
        }

        // Rail border outline
        using (var borderPen = new Pen(Color.FromArgb(86, 97, 109), 2f))
        {
            g.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }
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
