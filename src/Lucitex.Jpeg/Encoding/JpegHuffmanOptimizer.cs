using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Encoding;

internal static class JpegHuffmanOptimizer
{
    public static JpegHuffmanSpec Build(ReadOnlySpan<long> frequencies, int id, bool isAc)
    {
        Span<long> weights = stackalloc long[513];
        Span<int> parents = stackalloc int[513];
        Span<int> heap = stackalloc int[257];
        Span<int> symbols = stackalloc int[257];
        Span<int> counts = stackalloc int[257];
        parents.Fill(-1);
        counts.Clear();
        var leaves = 0;
        var heapCount = 0;
        for (var symbol = 0; symbol <= 256; symbol++) {
            var weight = symbol == 256 ? 1 : frequencies[symbol];
            if (weight == 0) {
                continue;
            }
            symbols[leaves] = symbol;
            weights[leaves] = weight;
            Push(heap, ref heapCount, weights, leaves++);
        }
        var nodeCount = leaves;
        while (heapCount > 1) {
            var left = Pop(heap, ref heapCount, weights);
            var right = Pop(heap, ref heapCount, weights);
            weights[nodeCount] = weights[left] + weights[right];
            parents[left] = parents[right] = nodeCount;
            Push(heap, ref heapCount, weights, nodeCount++);
        }
        for (var leaf = 0; leaf < leaves; leaf++) {
            var depth = 0;
            for (var node = leaf; parents[node] >= 0; node = parents[node]) {
                depth++;
            }
            counts[depth]++;
        }
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
        while (longest > 0 && counts[longest] == 0) {
            longest--;
        }
        counts[longest]--;
        var symbolCount = leaves - 1;
        for (var i = 1; i < symbolCount; i++) {
            var symbol = symbols[i];
            var position = i;
            while (position > 0 && frequencies[symbols[position - 1]] < frequencies[symbol]) {
                symbols[position] = symbols[position - 1];
                position--;
            }
            symbols[position] = symbol;
        }
        var bits = new byte[16];
        var values = new byte[symbolCount];
        for (var i = 1; i <= 16; i++) {
            bits[i - 1] = checked((byte)counts[i]);
        }
        for (var i = 0; i < symbolCount; i++) {
            values[i] = (byte)symbols[i];
        }
        return new JpegHuffmanSpec { Id = id, IsAc = isAc, Bits = bits, Values = values };
    }

    private static bool Precedes(int left, int right, ReadOnlySpan<long> weights) =>
        weights[left] < weights[right] || (weights[left] == weights[right] && left < right);

    private static void Push(Span<int> heap, ref int count, ReadOnlySpan<long> weights, int node)
    {
        var position = count++;
        while (position > 0) {
            var parent = (position - 1) / 2;
            if (!Precedes(node, heap[parent], weights)) {
                break;
            }
            heap[position] = heap[parent];
            position = parent;
        }
        heap[position] = node;
    }

    private static int Pop(Span<int> heap, ref int count, ReadOnlySpan<long> weights)
    {
        var result = heap[0];
        var node = heap[--count];
        var position = 0;
        while (position * 2 + 1 < count) {
            var child = position * 2 + 1;
            if (child + 1 < count && Precedes(heap[child + 1], heap[child], weights)) {
                child++;
            }
            if (!Precedes(heap[child], node, weights)) {
                break;
            }
            heap[position] = heap[child];
            position = child;
        }
        heap[position] = node;
        return result;
    }
}
