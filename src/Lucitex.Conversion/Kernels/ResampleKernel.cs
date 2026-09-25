namespace Lucitex.Conversion.Kernels;

internal static partial class ResampleKernel
{
    internal readonly record struct WeightedSample(int Index, float Weight);

    public static (int Width, int Height) Resize(ReadOnlySpan<float> source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, Span<float> destination)
    {
        if (targetWidth <= 0 || targetHeight <= 0) {
            throw new ArgumentOutOfRangeException(targetWidth <= 0 ? nameof(targetWidth) : nameof(targetHeight), "Target dimensions must be positive.");
        }

        if (targetWidth == sourceWidth && targetHeight == sourceHeight) {
            source[..(sourceWidth * sourceHeight)].CopyTo(destination);
            return (targetWidth, targetHeight);
        }

        var horizontalTaps = targetWidth == sourceWidth ? null : BuildAxisTaps(sourceWidth, targetWidth);
        if (targetHeight == sourceHeight) {
            for (var y = 0; y < sourceHeight; y++) {
                ResizeRow(source.Slice(y * sourceWidth, sourceWidth), horizontalTaps!, destination.Slice(y * targetWidth, targetWidth));
            }
            return (targetWidth, targetHeight);
        }

        var verticalTaps = BuildAxisTaps(sourceHeight, targetHeight);
        var cachedRowCount = horizontalTaps is null ? 0 : verticalTaps.Max(taps => taps.Length);
        var horizontal = new float[checked(cachedRowCount * targetWidth)];
        var cachedRows = new int[cachedRowCount];
        Array.Fill(cachedRows, -1);
        for (var ty = 0; ty < targetHeight; ty++) {
            var row = destination.Slice(ty * targetWidth, targetWidth);
            row.Clear();
            foreach (var tap in verticalTaps[ty]) {
                ReadOnlySpan<float> sourceRow;
                if (horizontalTaps is null) {
                    sourceRow = source.Slice(tap.Index * sourceWidth, sourceWidth);
                }
                else {
                    var slot = tap.Index % cachedRowCount;
                    var cached = horizontal.AsSpan(slot * targetWidth, targetWidth);
                    if (cachedRows[slot] != tap.Index) {
                        ResizeRow(source.Slice(tap.Index * sourceWidth, sourceWidth), horizontalTaps, cached);
                        cachedRows[slot] = tap.Index;
                    }
                    sourceRow = cached;
                }
                for (var x = 0; x < targetWidth; x++) {
                    row[x] += tap.Weight * sourceRow[x];
                }
            }
        }

        return (targetWidth, targetHeight);
    }

    private static void ResizeRow(ReadOnlySpan<float> sourceRow, WeightedSample[][] taps, Span<float> destinationRow)
    {
        for (var d = 0; d < destinationRow.Length; d++) {
            var sum = 0f;
            foreach (var tap in taps[d]) {
                sum += tap.Weight * sourceRow[tap.Index];
            }

            destinationRow[d] = sum;
        }
    }

    private static WeightedSample[][] BuildAxisTaps(int sourceSize, int targetSize)
    {
        var scale = (double)sourceSize / targetSize;
        var filterRadius = Math.Max(1.0, scale);
        var taps = new WeightedSample[targetSize][];
        var buffer = new List<WeightedSample>();

        for (var d = 0; d < targetSize; d++) {
            var center = ((d + 0.5) * scale) - 0.5;
            var lo = Math.Max(0, (int)Math.Floor(center - filterRadius));
            var hi = Math.Min(sourceSize - 1, (int)Math.Ceiling(center + filterRadius));

            buffer.Clear();
            var weightSum = 0.0;
            for (var s = lo; s <= hi; s++) {
                var weight = Math.Max(0.0, 1.0 - (Math.Abs(s - center) / filterRadius));
                if (weight <= 0) {
                    continue;
                }

                buffer.Add(new WeightedSample(s, (float)weight));
                weightSum += weight;
            }

            if (buffer.Count == 0) {
                var nearest = Math.Clamp((int)Math.Round(center), 0, sourceSize - 1);
                taps[d] = [new WeightedSample(nearest, 1f)];
                continue;
            }

            var normalized = new WeightedSample[buffer.Count];
            for (var i = 0; i < buffer.Count; i++) {
                normalized[i] = buffer[i] with { Weight = (float)(buffer[i].Weight / weightSum) };
            }

            taps[d] = normalized;
        }

        return taps;
    }
}
