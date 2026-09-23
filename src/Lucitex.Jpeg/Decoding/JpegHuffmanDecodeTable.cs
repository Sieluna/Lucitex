using Lucitex.Core.Execution;
using Lucitex.Jpeg.Format;
using System.Runtime.CompilerServices;
using System.Buffers;

namespace Lucitex.Jpeg.Decoding;

internal sealed class JpegHuffmanDecodeTable : IDisposable
{
    private const int k_FastBits = 12;
    private const int k_FastTableSize = 1 << k_FastBits;

    private readonly CodeRange[] _longCodes = new CodeRange[16 - k_FastBits];
    private readonly byte[] _values;
    private readonly LookupEntry[] _lookup;
    private readonly bool _pooled;
    private bool _disposed;

    private readonly record struct CodeRange(int Min, int Max, int ValueOffset);

    private readonly struct LookupEntry(int packed)
    {
        private const int k_LengthMask = 15;
        private const int k_CombinedLengthShift = 4;
        private const int k_SymbolShift = 8;
        private const int k_ValueShift = 16;
        private readonly int _packed = packed;

        public bool IsValid => _packed != 0;
        public int CodeLength => _packed & k_LengthMask;
        public int CombinedLength => (_packed >> k_CombinedLengthShift) & k_LengthMask;
        public byte Symbol => (byte)(_packed >> k_SymbolShift);
        public int ZeroRun => Symbol >> 4;
        public int Value => _packed >> k_ValueShift;

        public static LookupEntry FromCode(JpegHuffmanCode code) => new((code.Symbol << k_SymbolShift) | code.Length);

        public LookupEntry WithValue(int value, int combinedLength) => new(_packed | (combinedLength << k_CombinedLengthShift) | (value << k_ValueShift));
    }

    public static JpegHuffmanDecodeTable Create(JpegHuffmanSpec spec)
    {
        var start = spec.IsAc ? 2 : 0;
        for (var i = start; i < start + 2; i++) {
            var standard = JpegStandardTables.Huffman[i];
            if (spec.Bits.AsSpan().SequenceEqual(standard.Bits) && spec.Values.AsSpan().SequenceEqual(standard.Values)) {
                return Standard.Tables[i];
            }
        }
        return new JpegHuffmanDecodeTable(spec, pooled: true);
    }

    private static class Standard
    {
        internal static readonly JpegHuffmanDecodeTable[] Tables = JpegStandardTables.Huffman.Select(spec => new JpegHuffmanDecodeTable(spec)).ToArray();
    }

    public JpegHuffmanDecodeTable(JpegHuffmanSpec spec) : this(spec, pooled: false) { }

    private JpegHuffmanDecodeTable(JpegHuffmanSpec spec, bool pooled)
    {
        _values = spec.Values;
        _pooled = pooled;
        _lookup = pooled ? ArrayPool<LookupEntry>.Shared.Rent(k_FastTableSize) : new LookupEntry[k_FastTableSize];
        try {
            if (pooled) {
                _lookup.AsSpan(0, k_FastTableSize).Clear();
            }
            Initialize(spec);
        }
        catch {
            Dispose();
            throw;
        }
    }

    private void Initialize(JpegHuffmanSpec spec)
    {
        Span<JpegHuffmanCode> codes = stackalloc JpegHuffmanCode[256];
        var count = JpegHuffmanCode.Build(spec, codes);

        var pointer = 0;
        for (var length = 1; length <= 16; length++) {
            var lengthCount = spec.Bits[length - 1];
            if (length > k_FastBits) {
                _longCodes[length - k_FastBits - 1] = lengthCount == 0
                    ? new CodeRange(0, -1, 0)
                    : new CodeRange(codes[pointer].Code, codes[pointer + lengthCount - 1].Code, pointer - codes[pointer].Code);
            }
            pointer += lengthCount;
        }

        foreach (var code in codes[..count]) {
            var length = code.Length;
            if (length > k_FastBits) {
                continue;
            }

            var shift = k_FastBits - length;
            var entryBase = code.Code << shift;
            var entry = LookupEntry.FromCode(code);
            var span = 1 << shift;
            var size = code.Symbol & 15;
            if (!spec.IsAc || size == 0 || length + size > k_FastBits) {
                _lookup.AsSpan(entryBase, span).Fill(entry);
                continue;
            }
            var repeat = 1 << (k_FastBits - length - size);
            for (var bits = 0; bits < 1 << size; bits++) {
                var value = JpegMagnitude.Decode(bits, size);
                var combined = entry.WithValue(value, length + size);
                _lookup.AsSpan(entryBase + bits * repeat, repeat).Fill(combined);
            }
        }
    }

    public void Dispose()
    {
        if (!_pooled || _disposed) {
            return;
        }
        _disposed = true;
        ArrayPool<LookupEntry>.Shared.Return(_lookup);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeAc(JpegBitReader reader, out int run)
    {
        var entry = _lookup[reader.PeekBits(k_FastBits)];
        var combinedLength = entry.CombinedLength;
        if (combinedLength != 0) {
            reader.Advance(combinedLength);
            run = entry.ZeroRun;
            return entry.Value;
        }
        var symbol = DecodeEntry(reader, entry);
        run = symbol >> 4;
        return reader.ReceiveExtend(symbol & 15);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Decode(JpegBitReader reader)
    {
        return DecodeEntry(reader, _lookup[reader.PeekBits(k_FastBits)]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte DecodeEntry(JpegBitReader reader, LookupEntry entry)
    {
        if (entry.IsValid) {
            reader.Advance(entry.CodeLength);
            return entry.Symbol;
        }

        return SlowDecode(reader);
    }

    private byte SlowDecode(JpegBitReader reader)
    {
        var bits = reader.PeekBits(16);
        for (var i = 0; i < _longCodes.Length; i++) {
            var length = k_FastBits + 1 + i;
            var code = bits >> (16 - length);
            var range = _longCodes[i];
            if (code >= range.Min && code <= range.Max) {
                reader.Advance(length);
                return _values[code + range.ValueOffset];
            }
        }
        throw new ImageFormatException("jpeg", "BadHuffmanCode", "Encountered an invalid Huffman code while decoding JPEG entropy data.");
    }
}
