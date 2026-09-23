using Lucitex.Core.Execution;

namespace Lucitex.Jpeg.Format;

internal readonly record struct JpegHuffmanCode(byte Symbol, byte Length, ushort Code)
{
    public static int Build(JpegHuffmanSpec spec, Span<JpegHuffmanCode> destination)
    {
        if (spec.Bits.Length != 16 || spec.Values.Length > 256) {
            throw InvalidTable();
        }

        var code = 0;
        var index = 0;
        for (var length = 1; length <= 16; length++) {
            var count = spec.Bits[length - 1];
            if (code + count > (1 << length) || index + count > spec.Values.Length) {
                throw InvalidTable();
            }
            for (var i = 0; i < count; i++) {
                destination[index] = new JpegHuffmanCode(spec.Values[index], (byte)length, (ushort)code++);
                index++;
            }
            code <<= 1;
        }
        if (index != spec.Values.Length) {
            throw InvalidTable();
        }
        return index;
    }

    private static ImageFormatException InvalidTable() => new("jpeg", "BadHuffmanTable", "JPEG Huffman code lengths do not form a valid table.");
}
