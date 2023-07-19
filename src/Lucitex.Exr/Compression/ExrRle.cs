namespace Lucitex.Exr.Compression;

internal static class ExrRle
{
    private const int MinRunLength = 3;
    private const int MaxRunLength = 127;

    public static byte[] Compress(ReadOnlySpan<byte> input)
    {
        if (input.Length == 0)
        {
            return [];
        }

        var output = new byte[Math.Max(64, input.Length * 2)];
        var outPos = 0;
        var runStart = 0;
        var runEnd = 1;
        var inLength = input.Length;

        while (runStart < inLength)
        {
            while (runEnd < inLength &&
                   input[runStart] == input[runEnd] &&
                   runEnd - runStart - 1 < MaxRunLength)
            {
                runEnd++;
            }

            if (runEnd - runStart >= MinRunLength)
            {
                output[outPos++] = unchecked((byte)(sbyte)((runEnd - runStart) - 1));
                output[outPos++] = input[runStart];
                runStart = runEnd;
            }
            else
            {
                while (runEnd < inLength &&
                       ((runEnd + 1 >= inLength || input[runEnd] != input[runEnd + 1]) ||
                        (runEnd + 2 >= inLength || input[runEnd + 1] != input[runEnd + 2])) &&
                       runEnd - runStart < MaxRunLength)
                {
                    runEnd++;
                }

                output[outPos++] = unchecked((byte)(sbyte)(runStart - runEnd));
                while (runStart < runEnd)
                {
                    output[outPos++] = input[runStart++];
                }
            }

            runEnd++;
        }

        return output[..outPos];
    }

    public static int Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var inPos = 0;
        var outPos = 0;

        while (inPos < input.Length)
        {
            var control = unchecked((sbyte)input[inPos]);
            inPos++;

            if (control < 0)
            {
                var count = -control;
                input.Slice(inPos, count).CopyTo(output.Slice(outPos, count));
                inPos += count;
                outPos += count;
            }
            else
            {
                var count = control + 1;
                var value = input[inPos];
                inPos++;
                output.Slice(outPos, count).Fill(value);
                outPos += count;
            }
        }

        return outPos;
    }
}
