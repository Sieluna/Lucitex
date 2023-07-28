using Lucitex.Core.Execution;
using Lucitex.Png.Format;

namespace Lucitex.Png.Filtering;

internal static class PngFilter
{
    public static void Reconstruct(PngFilterType filterType, Span<byte> current, ReadOnlySpan<byte> previous, int bpp)
    {
        switch (filterType)
        {
            case PngFilterType.None:
                break;
            case PngFilterType.Sub:
                for (var i = bpp; i < current.Length; i++)
                {
                    current[i] = unchecked((byte)(current[i] + current[i - bpp]));
                }

                break;
            case PngFilterType.Up:
                for (var i = 0; i < current.Length; i++)
                {
                    var up = previous.IsEmpty ? 0 : previous[i];
                    current[i] = unchecked((byte)(current[i] + up));
                }

                break;
            case PngFilterType.Average:
                for (var i = 0; i < current.Length; i++)
                {
                    int left = i >= bpp ? current[i - bpp] : 0;
                    int up = previous.IsEmpty ? 0 : previous[i];
                    current[i] = unchecked((byte)(current[i] + ((left + up) / 2)));
                }

                break;
            case PngFilterType.Paeth:
                for (var i = 0; i < current.Length; i++)
                {
                    int left = i >= bpp ? current[i - bpp] : 0;
                    int up = previous.IsEmpty ? 0 : previous[i];
                    int upLeft = i >= bpp && !previous.IsEmpty ? previous[i - bpp] : 0;
                    current[i] = unchecked((byte)(current[i] + Paeth(left, up, upLeft)));
                }

                break;
            default:
                throw new ImageFormatException("png", "BadFilterType", $"Unknown PNG filter type {(byte)filterType}.");
        }
    }

    public static void Apply(PngFilterType filterType, Span<byte> output, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> previousRaw, int bpp)
    {
        switch (filterType)
        {
            case PngFilterType.None:
                raw.CopyTo(output);
                break;
            case PngFilterType.Sub:
                for (var i = 0; i < raw.Length; i++)
                {
                    int left = i >= bpp ? raw[i - bpp] : 0;
                    output[i] = unchecked((byte)(raw[i] - left));
                }

                break;
            case PngFilterType.Up:
                for (var i = 0; i < raw.Length; i++)
                {
                    var up = previousRaw.IsEmpty ? 0 : previousRaw[i];
                    output[i] = unchecked((byte)(raw[i] - up));
                }

                break;
            case PngFilterType.Average:
                for (var i = 0; i < raw.Length; i++)
                {
                    int left = i >= bpp ? raw[i - bpp] : 0;
                    int up = previousRaw.IsEmpty ? 0 : previousRaw[i];
                    output[i] = unchecked((byte)(raw[i] - ((left + up) / 2)));
                }

                break;
            case PngFilterType.Paeth:
                for (var i = 0; i < raw.Length; i++)
                {
                    int left = i >= bpp ? raw[i - bpp] : 0;
                    int up = previousRaw.IsEmpty ? 0 : previousRaw[i];
                    int upLeft = i >= bpp && !previousRaw.IsEmpty ? previousRaw[i - bpp] : 0;
                    output[i] = unchecked((byte)(raw[i] - Paeth(left, up, upLeft)));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(filterType));
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        return pb <= pc ? b : c;
    }
}
