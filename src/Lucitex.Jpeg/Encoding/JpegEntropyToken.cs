using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Encoding;

internal readonly struct JpegEntropyToken(byte symbol, int value)
{
    public readonly ushort Bits = (ushort)JpegMagnitude.Encode(value, symbol & 15);
    public readonly byte Symbol = symbol;
    public readonly byte BitCount = (byte)(symbol & 15);
}

internal readonly record struct JpegBlockInfo(short Dc, byte TokenCount);
