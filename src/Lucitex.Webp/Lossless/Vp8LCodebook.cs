namespace Lucitex.Webp.Lossless;

internal sealed class Vp8LCodebook
{
    private readonly byte[] _lengths;
    private readonly ushort[] _codes;
    private readonly int _symbolCount;
    private readonly int _firstSymbol;
    private readonly int _lastSymbol;

    public Vp8LCodebook(ReadOnlySpan<int> frequencies, int maxBits = 15)
    {
        _lengths = BuildLengths(frequencies, maxBits);
        _codes = new ushort[_lengths.Length];
        Vp8LHuffman.BuildCodes(_lengths, _codes);
        _firstSymbol = -1;
        for (var i = 0; i < _lengths.Length; i++) {
            if (_lengths[i] != 0) {
                _symbolCount++;
                if (_firstSymbol < 0) {
                    _firstSymbol = i;
                }
                _lastSymbol = i;
            }
        }
    }

    private static byte[] BuildLengths(ReadOnlySpan<int> frequencies, int maxBits)
    {
        var lengths = new byte[frequencies.Length];
        Span<int> weights = stackalloc int[frequencies.Length];
        frequencies.CopyTo(weights);
        Span<int> parents = stackalloc int[(frequencies.Length * 2) - 1];
        var queue = new PriorityQueue<int, (long Weight, int Symbol)>(frequencies.Length);
        while (true) {
            queue.Clear();
            parents.Fill(-1);
            for (var i = 0; i < weights.Length; i++) {
                if (weights[i] != 0) {
                    queue.Enqueue(i, (weights[i], i));
                }
            }
            if (queue.Count == 0) {
                lengths[0] = 1;
                return lengths;
            }
            if (queue.Count == 1) {
                lengths[queue.Dequeue()] = 1;
                return lengths;
            }
            var node = weights.Length;
            while (queue.Count > 1) {
                queue.TryDequeue(out var left, out var leftWeight);
                queue.TryDequeue(out var right, out var rightWeight);
                parents[left] = node;
                parents[right] = node;
                queue.Enqueue(node, (leftWeight.Weight + rightWeight.Weight, node));
                node++;
            }
            var maximum = 0;
            for (var i = 0; i < weights.Length; i++) {
                var bits = 0;
                for (var parent = parents[i]; parent >= 0; parent = parents[parent]) {
                    bits++;
                }
                lengths[i] = (byte)bits;
                maximum = Math.Max(maximum, bits);
            }
            if (maximum <= maxBits) {
                return lengths;
            }
            for (var i = 0; i < weights.Length; i++) {
                weights[i] = (weights[i] + 1) >> 1;
            }
        }
    }

    public long Measure(ReadOnlySpan<int> frequencies)
    {
        long bits = 0;
        if (_symbolCount > 1) {
            for (var i = 0; i < frequencies.Length; i++) {
                bits += (long)frequencies[i] * _lengths[i];
            }
        }
        return bits;
    }

    public void WriteSymbol(Vp8LBitWriter writer, int symbol)
    {
        if (_symbolCount > 1) {
            writer.Write(_codes[symbol], _lengths[symbol]);
        }
    }

    public void WriteHeader(Vp8LBitWriter writer)
    {
        if (_symbolCount <= 2 && _lastSymbol < 256) {
            writer.Write(1, 1);
            writer.Write((uint)(_symbolCount - 1), 1);
            writer.Write(_firstSymbol < 2 ? 0u : 1u, 1);
            writer.Write((uint)_firstSymbol, _firstSymbol < 2 ? 1 : 8);
            if (_symbolCount == 2) {
                writer.Write((uint)_lastSymbol, 8);
            }
            return;
        }
        Span<int> frequencies = stackalloc int[19];
        frequencies.Clear();
        for (var i = 0; i < _lengths.Length;) {
            var length = _lengths[i];
            var run = 1;
            while (i + run < _lengths.Length && _lengths[i + run] == length) {
                run++;
            }
            i += run;
            CountLengthRun(frequencies, length, run);
        }
        var lengthBook = new Vp8LCodebook(frequencies, 7);
        writer.Write(0, 1);
        var count = 19;
        while (count > 4 && lengthBook._lengths[Vp8LHuffman.CodeLengthOrder[count - 1]] == 0) {
            count--;
        }
        writer.Write((uint)(count - 4), 4);
        for (var i = 0; i < count; i++) {
            writer.Write(lengthBook._lengths[Vp8LHuffman.CodeLengthOrder[i]], 3);
        }
        writer.Write(0, 1);
        for (var i = 0; i < _lengths.Length;) {
            var length = _lengths[i];
            var run = 1;
            while (i + run < _lengths.Length && _lengths[i + run] == length) {
                run++;
            }
            i += run;
            WriteLengthRun(writer, lengthBook, length, run);
        }
    }

    private static void CountLengthRun(Span<int> frequencies, int length, int run)
    {
        if (length != 0) {
            frequencies[length]++;
            run--;
            while (run >= 3) {
                frequencies[16]++;
                run -= Math.Min(run, 6);
            }
        }
        else {
            while (run >= 11) {
                frequencies[18]++;
                run -= Math.Min(run, 138);
            }
            if (run >= 3) {
                frequencies[17]++;
                run -= Math.Min(run, 10);
            }
        }
        frequencies[length] += run;
    }

    private static void WriteLengthRun(Vp8LBitWriter writer, Vp8LCodebook book, int length, int run)
    {
        if (length != 0) {
            book.WriteSymbol(writer, length);
            run--;
            while (run >= 3) {
                var repeat = Math.Min(run, 6);
                book.WriteSymbol(writer, 16);
                writer.Write((uint)(repeat - 3), 2);
                run -= repeat;
            }
        }
        else {
            while (run >= 11) {
                var repeat = Math.Min(run, 138);
                book.WriteSymbol(writer, 18);
                writer.Write((uint)(repeat - 11), 7);
                run -= repeat;
            }
            if (run >= 3) {
                var repeat = Math.Min(run, 10);
                book.WriteSymbol(writer, 17);
                writer.Write((uint)(repeat - 3), 3);
                run -= repeat;
            }
        }
        while (run-- > 0) {
            book.WriteSymbol(writer, length);
        }
    }
}
