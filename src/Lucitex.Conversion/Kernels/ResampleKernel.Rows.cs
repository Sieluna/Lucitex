using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Lucitex.Conversion.Kernels;

internal static partial class ResampleKernel
{
    internal delegate void RowReader(int row, Span<float> destination);
    internal delegate void RowWriter(int row, ReadOnlySpan<float> source);

    internal sealed class RowPlan
    {
        internal readonly int SourceWidth;
        internal readonly int TargetWidth;
        internal readonly int TargetHeight;
        internal readonly int Components;
        internal readonly WeightedSample[][]? Horizontal;
        internal readonly WeightedSample[][]? Vertical;
        internal readonly int CachedRows;

        public RowPlan(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, int components = 1)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);
            if (components is not (1 or 4)) throw new ArgumentOutOfRangeException(nameof(components));
            SourceWidth = sourceWidth;
            TargetWidth = targetWidth;
            TargetHeight = targetHeight;
            Components = components;
            Horizontal = sourceWidth == targetWidth ? null : BuildAxisTaps(sourceWidth, targetWidth);
            Vertical = sourceHeight == targetHeight ? null : BuildAxisTaps(sourceHeight, targetHeight);
            CachedRows = Vertical is null ? 1 : Vertical.Max(taps => taps.Length);
        }
    }

    // Each row batch owns its scratch buffers and output rows; the filter plan is immutable/shared.
    public static void ResizeRows(RowPlan plan, int firstRow, int endRow, RowReader read, RowWriter write,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstRow);
        ArgumentOutOfRangeException.ThrowIfLessThan(endRow, firstRow);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(endRow, plan.TargetHeight);
        cancellationToken.ThrowIfCancellationRequested();
        if (firstRow == endRow) return;
        var targetSamples = checked(plan.TargetWidth * plan.Components);
        var sourceRow = plan.Horizontal is null ? [] : new float[checked(plan.SourceWidth * plan.Components)];
        var horizontal = new float[checked(plan.CachedRows * targetSamples)];
        var tags = new int[plan.CachedRows];
        Array.Fill(tags, -1);
        var output = new float[targetSamples];

        void ReadHorizontal(int row, Span<float> destination)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.Horizontal is null) read(row, destination);
            else {
                read(row, sourceRow);
                if (plan.Components == 4) ResizeRgbaRow(sourceRow, plan.Horizontal, destination);
                else ResizeRow(sourceRow, plan.Horizontal, destination);
            }
        }

        for (var y = firstRow; y < endRow; y++) {
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.Vertical is null) {
                ReadHorizontal(y, output);
            }
            else {
                output.AsSpan().Clear();
                foreach (var tap in plan.Vertical[y]) {
                    var slot = tap.Index % plan.CachedRows;
                    var cached = horizontal.AsSpan(slot * targetSamples, targetSamples);
                    if (tags[slot] != tap.Index) {
                        ReadHorizontal(tap.Index, cached);
                        tags[slot] = tap.Index;
                    }
                    if (plan.Components == 4) {
                        var weight = Vector128.Create(tap.Weight);
                        for (var x = 0; x < targetSamples; x += 4) {
                            var values = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(cached), (nuint)x);
                            var sum = Vector128.LoadUnsafe(ref MemoryMarshal.GetArrayDataReference(output), (nuint)x);
                            Vector128.Add(sum, Vector128.Multiply(weight, values)).CopyTo(output.AsSpan(x, 4));
                        }
                    }
                    else {
                        for (var x = 0; x < targetSamples; x++) output[x] += tap.Weight * cached[x];
                    }
                }
            }
            write(y, output);
        }
    }

    private static void ResizeRgbaRow(ReadOnlySpan<float> source, WeightedSample[][] taps, Span<float> destination)
    {
        for (var x = 0; x < taps.Length; x++) {
            var sum = Vector128<float>.Zero;
            foreach (var tap in taps[x]) {
                var values = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(source), (nuint)(tap.Index * 4));
                sum = Vector128.Add(sum, Vector128.Multiply(Vector128.Create(tap.Weight), values));
            }
            sum.CopyTo(destination.Slice(x * 4, 4));
        }
    }
}
