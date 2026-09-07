namespace SrtSuite;

/// <summary>
/// Thread-safe circular FIFO buffer for 16-bit stereo (4-byte frame aligned) PCM audio.
/// Ensures lossless transfer between the asynchronous TCP reader and the synchronous video pump.
/// </summary>
public sealed class AudioFifo
{
    private readonly byte[] _buffer;
    private int _head;
    private int _tail;
    private int _count;
    private readonly object _lock = new();

    public AudioFifo(int capacityBytes = 48000 * 4 * 2) // Default ~2.0 seconds of 48kHz stereo (384,000 bytes)
    {
        // Enforce 4-byte alignment on capacity
        _buffer = new byte[(capacityBytes / 4) * 4];
    }

    /// <summary>
    /// Current number of usable audio bytes in the buffer. Always a multiple of 4.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock) return _count;
        }
    }

    /// <summary>
    /// Writes incoming PCM bytes into the FIFO. Enforces 4-byte sample frame alignment.
    /// If buffer capacity is exceeded, drops the oldest samples in exact 4-byte increments.
    /// </summary>
    public void Write(byte[] data, int offset, int count)
    {
        // Must be multiple of 4 bytes (16-bit stereo = 4 bytes per sample frame)
        count = (count / 4) * 4;
        if (count <= 0) return;

        lock (_lock)
        {
            // If overflow, drop oldest samples to make room
            if (_count + count > _buffer.Length)
            {
                int drop = (_count + count) - _buffer.Length;
                drop = ((drop + 3) / 4) * 4;
                if (drop > _count) drop = _count;
                _head = (_head + drop) % _buffer.Length;
                _count -= drop;
            }

            int firstChunk = Math.Min(count, _buffer.Length - _tail);
            Buffer.BlockCopy(data, offset, _buffer, _tail, firstChunk);
            if (count > firstChunk)
            {
                Buffer.BlockCopy(data, offset + firstChunk, _buffer, 0, count - firstChunk);
            }
            _tail = (_tail + count) % _buffer.Length;
            _count += count;
        }
    }

    /// <summary>
    /// Reads up to count bytes from the FIFO into destination.
    /// Returns the actual number of bytes read (always a multiple of 4).
    /// </summary>
    public int Read(byte[] destination, int offset, int count)
    {
        count = (count / 4) * 4;
        if (count <= 0) return 0;

        lock (_lock)
        {
            int toRead = Math.Min(count, _count);
            if (toRead <= 0) return 0;

            int firstChunk = Math.Min(toRead, _buffer.Length - _head);
            Buffer.BlockCopy(_buffer, _head, destination, offset, firstChunk);
            if (toRead > firstChunk)
            {
                Buffer.BlockCopy(_buffer, 0, destination, offset + firstChunk, toRead - firstChunk);
            }
            _head = (_head + toRead) % _buffer.Length;
            _count -= toRead;
            return toRead;
        }
    }

    /// <summary>
    /// Trims the FIFO so that at most maxBytes remain (dropping oldest samples).
    /// Used to eliminate any accumulated latency if video stalls or pauses.
    /// </summary>
    public void TrimTo(int maxBytes)
    {
        maxBytes = (maxBytes / 4) * 4;
        lock (_lock)
        {
            if (_count > maxBytes)
            {
                int drop = _count - maxBytes;
                drop = ((drop + 3) / 4) * 4;
                if (drop > _count) drop = _count;
                _head = (_head + drop) % _buffer.Length;
                _count -= drop;
            }
        }
    }

    /// <summary>
    /// Clears all buffered audio data.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }
}
