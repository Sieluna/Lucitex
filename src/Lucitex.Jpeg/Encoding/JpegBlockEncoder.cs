using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Encoding;

internal static class JpegBlockEncoder
{
    public static void EncodeBlock(
        JpegBitWriter writer,
        JpegBlockInfo block,
        ReadOnlySpan<JpegEntropyToken> tokens,
        ref int dcPredictor,
        JpegHuffmanEncodeTable dcTable,
        JpegHuffmanEncodeTable acTable)
    {
        EncodeDc(writer, block.Dc, ref dcPredictor, dcTable);
        EncodeAc(writer, tokens[..block.TokenCount], acTable);
    }

    public static void EncodeDc(JpegBitWriter writer, int dcCoefficient, ref int dcPredictor, JpegHuffmanEncodeTable dcTable)
    {
        var diff = dcCoefficient - dcPredictor;
        dcPredictor = dcCoefficient;

        var dcSize = JpegMagnitude.GetSize(diff);
        var dcCode = dcTable.Get((byte)dcSize);
        writer.WriteBits((dcCode.Code << dcSize) | JpegMagnitude.Encode(diff, dcSize), dcCode.Length + dcSize);
    }

    public static void EncodeAc(JpegBitWriter writer, ReadOnlySpan<JpegEntropyToken> tokens, JpegHuffmanEncodeTable acTable)
    {
        foreach (var token in tokens) {
            var code = acTable.Get(token.Symbol);
            writer.WriteBits((code.Code << token.BitCount) | token.Bits, code.Length + token.BitCount);
        }
    }
}
