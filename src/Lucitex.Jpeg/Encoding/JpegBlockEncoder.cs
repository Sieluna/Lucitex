using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Encoding;

internal static class JpegBlockEncoder
{
    public static void EncodeBlock(
        JpegBitWriter writer,
        ReadOnlySpan<short> coefficients,
        ref int dcPredictor,
        JpegHuffmanEncodeTable dcTable,
        JpegHuffmanEncodeTable acTable)
    {
        EncodeDc(writer, coefficients[0], ref dcPredictor, dcTable);
        EncodeAc(writer, coefficients, acTable);
    }

    public static void EncodeDc(JpegBitWriter writer, int dcCoefficient, ref int dcPredictor, JpegHuffmanEncodeTable dcTable)
    {
        var diff = dcCoefficient - dcPredictor;
        dcPredictor = dcCoefficient;

        var (dcSize, dcBits) = EncodeMagnitude(diff);
        var dcCode = dcTable.Get((byte)dcSize);
        writer.WriteBits(dcCode.Code, dcCode.Length);
        if (dcSize > 0) {
            writer.WriteBits(dcBits, dcSize);
        }
    }

    public static void EncodeAc(JpegBitWriter writer, ReadOnlySpan<short> coefficients, JpegHuffmanEncodeTable acTable)
    {
        var zigzag = JpegZigZag.Order;
        var run = 0;

        for (var k = 1; k < 64; k++) {
            var value = coefficients[zigzag[k]];
            if (value == 0) {
                run++;
                continue;
            }

            while (run > 15) {
                var zeroRunCode = acTable.Get(0xF0);
                writer.WriteBits(zeroRunCode.Code, zeroRunCode.Length);
                run -= 16;
            }

            var (size, bits) = EncodeMagnitude(value);
            var runSizeCode = acTable.Get((byte)((run << 4) | size));
            writer.WriteBits(runSizeCode.Code, runSizeCode.Length);
            writer.WriteBits(bits, size);
            run = 0;
        }

        if (run > 0) {
            var endOfBlockCode = acTable.Get(0x00);
            writer.WriteBits(endOfBlockCode.Code, endOfBlockCode.Length);
        }
    }

    private static (int Size, int Bits) EncodeMagnitude(int value)
    {
        if (value == 0) {
            return (0, 0);
        }

        var magnitude = Math.Abs(value);
        var size = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)magnitude);
        var bits = value > 0 ? value : value - 1 + (1 << size);
        return (size, bits);
    }
}
