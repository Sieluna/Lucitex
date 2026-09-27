using System.Buffers.Binary;

namespace Lucitex.Webp.Lossless;

internal sealed class Vp8LBitWriter(Stream? stream, WebpMemory memory, int bufferSize = 8192) : IDisposable
{
    private readonly WebpBuffer<byte> _buffer = memory.Rent<byte>(bufferSize);
    private ulong _bits;
    private int _pending;
    private int _position;

    public long TotalBits { get; private set; }

    public ReadOnlySpan<byte> WrittenSpan => _buffer.Span[..checked((int)((TotalBits + 7) / 8))];

    public void Reset()
    {
        _bits = 0;
        _pending = 0;
        _position = 0;
        TotalBits = 0;
    }

    public void Write(uint value, int count)
    {
        if (count == 0) {
            return;
        }
        _bits |= (ulong)(value & ((1u << count) - 1)) << _pending;
        _pending += count;
        TotalBits += count;
        if (_pending >= 32) {
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.Span[_position..], (uint)_bits);
            _position += 4;
            _bits >>= 32;
            _pending -= 32;
            if (_position == _buffer.Length) {
                FlushBuffer();
            }
        }
    }

    public void Finish()
    {
        while (_pending > 0) {
            _buffer.Span[_position++] = (byte)_bits;
            _bits >>= 8;
            _pending -= 8;
        }
        _pending = 0;
        FlushBuffer();
    }

    private void FlushBuffer()
    {
        if (stream is not null) {
            stream.Write(_buffer.Span[.._position]);
            _position = 0;
        }
    }

    public void Dispose() => _buffer.Dispose();
}
