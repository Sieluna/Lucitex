namespace Lucitex.Benchmarks.Codecs;

internal static class CodecCapabilities
{
    private static readonly IReadOnlyDictionary<Library, string[]> s_Formats = new Dictionary<Library, string[]> {
        [Library.Lucitex] = ["jpeg", "png", "exr", "ktx2", "webp"],
        [Library.ImageSharp] = ["jpeg", "png", "webp"],
        [Library.SkiaSharp] = ["jpeg", "png", "webp"],
        [Library.NetVips] = ["jpeg", "png", "webp"],
        [Library.MagickNet] = ["jpeg", "png", "exr", "webp"],
    };

    public static bool Supports(Library library, string format) => s_Formats.TryGetValue(library, out var formats) && formats.Contains(format);

    public static bool IsComparable(string source, string destination) =>
        Enum.GetValues<Library>().Count(l => Supports(l, source) && Supports(l, destination)) >= 2;
}
