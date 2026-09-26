using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Lucitex.Webp.Lossless;

internal ref struct Vp8LBitReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _offset;
    private ulong _bits;
    private int _available;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Peek(int count) => (uint)PeekWindow(count) & ((1u << count) - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong PeekWindow(int count)
    {
        if (_available < count) {
            if (_data.Length - _offset >= 8) {
                var bytes = (64 - _available) >> 3;
                _bits |= BinaryPrimitives.ReadUInt64LittleEndian(_data[_offset..]) << _available;
                _offset += bytes;
                _available += bytes * 8;
            }
            else {
                while (_available < count && _offset < _data.Length) {
                    _bits |= (ulong)_data[_offset++] << _available;
                    _available += 8;
                }
            }
        }
        return _bits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int count)
    {
        if (_available < count) {
            throw new EndOfStreamException("Truncated VP8L bitstream.");
        }
        _bits >>= count;
        _available -= count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(int count)
    {
        var result = Peek(count);
        Skip(count);
        return (int)result;
    }

    public int ReadPrefix(int prefix)
    {
        if (prefix < 4) {
            return prefix + 1;
        }
        var extra = (prefix - 2) >> 1;
        return ((2 + (prefix & 1)) << extra) + Read(extra) + 1;
    }
}
