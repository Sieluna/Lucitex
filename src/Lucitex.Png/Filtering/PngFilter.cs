using System.Numerics;
using Lucitex.Core.Execution;
using Lucitex.Png.Format;

namespace Lucitex.Png.Filtering;

internal static class PngFilter
{
    public static void Reconstruct(PngFilterType filterType, Span<byte> current, ReadOnlySpan<byte> previous, int bpp)
    {
        switch (filterType) {
            case PngFilterType.None:
                break;
            case PngFilterType.Sub:
                for (var i = bpp; i < current.Length; i++) {
                    current[i] = unchecked((byte)(current[i] + current[i - bpp]));
                }

                break;
            case PngFilterType.Up:
                ReconstructUp(current, previous);

                break;
            case PngFilterType.Average:
                for (var i = 0; i < current.Length; i++) {
                    var left = i >= bpp ? current[i - bpp] : 0;
                    var up = previous.IsEmpty ? 0 : previous[i];
                    current[i] = unchecked((byte)(current[i] + ((left + up) / 2)));
                }

                break;
            case PngFilterType.Paeth:
                for (var i = 0; i < current.Length; i++) {
                    var left = i >= bpp ? current[i - bpp] : 0;
                    var up = previous.IsEmpty ? 0 : previous[i];
                    var upLeft = i >= bpp && !previous.IsEmpty ? previous[i - bpp] : 0;
                    current[i] = unchecked((byte)(current[i] + Paeth(left, up, upLeft)));
                }

                break;
            default:
                throw new ImageFormatException("png", "BadFilterType", $"Unknown PNG filter type {(byte)filterType}.");
        }
    }

    public static void Apply(PngFilterType filterType, Span<byte> output, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> previousRaw, int bpp)
    {
        switch (filterType) {
            case PngFilterType.None:
                raw.CopyTo(output);
                break;
            case PngFilterType.Sub:
                ApplySub(output, raw, bpp);

                break;
            case PngFilterType.Up:
                ApplyUp(output, raw, previousRaw);

                break;
            case PngFilterType.Average:
                ApplyAverage(output, raw, previousRaw, bpp);

                break;
            case PngFilterType.Paeth:
                for (var i = 0; i < raw.Length; i++) {
                    var left = i >= bpp ? raw[i - bpp] : 0;
                    var up = previousRaw.IsEmpty ? 0 : previousRaw[i];
                    var upLeft = i >= bpp && !previousRaw.IsEmpty ? previousRaw[i - bpp] : 0;
                    output[i] = unchecked((byte)(raw[i] - Paeth(left, up, upLeft)));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(filterType));
        }
    }

    private static void ReconstructUp(Span<byte> current, ReadOnlySpan<byte> previous)
    {
        if (previous.IsEmpty) {
            return;
        }

        var i = 0;
        var lanes = Vector<byte>.Count;
        if (Vector.IsHardwareAccelerated) {
            for (; i <= current.Length - lanes; i += lanes) {
                var value = new Vector<byte>(current.Slice(i, lanes));
                var up = new Vector<byte>(previous.Slice(i, lanes));
                (value + up).CopyTo(current.Slice(i, lanes));
            }
        }

        for (; i < current.Length; i++) {
            current[i] = unchecked((byte)(current[i] + previous[i]));
        }
    }

    private static void ApplySub(Span<byte> output, ReadOnlySpan<byte> raw, int bpp)
    {
        raw[..Math.Min(bpp, raw.Length)].CopyTo(output);
        var i = bpp;
        var lanes = Vector<byte>.Count;
        if (Vector.IsHardwareAccelerated) {
            for (; i <= raw.Length - lanes; i += lanes) {
                var value = new Vector<byte>(raw.Slice(i, lanes));
                var left = new Vector<byte>(raw.Slice(i - bpp, lanes));
                (value - left).CopyTo(output.Slice(i, lanes));
            }
        }

        for (; i < raw.Length; i++) {
            output[i] = unchecked((byte)(raw[i] - raw[i - bpp]));
        }
    }

    private static void ApplyUp(Span<byte> output, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> previous)
    {
        if (previous.IsEmpty) {
            raw.CopyTo(output);
            return;
        }

        var i = 0;
        var lanes = Vector<byte>.Count;
        if (Vector.IsHardwareAccelerated) {
            for (; i <= raw.Length - lanes; i += lanes) {
                var value = new Vector<byte>(raw.Slice(i, lanes));
                var up = new Vector<byte>(previous.Slice(i, lanes));
                (value - up).CopyTo(output.Slice(i, lanes));
            }
        }

        for (; i < raw.Length; i++) {
            output[i] = unchecked((byte)(raw[i] - previous[i]));
        }
    }

    private static void ApplyAverage(Span<byte> output, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> previous, int bpp)
    {
        var i = 0;
        var lanes = Vector<byte>.Count;
        if (Vector.IsHardwareAccelerated && !previous.IsEmpty) {
            for (; i < Math.Min(bpp, raw.Length); i++) {
                output[i] = unchecked((byte)(raw[i] - (previous[i] / 2)));
            }

            for (; i <= raw.Length - lanes; i += lanes) {
                var value = new Vector<byte>(raw.Slice(i, lanes));
                var left = new Vector<byte>(raw.Slice(i - bpp, lanes));
                var up = new Vector<byte>(previous.Slice(i, lanes));
                var average = (left & up) + ((left ^ up) >> 1);
                (value - average).CopyTo(output.Slice(i, lanes));
            }
        }

        for (; i < raw.Length; i++) {
            var left = i >= bpp ? raw[i - bpp] : 0;
            var up = previous.IsEmpty ? 0 : previous[i];
            output[i] = unchecked((byte)(raw[i] - ((left + up) / 2)));
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc) {
            return a;
        }

        return pb <= pc ? b : c;
    }
}
