namespace Lucitex.Benchmarks.Validation;

internal sealed record QualityMetrics(double Mse, double? PsnrDb, int MaxError, long DifferentSamples)
{
    public bool Exact => DifferentSamples == 0;

    public static QualityMetrics Compare(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        if (expected.Length != actual.Length || expected.IsEmpty) {
            throw new InvalidDataException("Pixel buffer sizes differ or are empty.");
        }
        double squared = 0;
        var maximum = 0;
        long different = 0;
        for (var i = 0; i < expected.Length; i++) {
            var difference = Math.Abs(expected[i] - actual[i]);
            squared += difference * difference;
            maximum = Math.Max(maximum, difference);
            if (difference != 0) {
                different++;
            }
        }
        var mse = squared / expected.Length;
        return new QualityMetrics(mse, mse == 0 ? null : 10 * Math.Log10(255 * 255 / mse), maximum, different);
    }
}
