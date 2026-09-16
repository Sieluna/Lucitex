using Lucitex.Core.Execution;
using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Decoding;

internal sealed class JpegHuffmanDecodeTable
{
    private const int k_FastBits = 9;
    private const int k_FastTableSize = 1 << k_FastBits;

    private readonly int[] _minCode = new int[17];
    private readonly int[] _maxCode = new int[17];
    private readonly int[] _valuePointer = new int[17];
    private readonly byte[] _values;
    private readonly ushort[] _fastTable = new ushort[k_FastTableSize];

    public JpegHuffmanDecodeTable(JpegHuffmanSpec spec)
    {
        _values = spec.Values;

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

        var pointer = 0;
        for (var length = 1; length <= 16; length++) {
            if (spec.Bits[length - 1] > 0) {
                _valuePointer[length] = pointer;
                _minCode[length] = huffCode[pointer];
                pointer += spec.Bits[length - 1];
                _maxCode[length] = huffCode[pointer - 1];
            }
            else {
                _maxCode[length] = -1;
            }
        }

        for (var i = 0; i < huffSize.Count; i++) {
            var length = huffSize[i];
            if (length > k_FastBits) {
                continue;
            }

            var shift = k_FastBits - length;
            var entryBase = huffCode[i] << shift;
            var entry = (ushort)((spec.Values[i] << 8) | length);
            var span = 1 << shift;
            for (var k = 0; k < span; k++) {
                _fastTable[entryBase + k] = entry;
            }
        }
    }

    public byte Decode(JpegBitReader reader)
    {
        var peeked = reader.PeekBits(k_FastBits);
        var entry = _fastTable[peeked];
        if (entry != 0) {
            reader.Advance(entry & 0xFF);
            return (byte)(entry >> 8);
        }

        return SlowDecode(reader);
    }

    private byte SlowDecode(JpegBitReader reader)
    {
        var code = reader.ReadBit();
        var length = 1;

        while (length <= 16 && code > _maxCode[length]) {
            code = (code << 1) | reader.ReadBit();
            length++;
        }

        if (length > 16) {
            throw new ImageFormatException("jpeg", "BadHuffmanCode", "Encountered an invalid Huffman code while decoding JPEG entropy data.");
        }

        var index = _valuePointer[length] + (code - _minCode[length]);
        return _values[index];
    }
}
