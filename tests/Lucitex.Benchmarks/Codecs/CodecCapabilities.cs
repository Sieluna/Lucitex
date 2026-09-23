namespace Lucitex.Benchmarks.Codecs;

internal static class CodecCapabilities
{
    private static readonly IReadOnlyDictionary<Library, string[]> s_Formats = new Dictionary<Library, string[]> {
        [Library.Lucitex] = ["jpeg", "png", "exr", "ktx2"],
        [Library.ImageSharp] = ["jpeg", "png"],
        [Library.SkiaSharp] = ["jpeg", "png"],
        [Library.NetVips] = ["jpeg", "png"],
        [Library.MagickNet] = ["jpeg", "png", "exr"],
    };

    public static bool Supports(Library library, string format) => s_Formats.TryGetValue(library, out var formats) && formats.Contains(format);
}
