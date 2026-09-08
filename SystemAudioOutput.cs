using System.Runtime.InteropServices;

namespace SrtSuite;

public sealed class SystemAudioOutput : IDisposable
{
    private const int AudioSampleRate = 48000;
    private const int OutputChannels = 2;
    private const int OutputBitsPerSample = 16;
    private const int BytesPerSample = 2;
    private const int FrameBytes = OutputChannels * BytesPerSample;
    private const uint WaveMapper = unchecked((uint)-1);
    private const ushort WaveFormatPcm = 1;
    private const uint WhdrDone = 0x00000001;
    private const int MaxPendingBuffers = 24;

    private readonly object _lock = new();
    private readonly List<WaveBuffer> _pendingBuffers = new();
    private IntPtr _hWaveOut = IntPtr.Zero;
    private bool _disposed;
    private double _volume = 1.0;

    public double Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0, 1.0);
    }

    public bool IsOpen => _hWaveOut != IntPtr.Zero && !_disposed;

    public SystemAudioOutput()
    {
        Initialize();
    }

    private void Initialize()
    {
        var format = new WaveFormatEx
        {
            wFormatTag = WaveFormatPcm,
            nChannels = OutputChannels,
            nSamplesPerSec = AudioSampleRate,
            wBitsPerSample = OutputBitsPerSample,
            nBlockAlign = (ushort)(OutputChannels * BytesPerSample),
            cbSize = 0
        };
        format.nAvgBytesPerSec = format.nSamplesPerSec * format.nBlockAlign;

        int result = waveOutOpen(out _hWaveOut, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0);
        if (result != 0)
        {
            _hWaveOut = IntPtr.Zero;
        }
    }

    public void WriteAudio(byte[] pcm16, int byteCount)
    {
        if (_disposed || _hWaveOut == IntPtr.Zero || pcm16 == null || byteCount <= 0)
            return;

        int validBytes = Math.Min(byteCount, pcm16.Length);
        validBytes = (validBytes / FrameBytes) * FrameBytes;
        if (validBytes <= 0) return;

        byte[] bufferData;
        if (Math.Abs(_volume - 1.0) > 0.001)
        {
            bufferData = new byte[validBytes];
            for (int i = 0; i < validBytes; i += 2)
            {
                short sample = (short)(pcm16[i] | (pcm16[i + 1] << 8));
                short scaled = (short)Math.Clamp(Math.Round(sample * _volume), short.MinValue, short.MaxValue);
                bufferData[i] = (byte)(scaled & 0xFF);
                bufferData[i + 1] = (byte)((scaled >> 8) & 0xFF);
            }
        }
        else
        {
            bufferData = new byte[validBytes];
            Buffer.BlockCopy(pcm16, 0, bufferData, 0, validBytes);
        }

        lock (_lock)
        {
            if (_disposed || _hWaveOut == IntPtr.Zero) return;

            CleanupCompletedBuffers();

            if (_pendingBuffers.Count >= MaxPendingBuffers)
            {
                return;
            }

            var buffer = new WaveBuffer(bufferData);
            try
            {
                PrepareAndWriteBuffer(buffer);
                _pendingBuffers.Add(buffer);
            }
            catch
            {
                buffer.Dispose();
            }
        }
    }

    private void PrepareAndWriteBuffer(WaveBuffer buffer)
    {
        var header = new WaveHeader
        {
            lpData = buffer.Data,
            dwBufferLength = (uint)buffer.Length,
            dwFlags = 0
        };
        Marshal.StructureToPtr(header, buffer.Header, false);

        int headerSize = Marshal.SizeOf<WaveHeader>();
        int prepResult = waveOutPrepareHeader(_hWaveOut, buffer.Header, (uint)headerSize);
        if (prepResult != 0)
        {
            buffer.Dispose();
            return;
        }

        buffer.Prepared = true;
        int writeResult = waveOutWrite(_hWaveOut, buffer.Header, (uint)headerSize);
        if (writeResult != 0)
        {
            UnprepareBuffer(buffer);
            buffer.Dispose();
        }
    }

    private void CleanupCompletedBuffers()
    {
        for (int i = _pendingBuffers.Count - 1; i >= 0; i--)
        {
            var buffer = _pendingBuffers[i];
            var header = Marshal.PtrToStructure<WaveHeader>(buffer.Header);
            if ((header.dwFlags & WhdrDone) != 0)
            {
                UnprepareBuffer(buffer);
                buffer.Dispose();
                _pendingBuffers.RemoveAt(i);
            }
        }
    }

    private void UnprepareBuffer(WaveBuffer buffer)
    {
        if (!buffer.Prepared || _hWaveOut == IntPtr.Zero) return;
        waveOutUnprepareHeader(_hWaveOut, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>());
        buffer.Prepared = false;
    }

    public void Flush()
    {
        lock (_lock)
        {
            if (_hWaveOut != IntPtr.Zero)
            {
                waveOutReset(_hWaveOut);
                foreach (var buf in _pendingBuffers)
                {
                    UnprepareBuffer(buf);
                    buf.Dispose();
                }
                _pendingBuffers.Clear();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_hWaveOut != IntPtr.Zero)
            {
                waveOutReset(_hWaveOut);
                foreach (var buf in _pendingBuffers)
                {
                    UnprepareBuffer(buf);
                    buf.Dispose();
                }
                _pendingBuffers.Clear();

                waveOutClose(_hWaveOut);
                _hWaveOut = IntPtr.Zero;
            }
        }
        GC.SuppressFinalize(this);
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr hWaveOut, uint uDeviceID, ref WaveFormatEx lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr hWaveOut);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr hWaveOut);

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public int nSamplesPerSec;
        public int nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    private sealed class WaveBuffer : IDisposable
    {
        public IntPtr Data { get; }
        public IntPtr Header { get; }
        public int Length { get; }
        public bool Prepared { get; set; }

        public WaveBuffer(byte[] data)
        {
            Length = data.Length;
            Data = Marshal.AllocHGlobal(data.Length);
            Header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
            Marshal.Copy(data, 0, Data, data.Length);
        }

        public void Dispose()
        {
            if (Header != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Header);
            }
            if (Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Data);
            }
        }
    }
}
