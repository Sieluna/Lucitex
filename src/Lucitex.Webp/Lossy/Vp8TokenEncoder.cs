namespace Lucitex.Webp.Lossy;

internal static class Vp8TokenEncoder
{
    public static void Write(Vp8BoolEncoder writer, ReadOnlySpan<short> coefficients, int block, int type, Span<byte> left, Span<byte> above)
    {
        var first = type == 0 ? 1 : 0;
        var last = 15;
        while (last >= first && coefficients[Vp8Tables.Zigzag[last]] == 0) {
            last--;
        }
        var leftSlot = Vp8Tables.LeftContextIndex[block];
        var aboveSlot = Vp8Tables.AboveContextIndex[block];
        var context = left[leftSlot] + above[aboveSlot];
        var previousZero = false;
        Span<byte> probabilities = stackalloc byte[11];
        for (var index = first; index < 16; index++) {
            for (var p = 0; p < 11; p++) {
                probabilities[p] = Vp8Tables.DefaultCoeffProbs[type, Vp8Tables.CoeffBands[index], context, p];
            }
            if (!previousZero) {
                writer.Put(index <= last ? 1 : 0, probabilities[0]);
                if (index > last) {
                    break;
                }
            }
            var coefficient = coefficients[Vp8Tables.Zigzag[index]];
            var value = Math.Abs((int)coefficient);
            writer.Put(value == 0 ? 0 : 1, probabilities[1]);
            if (value > 0) {
                writer.Put(value == 1 ? 0 : 1, probabilities[2]);
                if (value > 1) {
                    writer.Put(value <= 4 ? 0 : 1, probabilities[3]);
                    if (value <= 4) {
                        writer.Put(value == 2 ? 0 : 1, probabilities[4]);
                        if (value > 2) {
                            writer.Put(value == 4 ? 1 : 0, probabilities[5]);
                        }
                    }
                    else {
                        var category = 0;
                        while (category < 5 && value >= Vp8Tables.CategoryBase[category + 1]) {
                            category++;
                        }
                        writer.Put(category < 2 ? 0 : 1, probabilities[6]);
                        if (category < 2) {
                            writer.Put(category, probabilities[7]);
                        }
                        else {
                            writer.Put(category < 4 ? 0 : 1, probabilities[8]);
                            writer.Put(category & 1, probabilities[category < 4 ? 9 : 10]);
                        }
                        var extra = value - Vp8Tables.CategoryBase[category];
                        var extraProbabilities = Vp8Tables.CategoryProbs[category];
                        for (var bit = 0; bit < extraProbabilities.Length; bit++) {
                            writer.Put((extra >> (extraProbabilities.Length - bit - 1)) & 1, extraProbabilities[bit]);
                        }
                    }
                }
                writer.Put(coefficient < 0 ? 1 : 0);
            }
            context = value == 0 ? 0 : value == 1 ? 1 : 2;
            previousZero = value == 0;
        }
        left[leftSlot] = above[aboveSlot] = (byte)(last >= first ? 1 : 0);
    }
}
