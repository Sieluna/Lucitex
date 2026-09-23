using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Lucitex.Jpeg.Encoding;

internal sealed class JpegBitWriter : IDisposable
{
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(4096);
    private int _length;
    private ulong _bitBuffer;
    private int _bitCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBits(int value, int length)
    {
        _bitBuffer = (_bitBuffer << length) | ((uint)value & ((1UL << length) - 1));
        _bitCount += length;
        if (_bitCount >= 32) {
            _bitCount -= 32;
            EnsureCapacity(4);
            BinaryPrimitives.WriteUInt32BigEndian(_buffer.AsSpan(_length, 4), (uint)(_bitBuffer >> _bitCount));
            _length += 4;
        }
    }

    public void PadAndFlush()
    {
        var padding = (8 - _bitCount) & 7;
        _bitBuffer = (_bitBuffer << padding) | ((1UL << padding) - 1);
        _bitCount += padding;
        EnsureCapacity(4);
        while (_bitCount > 0) {
            _bitCount -= 8;
            _buffer[_length++] = (byte)(_bitBuffer >> _bitCount);
        }
    }

    public void CopyTo(Stream stream)
    {
        Span<byte> stuffed = stackalloc byte[4096];
        for (var offset = 0; offset < _length;) {
            var count = Math.Min(2048, _length - offset);
            var source = _buffer.AsSpan(offset, count);
            var written = 0;
            foreach (var value in source) {
                stuffed[written++] = value;
                if (value == 0xFF) {
                    stuffed[written++] = 0;
                }
            }
            stream.Write(stuffed[..written]);
            offset += count;
        }
    }

    public void Dispose()
    {
        if (_buffer.Length != 0) {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int additional)
    {
        if (_length + additional > _buffer.Length) {
            Grow(additional);
        }
    }

    private void Grow(int additional)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(checked(_length + additional), checked(_buffer.Length * 2)));
        _buffer.AsSpan(0, _length).CopyTo(buffer);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = buffer;
    }
}
