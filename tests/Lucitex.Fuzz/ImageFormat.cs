namespace Lucitex.Fuzz;

internal enum ImageFormat
{
    Png,
    Exr,
    Hdr,
    Ktx2,
    Jpeg,
    Webp,
}

internal static class ImageFormatExtensions
{
    public static bool TryParse(string value, out ImageFormat format)
    {
        if (string.Equals(value, "png", StringComparison.OrdinalIgnoreCase)) {
            format = ImageFormat.Png;
            return true;
        }

        if (string.Equals(value, "exr", StringComparison.OrdinalIgnoreCase)) {
            format = ImageFormat.Exr;
            return true;
        }

        if (string.Equals(value, "ktx2", StringComparison.OrdinalIgnoreCase)) {
            format = ImageFormat.Ktx2;
            return true;
        }

        if (string.Equals(value, "hdr", StringComparison.OrdinalIgnoreCase)) {
            format = ImageFormat.Hdr;
            return true;
        }

        if (string.Equals(value, "jpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "jpg", StringComparison.OrdinalIgnoreCase)) {
            format = ImageFormat.Jpeg;
            return true;
        }

        if (string.Equals(value, "webp", StringComparison.OrdinalIgnoreCase)) {
            format = ImageFormat.Webp;
            return true;
        }

        format = default;
        return false;
    }

    public static string Extension(this ImageFormat format) => format switch {
        ImageFormat.Png => "png",
        ImageFormat.Exr => "exr",
        ImageFormat.Hdr => "hdr",
        ImageFormat.Ktx2 => "ktx2",
        ImageFormat.Jpeg => "jpg",
        ImageFormat.Webp => "webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}
