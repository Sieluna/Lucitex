using Lucitex.Core.Execution;
using Lucitex.Jpeg.Format;
using System.Runtime.CompilerServices;

namespace Lucitex.Jpeg.Decoding;

internal sealed class JpegBitReader(JpegByteCursor cursor)
{
    private const int k_RefillTarget = 56;

    private ulong _bitBuffer;
    private int _bitCount;

    public byte? PendingMarker { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int PeekBits(int count)
    {
        if (_bitCount < count) {
            Refill();
        }

        if (_bitCount >= count) {
            return (int)((_bitBuffer >> (_bitCount - count)) & ((1ul << count) - 1));
        }

        var have = _bitCount > 0 ? (int)(_bitBuffer & ((1ul << _bitCount) - 1)) : 0;
        return have << (count - _bitCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        if (count > _bitCount) {
            throw new ImageFormatException("jpeg", "BadEntropyData", "JPEG entropy data ended before the coefficient was complete.");
        }
        _bitCount -= count;
    }

    public int ReadBit()
    {
        return ReadBits(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadBits(int count)
    {
        if (count == 0) {
            return 0;
        }

        var value = PeekBits(count);
        Advance(count);
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReceiveExtend(int size)
    {
        if (size == 0) {
            return 0;
        }

        return JpegMagnitude.Decode(ReadBits(size), size);
    }

    public void DiscardBitBuffer()
    {
        _bitBuffer = 0;
        _bitCount = 0;
    }

    public void ConsumeRestartMarker(byte expectedMarker)
    {
        ValidateRemainingBits();
        DiscardBitBuffer();

        var marker = PendingMarker ?? cursor.ReadMarker();
        PendingMarker = null;

        if (marker != expectedMarker) {
            throw new ImageFormatException("jpeg", "BadRestart", $"Expected restart marker 0x{expectedMarker:X2} but found 0x{marker:X2}.");
        }
    }

    public void FinishSegment()
    {
        ValidateRemainingBits();
        if (PendingMarker is { } marker) {
            cursor.PushBackMarker(marker);
            PendingMarker = null;
        }

        DiscardBitBuffer();
    }

    private void ValidateRemainingBits()
    {
        if (_bitCount >= 8) {
            throw new ImageFormatException("jpeg", "BadEntropyData", "JPEG entropy segment contains extraneous bytes after its coefficients.");
        }
    }

    private void Refill()
    {
        var count = (63 - _bitCount) >> 3;
        if (PendingMarker is null && cursor.TryReadEntropyBytes(count, out var bytes)) {
            _bitBuffer = (_bitBuffer << (count * 8)) | bytes;
            _bitCount += count * 8;
            return;
        }
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
