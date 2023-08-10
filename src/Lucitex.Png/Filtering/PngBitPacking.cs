namespace Lucitex.Png.Filtering;

internal static class PngBitPacking
{
    public static uint ReadSample(ReadOnlySpan<byte> row, int sampleIndex, int bitDepth)
    {
        switch (bitDepth) {
            case 8:
                return row[sampleIndex];
            case 16:
                return (uint)((row[sampleIndex * 2] << 8) | row[(sampleIndex * 2) + 1]);
            default:
                var bitPosition = sampleIndex * bitDepth;
                var byteIndex = bitPosition / 8;
                var bitOffset = bitPosition % 8;
                var shift = 8 - bitDepth - bitOffset;
                var mask = (1 << bitDepth) - 1;
                return (uint)((row[byteIndex] >> shift) & mask);
        }
    }

    public static void WriteSample(Span<byte> row, int sampleIndex, int bitDepth, uint value)
    {
        switch (bitDepth) {
            case 8:
                row[sampleIndex] = (byte)value;
                break;
            case 16:
                row[sampleIndex * 2] = (byte)(value >> 8);
                row[(sampleIndex * 2) + 1] = (byte)value;
                break;
            default:
                var bitPosition = sampleIndex * bitDepth;
                var byteIndex = bitPosition / 8;
                var bitOffset = bitPosition % 8;
                var shift = 8 - bitDepth - bitOffset;
                var mask = (1 << bitDepth) - 1;
                row[byteIndex] = (byte)((row[byteIndex] & ~(mask << shift)) | ((int)(value & mask) << shift));
                break;
        }
    }
}
