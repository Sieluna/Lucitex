using Lucitex.Core.Execution;
using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Decoding;

internal sealed class JpegBitReader(JpegByteCursor cursor)
{
    private const int k_RefillTarget = 24;

    private uint _bitBuffer;
    private int _bitCount;

    public byte? PendingMarker { get; private set; }

    public int PeekBits(int count)
    {
        Refill();

        if (_bitCount >= count) {
            return (int)((_bitBuffer >> (_bitCount - count)) & ((1u << count) - 1));
        }

        var have = _bitCount > 0 ? (int)(_bitBuffer & ((1u << _bitCount) - 1)) : 0;
        return have << (count - _bitCount);
    }

    public void Advance(int count) => _bitCount -= count;

    public int ReadBit()
    {
        Refill();

        if (_bitCount == 0) {
            return 0;
        }

        _bitCount--;
        return (int)((_bitBuffer >> _bitCount) & 1);
    }

    public int ReadBits(int count)
    {
        if (count == 0) {
            return 0;
        }

        var value = PeekBits(count);
        Advance(count);
        return value;
    }

    public int ReceiveExtend(int size)
    {
        if (size == 0) {
            return 0;
        }

        var value = ReadBits(size);
        var threshold = 1 << (size - 1);
        return value < threshold ? value - (1 << size) + 1 : value;
    }

    public void DiscardBitBuffer()
    {
        _bitBuffer = 0;
        _bitCount = 0;
    }

    public void ConsumeRestartMarker(byte expectedMarker)
    {
        DiscardBitBuffer();

        var marker = PendingMarker ?? cursor.ReadMarker();
        PendingMarker = null;

        if (marker != expectedMarker) {
            throw new ImageFormatException("jpeg", "BadRestart", $"Expected restart marker 0x{expectedMarker:X2} but found 0x{marker:X2}.");
        }
    }

    public void FinishSegment()
    {
        if (PendingMarker is { } marker) {
            cursor.PushBackMarker(marker);
            PendingMarker = null;
        }

        DiscardBitBuffer();
    }

    private void Refill()
    {
        while (_bitCount < k_RefillTarget && FillByte()) {
        }
    }

    private bool FillByte()
    {
        if (PendingMarker is not null) {
            return false;
        }

        var value = cursor.ReadByte();
        if (value == JpegMarkers.Prefix) {
            var next = cursor.ReadByte();
            if (next != JpegMarkers.Padding) {
                PendingMarker = next;
                return false;
            }
        }

        _bitBuffer = (_bitBuffer << 8) | value;
        _bitCount += 8;
        return true;
    }
}
