namespace Lucitex.Fuzz;

internal static class ByteMutator
{
    public static byte[] Mutate(byte[] source, Random random)
    {
        var result = source.ToList();
        var operationCount = random.Next(1, 9);
        for (var operation = 0; operation < operationCount; operation++) {
            switch (random.Next(5)) {
                case 0 when result.Count > 0:
                    result[random.Next(result.Count)] ^= (byte)(1 << random.Next(8));
                    break;
                case 1 when result.Count > 0:
                    result[random.Next(result.Count)] = (byte)random.Next(256);
                    break;
                case 2 when result.Count > 1:
                    var removeStart = random.Next(result.Count);
                    var removeCount = random.Next(1, Math.Min(32, result.Count - removeStart) + 1);
                    result.RemoveRange(removeStart, removeCount);
                    break;
                case 3 when result.Count < source.Length + 256:
                    result.Insert(random.Next(result.Count + 1), (byte)random.Next(256));
                    break;
                case 4 when result.Count > 0:
                    var truncateAt = random.Next(result.Count);
                    result.RemoveRange(truncateAt, result.Count - truncateAt);
                    break;
            }
        }

        return result.ToArray();
    }
}
