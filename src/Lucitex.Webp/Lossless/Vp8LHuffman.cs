using System.Runtime.CompilerServices;

namespace Lucitex.Webp.Lossless;

internal sealed class Vp8LHuffman : IDisposable
{
    private const int k_RootBits = 8;
    private readonly WebpBuffer<int>? _table;
    private readonly int _singleSymbol;

    internal static ReadOnlySpan<byte> CodeLengthOrder => [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    private Vp8LHuffman(int singleSymbol) => _singleSymbol = singleSymbol;

    private Vp8LHuffman(WebpBuffer<int> table)
    {
        _table = table;
        _singleSymbol = -1;
    }

    public static Vp8LHuffman Read(ref Vp8LBitReader reader, int alphabetSize, WebpMemory memory)
    {
        Span<byte> lengths = stackalloc byte[alphabetSize];
        lengths.Clear();
        if (reader.Read(1) != 0) {
            var count = reader.Read(1) + 1;
            var symbol = reader.Read(reader.Read(1) == 0 ? 1 : 8);
            if (symbol >= alphabetSize) {
                throw new InvalidDataException("Invalid VP8L Huffman symbol.");
            }
            lengths[symbol] = 1;
            if (count == 2) {
                symbol = reader.Read(8);
                if (symbol >= alphabetSize) {
                    throw new InvalidDataException("Invalid VP8L Huffman symbol.");
                }
                lengths[symbol] = 1;
            }
        }
        else {
            Span<byte> codeLengths = stackalloc byte[19];
            codeLengths.Clear();
            var count = reader.Read(4) + 4;
            for (var i = 0; i < count; i++) {
                codeLengths[CodeLengthOrder[i]] = (byte)reader.Read(3);
            }
            using var codes = Build(codeLengths, memory);
            var maxSymbols = alphabetSize;
            if (reader.Read(1) != 0) {
                maxSymbols = 2 + reader.Read(2 + (2 * reader.Read(3)));
                if (maxSymbols > alphabetSize) {
                    throw new InvalidDataException("Invalid VP8L code length count.");
                }
            }
            var index = 0;
            byte previous = 8;
            while (index < alphabetSize && maxSymbols-- > 0) {
                var code = codes.Decode(ref reader);
                if (code < 16) {
                    lengths[index++] = (byte)code;
                    if (code != 0) {
                        previous = (byte)code;
                    }
                }
                else {
                    var repeat = code switch {
                        16 => reader.Read(2) + 3,
                        17 => reader.Read(3) + 3,
                        18 => reader.Read(7) + 11,
                        _ => throw new InvalidDataException("Invalid VP8L code length."),
                    };
                    if (repeat > alphabetSize - index) {
                        throw new InvalidDataException("VP8L code length repeat exceeds its alphabet.");
                    }
                    lengths.Slice(index, repeat).Fill(code == 16 ? previous : (byte)0);
                    index += repeat;
                }
            }
        }
        return Build(lengths, memory);
    }

    private static Vp8LHuffman Build(ReadOnlySpan<byte> lengths, WebpMemory memory)
    {
        Span<int> counts = stackalloc int[16];
        counts.Clear();
        var symbols = 0;
        var last = 0;
        for (var i = 0; i < lengths.Length; i++) {
            if (lengths[i] != 0) {
                counts[lengths[i]]++;
                symbols++;
                last = i;
            }
        }
        if (symbols == 1 && lengths[last] == 1) {
            return new Vp8LHuffman(last);
        }
        var space = 1;
        for (var bits = 1; bits <= 15; bits++) {
            space = (space * 2) - counts[bits];
            if (space < 0) {
                throw new InvalidDataException("Oversubscribed VP8L Huffman tree.");
            }
        }
        if (space != 0) {
            throw new InvalidDataException("Incomplete VP8L Huffman tree.");
        }
        Span<ushort> codes = stackalloc ushort[lengths.Length];
        BuildCodes(lengths, codes);
        Span<byte> suffixBits = stackalloc byte[256];
        suffixBits.Clear();
        for (var i = 0; i < lengths.Length; i++) {
            if (lengths[i] > k_RootBits) {
                var prefix = codes[i] & 255;
                suffixBits[prefix] = Math.Max(suffixBits[prefix], (byte)(lengths[i] - k_RootBits));
            }
        }
        var size = 256;
        for (var i = 0; i < 256; i++) {
            if (suffixBits[i] != 0) {
                size += 1 << suffixBits[i];
            }
        }
        var storage = memory.Rent<int>(size);
        var table = storage.Span;
        table.Clear();
        var offset = 256;
        for (var i = 0; i < 256; i++) {
            if (suffixBits[i] != 0) {
                table[i] = -((offset << 4) | suffixBits[i]);
                offset += 1 << suffixBits[i];
            }
        }
        for (var symbol = 0; symbol < lengths.Length; symbol++) {
            var bits = lengths[symbol];
            if (bits == 0) {
                continue;
            }
            var entry = (symbol << 4) | bits;
            if (bits <= k_RootBits) {
                for (var index = (int)codes[symbol]; index < 256; index += 1 << bits) {
                    table[index] = entry;
                }
            }
            else {
                var root = codes[symbol] & 255;
                var start = -table[root] >> 4;
                var end = 1 << suffixBits[root];
                for (var index = codes[symbol] >> k_RootBits; index < end; index += 1 << (bits - k_RootBits)) {
                    table[start + index] = entry;
                }
            }
        }
        return new Vp8LHuffman(storage);
    }

    internal static void BuildCodes(ReadOnlySpan<byte> lengths, Span<ushort> codes)
    {
        Span<int> counts = stackalloc int[16];
        Span<int> next = stackalloc int[16];
        counts.Clear();
        next.Clear();
        foreach (var length in lengths) {
            if (length != 0) {
                counts[length]++;
            }
        }
        var code = 0;
        for (var bits = 1; bits <= 15; bits++) {
            code = (code + counts[bits - 1]) << 1;
            next[bits] = code;
        }
        for (var i = 0; i < lengths.Length; i++) {
            var length = lengths[i];
            var value = next[length]++;
            var reversed = 0;
            for (var bit = 0; bit < length; bit++) {
                reversed = (reversed << 1) | (value & 1);
                value >>= 1;
            }
            codes[i] = (ushort)reversed;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(ref Vp8LBitReader reader)
    {
        if (_singleSymbol >= 0) {
            return _singleSymbol;
        }
        var table = _table!.Span;
        var entry = table[(int)reader.Peek(k_RootBits)];
        if (entry < 0) {
            var link = -entry;
            var suffix = (int)(reader.Peek(k_RootBits + (link & 15)) >> k_RootBits);
            entry = table[(link >> 4) + suffix];
        }
        reader.Skip(entry & 15);
        return entry >> 4;
    }

    public void Dispose() => _table?.Dispose();
}
