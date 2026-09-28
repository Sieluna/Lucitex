using System.IO.Compression;
using Lucitex.Webp;

namespace Lucitex.Benchmarks.Codecs;

// Candidates are search points, not cross-library effort levels or result tiers.
internal sealed record EncoderCandidate(string Id, int? PngLevel = null,
    CompressionLevel? PngCompression = null, int WebpMethod = 4, int WebpQuality = 100,
    WebpCompressionEffort? WebpEffort = null, bool OptimizeHuffman = true);

internal static class EncoderCandidates
{
    public const string Version = "1";
    private static readonly EncoderCandidate Default = new("default");
    private static readonly Dictionary<(string, Library), IReadOnlyList<EncoderCandidate>> Catalog =
        (from format in new[] { "png", "webp", "jpeg", "exr", "ktx2" } from library in Enum.GetValues<Library>()
            select (format, library)).ToDictionary(key => key, key => Build(key.format, key.library));
    private static readonly Dictionary<(string, Library, string), EncoderCandidate> Lookup = Catalog
        .SelectMany(entry => entry.Value.Select(c => (Key: (entry.Key.Item1, entry.Key.Item2, c.Id), Value: c)))
        .ToDictionary(entry => entry.Key, entry => entry.Value);

    public static IReadOnlyList<EncoderCandidate> For(string format, Library library) => Catalog[(format, library)];

    private static IReadOnlyList<EncoderCandidate> Build(string format, Library library) => (format, library) switch {
        ("png", Library.Lucitex) => [
            new("png-store", PngCompression: CompressionLevel.NoCompression),
            new("png-fast", PngCompression: CompressionLevel.Fastest),
            new("png-optimal", PngCompression: CompressionLevel.Optimal),
            new("png-smallest", PngCompression: CompressionLevel.SmallestSize)],
        ("png", _) => [new("png-z0", PngLevel: 0), new("png-z1", PngLevel: 1),
            new("png-z6", PngLevel: 6), new("png-z9", PngLevel: 9)],
        ("webp", Library.Lucitex) => [new("webp-fast", WebpEffort: WebpCompressionEffort.Fast),
            new("webp-balanced", WebpEffort: WebpCompressionEffort.Balanced),
            new("webp-best", WebpEffort: WebpCompressionEffort.Best)],
        ("webp", Library.SkiaSharp) => [new("webp-q0", WebpMethod: 0, WebpQuality: 0),
            new("webp-q75", WebpMethod: 0, WebpQuality: 75), new("webp-q100", WebpMethod: 0)],
        // Keep Magick quality at 100: native versions may also use quality for near-lossless.
        ("webp", Library.MagickNet) => [new("webp-m0-q100", WebpMethod: 0),
            new("webp-m4-q100"), new("webp-m6-q100", WebpMethod: 6)],
        ("webp", _) => (from method in new[] { 0, 4, 6 } from quality in new[] { 0, 75, 100 }
            select new EncoderCandidate($"webp-m{method}-q{quality}", WebpMethod: method, WebpQuality: quality)).ToArray(),
        ("jpeg", Library.Lucitex) => [new("jpeg-huffman", OptimizeHuffman: true), new("jpeg-fixed", OptimizeHuffman: false)],
        _ => [new("fixed")],
    };

    public static EncoderCandidate Resolve(ComparisonCase comparison) => comparison.Candidate == "default"
        ? Default : Lookup[(comparison.Format, comparison.Library, comparison.Candidate)];

    public static IEnumerable<string> Ids(string format, RunOptions options) => !options.Tradeoffs
        ? ["default"]
        : options.TradeoffSelections.Length > 0
            ? options.TradeoffSelections.Where(s => Formats.FormatCatalog.ForProfile(s.Profile).Id == format).Select(s => s.Candidate).Distinct()
            : options.Libraries.SelectMany(l => For(format, Enum.Parse<Library>(l))).Select(c => c.Id).Distinct();

    public static bool Includes(ComparisonCase comparison, RunOptions options) => !options.Tradeoffs
        ? comparison.Candidate == "default"
        : comparison.Operation == Operation.Encode
            && For(comparison.Format, comparison.Library).Any(c => c.Id == comparison.Candidate)
            && (options.TradeoffSelections.Length == 0 || options.TradeoffSelections.Any(s =>
                s.Profile == comparison.Profile && s.Library == comparison.Library && s.Candidate == comparison.Candidate));
}
