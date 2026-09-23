using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Encoding;

internal sealed class JpegHuffmanEncodeTable
{
    private readonly uint[] _table = new uint[256];

    public static JpegHuffmanEncodeTable GetStandard(int id, bool isAc) => Standard.Tables[id + (isAc ? 2 : 0)];

    private static class Standard
    {
        internal static readonly JpegHuffmanEncodeTable[] Tables = JpegStandardTables.Huffman.Select(spec => new JpegHuffmanEncodeTable(spec)).ToArray();
    }

    public JpegHuffmanEncodeTable(JpegHuffmanSpec spec)
    {
        Spec = spec;
        Span<JpegHuffmanCode> codes = stackalloc JpegHuffmanCode[256];
        var count = JpegHuffmanCode.Build(spec, codes);
        foreach (var code in codes[..count]) {
            _table[code.Symbol] = ((uint)code.Code << 16) | (uint)code.Length;
        }
    }

    public (int Code, int Length) Get(byte symbol)
    {
        var entry = _table[symbol];
        return ((int)(entry >> 16), (int)(entry & 31));
    }

    public JpegHuffmanSpec Spec { get; }
}
