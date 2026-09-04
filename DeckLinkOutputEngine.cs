using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DeckLinkAPI;

namespace SrtSuite;

public sealed class DeckLinkOutputEngine : IDisposable
{
    private readonly object _outputLock = new();
    private IDeckLink? _deckLink;
    private IDeckLinkOutput_v14_2_1? _output;
    private bool _videoEnabled;
    private bool _audioEnabled;
    private int _audioChannels = 2;

    public long TotalAudioSampleFramesWritten { get; private set; }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int RowBytes { get; private set; }
    public int FrameBytes { get; private set; }
    public double FrameRate { get; private set; }
    public string DeviceName { get; private set; } = string.Empty;
    public string? LastErrorMessage { get; private set; }

    public bool Initialize(string deviceName, string formatCode = "Hi50", bool enableAudio = true, int audioChannels = 2)
    {
        Shutdown();

        _deckLink = DeckLinkInterop.FindDevice(deviceName);
        if (_deckLink is null)
        {
            throw new InvalidOperationException($"DeckLink output device not found: '{deviceName}'");
        }

        _output = (IDeckLinkOutput_v14_2_1)_deckLink;
        DeviceName = deviceName;
        _audioChannels = audioChannels;
        TotalAudioSampleFramesWritten = 0;

        var mode = DeckLinkInterop.ResolveDisplayMode(formatCode, out var w, out var h, out var fps);
        Width = w;
        Height = h;
        FrameRate = fps;
        RowBytes = Width * 2; // UYVY 8-bit = 2 bytes per pixel
        FrameBytes = RowBytes * Height;

        lock (_outputLock)
        {
            _output.EnableVideoOutput(mode, _BMDVideoOutputFlags.bmdVideoOutputFlagDefault);
            _videoEnabled = true;
        }

        // Output an initial standby pattern so SDI monitors immediately sync lock to 1080i50
        DisplayStandbyPattern();

        if (enableAudio)
        {
            try
            {
                lock (_outputLock)
                {
                    _output.EnableAudioOutput(
                        _BMDAudioSampleRate.bmdAudioSampleRate48kHz,
                        _BMDAudioSampleType.bmdAudioSampleType16bitInteger,
                        (uint)audioChannels,
                        _BMDAudioOutputStreamType.bmdAudioOutputStreamContinuous);
                    _audioEnabled = true;
                }
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"Audio init: {ex.Message}";
            }
        }

        return true;
    }

    public void DisplayStandbyPattern()
    {
        var buffer = new byte[FrameBytes];
        int barWidth = Math.Max(Width / 8, 1);
        byte[][] bars = new byte[][]
        {
            new byte[] { 128, 235, 128, 235 }, // 75% White
            new byte[] { 16,  210, 146, 210 }, // Yellow
            new byte[] { 166, 170, 16,  170 }, // Cyan
            new byte[] { 54,  145, 34,  145 }, // Green
            new byte[] { 202, 106, 222, 106 }, // Magenta
            new byte[] { 90,  81,  240, 81  }, // Red
            new byte[] { 240, 41,  110, 41  }, // Blue
            new byte[] { 128, 16,  128, 16  }  // Black
        };

        for (int y = 0; y < Height; y++)
        {
            int rowOffset = y * RowBytes;
            for (int x = 0; x < Width; x += 2)
            {
                int barIdx = Math.Min(x / barWidth, 7);
                var bar = bars[barIdx];
                int idx = rowOffset + (x * 2);
                buffer[idx]     = bar[0];
                buffer[idx + 1] = bar[1];
                buffer[idx + 2] = bar[2];
                buffer[idx + 3] = bar[3];
            }
        }

        DisplayVideoFrame(buffer);
    }

    public bool DisplayVideoFrame(byte[] uyvyBuffer)
    {
        if (_output is null || !_videoEnabled) return false;

        IDeckLinkMutableVideoFrame_v14_2_1? frame = null;
        try
        {
            lock (_outputLock)
            {
                if (_output is null || !_videoEnabled) return false;
                _output.CreateVideoFrame(
                    Width,
                    Height,
                    RowBytes,
                    _BMDPixelFormat.bmdFormat8BitYUV,
                    _BMDFrameFlags.bmdFrameFlagDefault,
                    out frame);
            }

            if (frame is null)
            {
                LastErrorMessage = "CreateVideoFrame returned null";
                return false;
            }

            frame.GetBytes(out var bufferPtr);
            var copyLength = Math.Min(uyvyBuffer.Length, FrameBytes);
            Marshal.Copy(uyvyBuffer, 0, bufferPtr, copyLength);

            lock (_outputLock)
            {
                if (_output is null || !_videoEnabled) return false;
                _output.DisplayVideoFrameSync((IDeckLinkVideoFrame_v14_2_1)frame);
            }

            return true;
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            if (frame is not null)
            {
                Marshal.ReleaseComObject(frame);
            }
        }
    }

    public bool WriteAudioSync(nint sampleBufferPtr, uint sampleFrameCount, out uint written)
    {
        written = 0;
        if (_output is null || !_audioEnabled) return false;
        try
        {
            lock (_outputLock)
            {
                if (_output is null || !_audioEnabled) return false;
                _output.WriteAudioSamplesSync(sampleBufferPtr, sampleFrameCount, out written);
                return true;
            }
        }
        catch (Exception ex)
        {
            LastErrorMessage = $"WriteAudioSync: {ex.Message}";
            return false;
        }
    }

    public unsafe void WriteAudioPcm(byte[] pcmData, int byteCount, CancellationToken token = default)
    {
        if (!_audioEnabled || _output is null || byteCount <= 0) return;

        int bytesPerSampleFrame = _audioChannels * 2; // 16-bit = 2 bytes per channel
        int sampleFrameCount = byteCount / bytesPerSampleFrame;
        if (sampleFrameCount == 0) return;

        fixed (byte* ptr = pcmData)
        {
            uint totalWritten = 0;
            while (totalWritten < (uint)sampleFrameCount && !token.IsCancellationRequested)
            {
                var remaining = (uint)sampleFrameCount - totalWritten;
                uint written = 0;

                try
                {
                    lock (_outputLock)
                    {
                        if (_output is null || !_audioEnabled) return;
                        _output.WriteAudioSamplesSync(
                            (nint)(ptr + (totalWritten * bytesPerSampleFrame)),
                            remaining,
                            out written);
                    }
                }
                catch (Exception ex)
                {
                    LastErrorMessage = $"WriteAudioSamplesSync: {ex.Message}";
                    break;
                }

                if (written > 0)
                {
                    totalWritten += written;
                    TotalAudioSampleFramesWritten += written;
                }
                else
                {
                    // Hardware FIFO temporarily full; sleep briefly to let SDI playout drain FIFO
                    Thread.Sleep(2);
                }
            }
        }
    }

    public void Shutdown()
    {
        lock (_outputLock)
        {
            if (_output is not null)
            {
                if (_audioEnabled)
                {
                    try
                    {
                        _output.FlushBufferedAudioSamples();
                    }
                    catch { }

                    try
                    {
                        _output.DisableAudioOutput();
                    }
                    catch { }
                    _audioEnabled = false;
                }

                if (_videoEnabled)
                {
                    try
                    {
                        _output.DisableVideoOutput();
                    }
                    catch { }
                    _videoEnabled = false;
                }

                Marshal.ReleaseComObject(_output);
                _output = null;
            }

            if (_deckLink is not null)
            {
                Marshal.ReleaseComObject(_deckLink);
                _deckLink = null;
            }
        }
    }

    public void Dispose()
    {
        Shutdown();
        GC.SuppressFinalize(this);
    }
}
