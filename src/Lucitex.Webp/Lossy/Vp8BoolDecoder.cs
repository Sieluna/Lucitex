using System.Runtime.CompilerServices;

namespace Lucitex.Webp.Lossy;

internal sealed class Vp8BoolDecoder
{
    private readonly byte[] _data;
    private int _offset;
    private uint _range;
    private uint _value;
    private int _bitCount;

    public Vp8BoolDecoder(byte[] data)
    {
        _data = data;
        _range = 255;
        _bitCount = 0;
        _value = ((uint)ReadByte() << 8) | ReadByte();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte ReadByte() => (uint)_offset < (uint)_data.Length ? _data[_offset++] : (byte)0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetBool(int probability)
    {
        var split = 1u + (((_range - 1) * (uint)probability) >> 8);
        var bigSplit = split << 8;
        int result;
        if (_value >= bigSplit) {
            result = 1;
            _range -= split;
            _value -= bigSplit;
        }
        else {
            result = 0;
            _range = split;
        }
        while (_range < 128) {
            _value <<= 1;
            _range <<= 1;
            if (++_bitCount == 8) {
                _bitCount = 0;
                _value |= ReadByte();
            }
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetFlag() => GetBool(128);

    public uint GetLiteral(int bits)
    {
        uint v = 0;
        for (var i = 0; i < bits; i++) {
            v = (v << 1) + (uint)GetFlag();
        }
        return v;
    }

    public int GetSignedValue(int bits)
    {
        var magnitude = (int)GetLiteral(bits);
        return GetFlag() != 0 ? -magnitude : magnitude;
    }

    public int GetTree(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probabilities, int start = 0)
    {
        var i = start;
        while ((i = tree[i + GetBool(probabilities[i >> 1])]) > 0) {
        }
        return -i;
    }
}
