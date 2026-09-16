using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Encoding;

internal sealed class JpegHuffmanEncodeTable
{
    private readonly (int Code, int Length)[] _table = new (int, int)[256];

    public JpegHuffmanEncodeTable(JpegHuffmanSpec spec)
    {
        var huffSize = new List<int>();
        for (var length = 1; length <= 16; length++) {
            for (var count = 0; count < spec.Bits[length - 1]; count++) {
                huffSize.Add(length);
            }
        }

        var huffCode = new int[huffSize.Count];
        var code = 0;
        var sizeIndex = 0;
        for (var length = 1; length <= 16 && sizeIndex < huffSize.Count; length++) {
            while (sizeIndex < huffSize.Count && huffSize[sizeIndex] == length) {
                huffCode[sizeIndex] = code;
                code++;
                sizeIndex++;
            }

            code <<= 1;
        }

        for (var i = 0; i < spec.Values.Length; i++) {
            _table[spec.Values[i]] = (huffCode[i], huffSize[i]);
        }
    }

    public (int Code, int Length) Get(byte symbol) => _table[symbol];
}
