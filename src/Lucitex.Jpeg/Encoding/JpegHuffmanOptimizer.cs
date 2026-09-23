using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Encoding;

internal static class JpegHuffmanOptimizer
{
    public static void Count(ReadOnlySpan<short> coefficients, ref int predictor, long[] dc, long[] ac)
    {
        var difference = coefficients[0] - predictor;
        predictor = coefficients[0];
        dc[JpegMagnitude.GetSize(difference)]++;
        foreach (var (symbol, _) in new JpegAcSymbols(coefficients)) {
            ac[symbol]++;
        }
    }

    public static JpegHuffmanSpec Build(long[] frequencies, int id, bool isAc)
    {
        var nodes = new List<(int Left, int Right, int Symbol)>();
        var queue = new PriorityQueue<int, (long Weight, int Order)>();
        var symbols = new List<int>();
        for (var symbol = 0; symbol <= 256; symbol++) {
            var weight = symbol == 256 ? 1 : frequencies[symbol];
            if (weight == 0) {
                continue;
            }
            symbols.Add(symbol);
            var index = nodes.Count;
            nodes.Add((-1, -1, symbol));
            queue.Enqueue(index, (weight, index));
        }
        while (queue.Count > 1) {
            queue.TryDequeue(out var left, out var leftWeight);
            queue.TryDequeue(out var right, out var rightWeight);
            var index = nodes.Count;
            nodes.Add((left, right, -1));
            queue.Enqueue(index, (leftWeight.Weight + rightWeight.Weight, index));
        }
        var counts = new int[257];
        CountDepth(queue.Dequeue(), 0);
        for (var length = counts.Length - 1; length > 16; length--) {
            while (counts[length] > 0) {
                var shorter = length - 2;
                while (counts[shorter] == 0) {
                    shorter--;
                }
                counts[length] -= 2;
                counts[length - 1]++;
                counts[shorter + 1] += 2;
                counts[shorter]--;
            }
        }
        var longest = 16;
        while (counts[longest] == 0) {
            longest--;
        }
        counts[longest]--;
        symbols.Remove(256);
        symbols.Sort((a, b) => {
            var comparison = frequencies[b].CompareTo(frequencies[a]);
            return comparison == 0 ? a.CompareTo(b) : comparison;
        });
        var bits = new byte[16];
        for (var i = 1; i <= 16; i++) {
            bits[i - 1] = checked((byte)counts[i]);
        }
        return new JpegHuffmanSpec { Id = id, IsAc = isAc, Bits = bits, Values = symbols.Select(s => (byte)s).ToArray() };

        void CountDepth(int index, int depth)
        {
            var node = nodes[index];
            if (node.Symbol >= 0) {
                counts[depth]++;
            }
            else {
                CountDepth(node.Left, depth + 1);
                CountDepth(node.Right, depth + 1);
            }
        }
    }
}
