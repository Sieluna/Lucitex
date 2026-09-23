namespace Lucitex.Benchmarks.Data;

internal static class PixelLayout
{
    public static byte[] ToRgb(ReadOnlySpan<byte> rgba)
    {
        var rgb = new byte[rgba.Length / 4 * 3];
        for (var i = 0; i < rgba.Length / 4; i++) {
            rgba.Slice(i * 4, 3).CopyTo(rgb.AsSpan(i * 3));
        }
        return rgb;
    }

    public static byte[] ToRgba(ReadOnlySpan<byte> rgb)
    {
        var rgba = new byte[rgb.Length / 3 * 4];
        for (var i = 0; i < rgb.Length / 3; i++) {
            rgb.Slice(i * 3, 3).CopyTo(rgba.AsSpan(i * 4));
            rgba[i * 4 + 3] = 255;
        }
        return rgba;
    }
}
