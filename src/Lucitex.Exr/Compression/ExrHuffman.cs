using System.Buffers.Binary;

namespace Lucitex.Exr.Compression;

internal static class ExrHuffman
{
    private const int k_EncodeBits = 16;
    private const int k_DecodeBits = 14;
    private const int k_EncodeSize = (1 << k_EncodeBits) + 1;
    private const int k_DecodeSize = 1 << k_DecodeBits;
    private const int k_DecodeMask = k_DecodeSize - 1;

    private const int k_ShortZeroRun = 59;
    private const int k_LongZeroRun = 63;
    private const int k_ShortestLongRun = 2 + k_LongZeroRun - k_ShortZeroRun;
    private const int k_LongestLongRun = 255 + k_ShortestLongRun;

    private const int k_MaxCodeLength = 56;
    private const int k_HeaderBytes = 20;

    public static byte[] Compress(ReadOnlySpan<ushort> raw)
    {
        if (raw.Length == 0) {
            return [];
        }

        var frequencies = new long[k_EncodeSize];
        foreach (var value in raw) {
            frequencies[value]++;
        }

        var codes = BuildEncodeTable(frequencies, out var minSymbol, out var maxSymbol);

        var tableWriter = new BitWriter();
        PackEncodeTable(codes, minSymbol, maxSymbol, tableWriter);
        var table = tableWriter.ToArray();

        var payloadWriter = new BitWriter();
        EncodeSymbols(codes, raw, maxSymbol, payloadWriter);
        var bitCount = payloadWriter.BitCount;
        var payload = payloadWriter.ToArray();

        var result = new byte[k_HeaderBytes + table.Length + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0), (uint)minSymbol);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)maxSymbol);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)table.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), (uint)bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), 0);
        table.CopyTo(result.AsSpan(k_HeaderBytes));
        payload.CopyTo(result.AsSpan(k_HeaderBytes + table.Length));

        return result;
    }

    public static void Uncompress(ReadOnlySpan<byte> compressed, Span<ushort> raw)
    {
        if (compressed.Length == 0) {
            if (!raw.IsEmpty) {
                throw new InvalidDataException("Huffman-compressed EXR data is empty but samples were expected.");
            }

            return;
        }

        if (compressed.Length < k_HeaderBytes) {
            throw new InvalidDataException("Huffman-compressed EXR data is truncated before its header ends.");
        }

        var minSymbol = (int)BinaryPrimitives.ReadUInt32LittleEndian(compressed);
        var maxSymbol = (int)BinaryPrimitives.ReadUInt32LittleEndian(compressed[4..]);
        var bitCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(compressed[12..]);

        if (minSymbol < 0 || minSymbol >= k_EncodeSize || maxSymbol < 0 || maxSymbol >= k_EncodeSize || minSymbol > maxSymbol) {
            throw new InvalidDataException($"Huffman symbol range [{minSymbol},{maxSymbol}] is invalid.");
        }

        var codes = new long[k_EncodeSize];
        var reader = new BitReader(compressed[k_HeaderBytes..]);
        UnpackEncodeTable(reader, minSymbol, maxSymbol, codes);
        CanonicalCodeTable(codes);

        var payloadStart = k_HeaderBytes + reader.BytesConsumed;
        if (bitCount < 0 || bitCount > 8L * (compressed.Length - payloadStart)) {
            throw new InvalidDataException("Huffman payload declares more bits than the chunk contains.");
        }

        var table = BuildDecodeTable(codes, minSymbol, maxSymbol);
        DecodeSymbols(codes, table, compressed[payloadStart..], bitCount, maxSymbol, raw);
    }

    private static long[] BuildEncodeTable(long[] frequencies, out int minSymbol, out int maxSymbol)
    {
        var link = new int[k_EncodeSize];
        var heap = new int[k_EncodeSize];
        var count = 0;

        minSymbol = 0;
        while (frequencies[minSymbol] == 0) {
            minSymbol++;
        }

        var largest = minSymbol;
        for (var i = minSymbol; i < k_EncodeSize; i++) {
            link[i] = i;
            if (frequencies[i] != 0) {
                heap[count++] = i;
                largest = i;
            }
        }

        maxSymbol = largest + 1;
        frequencies[maxSymbol] = 1;
        link[maxSymbol] = maxSymbol;
        heap[count++] = maxSymbol;

        for (var i = (count / 2) - 1; i >= 0; i--) {
            SiftDown(heap, count, i, frequencies);
        }

        var lengths = new long[k_EncodeSize];

        while (count > 1) {
            var first = heap[0];
            heap[0] = heap[--count];
            SiftDown(heap, count, 0, frequencies);

            var second = heap[0];
            frequencies[second] += frequencies[first];
            SiftDown(heap, count, 0, frequencies);

            for (var node = first; ; node = link[node]) {
                lengths[node]++;
                if (link[node] == node) {
                    break;
                }
            }

            for (var node = second; ; node = link[node]) {
                lengths[node]++;
                if (link[node] == node) {
                    link[node] = first;
                    break;
                }
            }
        }

        CanonicalCodeTable(lengths);
        return lengths;
    }

    private static void SiftDown(int[] heap, int count, int index, long[] frequencies)
    {
        while (true) {
            var smallest = index;
            var left = (2 * index) + 1;
            var right = left + 1;

            if (left < count && IsLess(heap[left], heap[smallest], frequencies)) {
                smallest = left;
            }

            if (right < count && IsLess(heap[right], heap[smallest], frequencies)) {
                smallest = right;
            }

            if (smallest == index) {
                return;
            }

            (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
            index = smallest;
        }
    }

    private static bool IsLess(int left, int right, long[] frequencies) =>
        frequencies[left] != frequencies[right] ? frequencies[left] < frequencies[right] : left < right;

    private static void CanonicalCodeTable(long[] codes)
    {
        var counts = new long[59];

        for (var i = 0; i < k_EncodeSize; i++) {
            var length = codes[i];
            if (length < 0 || length > 58) {
                throw new InvalidDataException($"Huffman code length {length} is outside the representable range.");
            }

            counts[length]++;
        }

        var code = 0L;
        for (var length = 58; length > 0; length--) {
            var next = (code + counts[length]) >> 1;
            counts[length] = code;
            code = next;
        }

        for (var i = 0; i < k_EncodeSize; i++) {
            var length = (int)codes[i];
            if (length > 0) {
                codes[i] = length | (counts[length]++ << 6);
            }
        }
    }

    private static long CodeOf(long entry) => entry >> 6;

    private static int LengthOf(long entry) => (int)(entry & 63);

    private static void PackEncodeTable(long[] codes, int minSymbol, int maxSymbol, BitWriter writer)
    {
        for (var symbol = minSymbol; symbol <= maxSymbol; symbol++) {
            var length = LengthOf(codes[symbol]);

            if (length == 0) {
                var run = 1;
                while (symbol < maxSymbol && run < k_LongestLongRun && LengthOf(codes[symbol + 1]) == 0) {
                    symbol++;
                    run++;
                }

                if (run >= 2) {
                    if (run >= k_ShortestLongRun) {
                        writer.Write(6, k_LongZeroRun);
                        writer.Write(8, run - k_ShortestLongRun);
                    }
                    else {
                        writer.Write(6, k_ShortZeroRun + run - 2);
                    }

                    continue;
                }
            }

            writer.Write(6, length);
        }
    }

    private static void UnpackEncodeTable(BitReader reader, int minSymbol, int maxSymbol, long[] codes)
    {
        for (var symbol = minSymbol; symbol <= maxSymbol; symbol++) {
            var length = (int)reader.Read(6);
            codes[symbol] = length;

            if (length == k_LongZeroRun) {
                var run = (int)reader.Read(8) + k_ShortestLongRun;
                if (symbol + run > maxSymbol + 1) {
                    throw new InvalidDataException("Huffman code table declares a zero run past its symbol range.");
                }

                while (run-- > 0) {
                    codes[symbol++] = 0;
                }

                symbol--;
            }
            else if (length >= k_ShortZeroRun) {
                var run = length - k_ShortZeroRun + 2;
                if (symbol + run > maxSymbol + 1) {
                    throw new InvalidDataException("Huffman code table declares a zero run past its symbol range.");
                }

                while (run-- > 0) {
                    codes[symbol++] = 0;
                }

                symbol--;
            }
        }
    }

    private static void EncodeSymbols(long[] codes, ReadOnlySpan<ushort> input, int runCode, BitWriter writer)
    {
        var symbol = (int)input[0];
        var run = 0;

        for (var i = 1; i < input.Length; i++) {
            if (symbol == input[i] && run < 255) {
                run++;
            }
            else {
                SendCode(codes, symbol, run, runCode, writer);
                run = 0;
            }

            symbol = input[i];
        }

        SendCode(codes, symbol, run, runCode, writer);
    }

    private static void SendCode(long[] codes, int symbol, int run, int runCode, BitWriter writer)
    {
        var code = codes[symbol];
        var repeat = codes[runCode];

        if (LengthOf(code) + LengthOf(repeat) + 8 < LengthOf(code) * (run + 1)) {
            WriteCode(writer, code);
            WriteCode(writer, repeat);
            writer.Write(8, run);
            return;
        }

        for (var i = 0; i <= run; i++) {
            WriteCode(writer, code);
        }
    }

    private static void WriteCode(BitWriter writer, long code)
    {
        var length = LengthOf(code);
        if (length == 0 || length > k_MaxCodeLength) {
            throw new InvalidDataException($"Huffman code length {length} is not supported.");
        }

        writer.Write(length, CodeOf(code));
    }

    private static (int[] Lengths, int[] Symbols, List<int>?[] Overflow) BuildDecodeTable(long[] codes, int minSymbol, int maxSymbol)
    {
        var lengths = new int[k_DecodeSize];
        var symbols = new int[k_DecodeSize];
        var overflow = new List<int>?[k_DecodeSize];

        for (var symbol = minSymbol; symbol <= maxSymbol; symbol++) {
            var code = CodeOf(codes[symbol]);
            var length = LengthOf(codes[symbol]);

            if (length == 0) {
                continue;
            }

            if (length > k_MaxCodeLength) {
                throw new InvalidDataException($"Huffman code length {length} is not supported.");
            }

            if (code >> length != 0) {
                throw new InvalidDataException("Huffman code table contains a code wider than its declared length.");
            }

            if (length > k_DecodeBits) {
                var index = (int)(code >> (length - k_DecodeBits));
                if (lengths[index] != 0) {
                    throw new InvalidDataException("Huffman code table mixes short and long codes in one slot.");
                }

                (overflow[index] ??= []).Add(symbol);
            }
            else {
                var index = (int)(code << (k_DecodeBits - length));
                for (var i = 1L << (k_DecodeBits - length); i > 0; i--, index++) {
                    if (lengths[index] != 0 || overflow[index] is not null) {
                        throw new InvalidDataException("Huffman code table assigns one slot to several codes.");
                    }

                    lengths[index] = length;
                    symbols[index] = symbol;
                }
            }
        }

        return (lengths, symbols, overflow);
    }

    private static void DecodeSymbols(
        long[] codes,
        (int[] Lengths, int[] Symbols, List<int>?[] Overflow) table,
        ReadOnlySpan<byte> payload,
        int bitCount,
        int runCode,
        Span<ushort> output)
    {
        var decoder = new SymbolDecoder(payload, bitCount, runCode, output);
        decoder.Run(codes, table);
    }

    private ref struct SymbolDecoder
    {
        private readonly ReadOnlySpan<byte> _payload;
        private readonly Span<ushort> _output;
        private readonly int _payloadEnd;
        private readonly int _bitCount;
        private readonly int _runCode;
        private ulong _accumulator;
        private int _available;
        private int _readPosition;
        private int _writePosition;

        public SymbolDecoder(ReadOnlySpan<byte> payload, int bitCount, int runCode, Span<ushort> output)
        {
            _payload = payload;
            _output = output;
            _bitCount = bitCount;
            _payloadEnd = (bitCount + 7) / 8;
            _runCode = runCode;
        }

        public void Run(long[] codes, (int[] Lengths, int[] Symbols, List<int>?[] Overflow) table)
        {
            while (_readPosition < _payloadEnd) {
                Fill();

                while (_available >= k_DecodeBits) {
                    var index = (int)((_accumulator >> (_available - k_DecodeBits)) & k_DecodeMask);

                    if (table.Lengths[index] != 0) {
                        _available -= table.Lengths[index];
                        Emit(table.Symbols[index]);
                        continue;
                    }

                    var candidates = table.Overflow[index]
                        ?? throw new InvalidDataException("Huffman payload contains a code that is not in the table.");

                    if (!TryEmitLongCode(codes, candidates)) {
                        throw new InvalidDataException("Huffman payload contains a code that is not in the table.");
                    }
                }
            }

            var padding = (8 - _bitCount) & 7;
            _accumulator >>= padding;
            _available -= padding;

            while (_available > 0) {
                var index = (int)((_accumulator << (k_DecodeBits - _available)) & k_DecodeMask);
                var length = table.Lengths[index];

                if (length == 0 || length > _available) {
                    throw new InvalidDataException("Huffman payload ends with an incomplete code.");
                }

                _available -= length;
                Emit(table.Symbols[index]);
            }

            if (_writePosition != _output.Length) {
                throw new InvalidDataException($"Huffman payload expands to {_writePosition} samples; expected {_output.Length}.");
            }
        }

        private bool TryEmitLongCode(long[] codes, List<int> candidates)
        {
            foreach (var symbol in candidates) {
                var length = LengthOf(codes[symbol]);

                while (_available < length && _readPosition < _payloadEnd) {
                    Fill();
                }

                if (_available < length) {
                    continue;
                }

                if (CodeOf(codes[symbol]) != (long)((_accumulator >> (_available - length)) & ((1UL << length) - 1))) {
                    continue;
                }

                _available -= length;
                Emit(symbol);
                return true;
            }

            return false;
        }

        private void Fill()
        {
            _accumulator = (_accumulator << 8) | _payload[_readPosition++];
            _available += 8;
        }

        private void Emit(int symbol)
        {
            if (symbol != _runCode) {
                if (_writePosition >= _output.Length) {
                    throw new InvalidDataException("Huffman payload expands beyond the expected sample count.");
                }

                _output[_writePosition++] = (ushort)symbol;
                return;
            }

            if (_available < 8) {
                if (_readPosition >= _payloadEnd) {
                    throw new InvalidDataException("Huffman payload ends inside a run-length code.");
                }

                Fill();
            }

            _available -= 8;
            var repeat = (int)((_accumulator >> _available) & 0xFF);

            if (_writePosition == 0 || _writePosition + repeat > _output.Length) {
                throw new InvalidDataException("Huffman run-length code expands beyond the expected sample count.");
            }

            var previous = _output[_writePosition - 1];
            for (var i = 0; i < repeat; i++) {
                _output[_writePosition++] = previous;
            }
        }
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private ulong _accumulator;
        private int _available;

        public long BitCount { get; private set; }

        public void Write(int bits, long value)
        {
            if (bits > 32) {
                Write(bits - 32, (long)((ulong)value >> 32));
                Write(32, value & 0xFFFFFFFFL);
                return;
            }

            _accumulator = (_accumulator << bits) | ((ulong)value & ((1UL << bits) - 1));
            _available += bits;
            BitCount += bits;

            while (_available >= 8) {
                _available -= 8;
                _bytes.Add((byte)(_accumulator >> _available));
            }
        }

        public byte[] ToArray()
        {
            if (_available > 0) {
                _bytes.Add((byte)(_accumulator << (8 - _available)));
                _available = 0;
            }

            return _bytes.ToArray();
        }
    }

    private sealed class BitReader
    {
        private readonly byte[] _source;
        private ulong _accumulator;
        private int _available;
        private int _position;

        public BitReader(ReadOnlySpan<byte> source) => _source = source.ToArray();

        public int BytesConsumed => _position;

        public long Read(int bits)
        {
            while (_available < bits) {
                if (_position >= _source.Length) {
                    throw new InvalidDataException("Huffman code table ends before all lengths were read.");
                }

                _accumulator = (_accumulator << 8) | _source[_position++];
                _available += 8;
            }

            _available -= bits;
            return (long)((_accumulator >> _available) & ((1UL << bits) - 1));
        }
    }
}
