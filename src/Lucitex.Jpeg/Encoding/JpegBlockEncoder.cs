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

        var dcSize = JpegMagnitude.GetSize(diff);
        var dcCode = dcTable.Get((byte)dcSize);
        writer.WriteBits(dcCode.Code, dcCode.Length);
        WriteMagnitude(writer, diff, dcSize);
    }

    public static void EncodeAc(JpegBitWriter writer, ReadOnlySpan<short> coefficients, JpegHuffmanEncodeTable acTable)
    {
        foreach (var (symbol, value) in new JpegAcSymbols(coefficients)) {
            var code = acTable.Get(symbol);
            writer.WriteBits(code.Code, code.Length);
            WriteMagnitude(writer, value, symbol & 15);
        }
    }

    private static void WriteMagnitude(JpegBitWriter writer, int value, int size)
    {
        if (size > 0) {
            writer.WriteBits(JpegMagnitude.Encode(value, size), size);
        }
    }
}
