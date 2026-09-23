using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Encoding;

internal sealed class JpegHuffmanEncodeTable
{
    private readonly (int Code, int Length)[] _table = new (int, int)[256];

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
            _table[code.Symbol] = (code.Code, code.Length);
        }
    }

    public (int Code, int Length) Get(byte symbol) => _table[symbol];

    public JpegHuffmanSpec Spec { get; }
}
