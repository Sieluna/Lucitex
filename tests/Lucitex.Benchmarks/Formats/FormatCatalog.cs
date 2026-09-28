using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Suites;

namespace Lucitex.Benchmarks.Formats;

internal static class FormatCatalog
{
    public static IReadOnlyList<IFormatModule> Modules { get; } = [new JpegModule(), new PngModule(), new ExrModule(), new Ktx2Module(), new WebpModule()];
    public static IReadOnlyList<ConversionRoute> Conversions { get; } = [
        new("png", "jpeg", typeof(PngToJpegBenchmarks)),
        new("jpeg", "png", typeof(JpegToPngBenchmarks)),
        new("png", "exr", typeof(PngToExrBenchmarks)),
        new("exr", "png", typeof(ExrToPngBenchmarks)),
        new("jpeg", "exr", typeof(JpegToExrBenchmarks)),
        new("exr", "jpeg", typeof(ExrToJpegBenchmarks)),
        new("png", "ktx2", typeof(PngToKtx2Benchmarks)),
        new("ktx2", "png", typeof(Ktx2ToPngBenchmarks)),
        new("jpeg", "ktx2", typeof(JpegToKtx2Benchmarks)),
        new("ktx2", "jpeg", typeof(Ktx2ToJpegBenchmarks)),
        new("exr", "ktx2", typeof(ExrToKtx2Benchmarks)),
        new("ktx2", "exr", typeof(Ktx2ToExrBenchmarks)),
        new("png", "webp", typeof(PngToWebpBenchmarks)),
        new("webp", "png", typeof(WebpToPngBenchmarks)),
        new("jpeg", "webp", typeof(JpegToWebpBenchmarks)),
        new("webp", "jpeg", typeof(WebpToJpegBenchmarks)),
        new("exr", "webp", typeof(ExrToWebpBenchmarks)),
        new("webp", "exr", typeof(WebpToExrBenchmarks)),
        new("ktx2", "webp", typeof(Ktx2ToWebpBenchmarks)),
        new("webp", "ktx2", typeof(WebpToKtx2Benchmarks)),
    ];
    public static IEnumerable<string> Profiles => Modules.SelectMany(m => m.Profiles);
    public static IFormatModule Get(string id) => Modules.Single(m => m.Id == id);
    public static IFormatModule ForProfile(string profile) => Modules.Single(m => m.Profiles.Contains(profile));

    public static IEnumerable<Type> SelectBenchmarks(RunOptions options)
    {
        foreach (var module in Modules.Where(m => m.Profiles.Intersect(options.Profiles).Any())) {
            if (options.IncludesFormat(module.Id)) {
                foreach (var type in module.BenchmarkTypes) {
                    if (options.Tradeoffs && !type.Name.Contains("Encode", StringComparison.Ordinal)) continue;
                    if (type == typeof(JpegDecodeBenchmarks) && !options.Profiles.Contains("jpeg-quality420")) continue;
                    yield return type;
                }
            }
            if (options.IncludesConvert() && !options.Tradeoffs) {
                foreach (var route in Conversions.Where(r => r.Destination == module.Id && options.IncludesConvertRoute(r.Source, r.Destination))) {
                    yield return route.BenchmarkType;
                }
            }
        }
        if (options.IncludesKernels() && !options.Tradeoffs) {
            yield return typeof(Crc32Benchmarks);
            yield return typeof(PngFilterBenchmarks);
        }
        if (options.Suites.HasFlag(Suite.Core) && !options.Tradeoffs) {
            yield return typeof(CoreExecutionBenchmarks);
        }
    }

    public static IEnumerable<ComparisonCase> SelectCases(RunOptions options)
    {
        foreach (var profile in options.Profiles) {
            var format = ForProfile(profile).Id;
            var routes = Conversions.Where(r => r.Destination == format && options.IncludesConvertRoute(r.Source, r.Destination)).ToArray();
            foreach (var pair in from image in options.Cases from library in options.Libraries select (image, library)) {
                var sample = new ComparisonCase(pair.image, profile, Enum.Parse<Library>(pair.library), Operation.Encode);
                if (options.Tradeoffs) {
                    if (options.IncludesFormat(format)) {
                        foreach (var candidate in EncoderCandidates.For(format, sample.Library)) {
                            var comparison = sample with { Candidate = candidate.Id };
                            if (EncoderCandidates.Includes(comparison, options)) yield return comparison;
                        }
                    }
                    continue;
                }
                if (options.IncludesFormat(format)) {
                    yield return sample;
                    if (!sample.RateMatched) yield return sample with { Operation = Operation.Decode };
                }
                foreach (var route in routes) yield return sample with { Operation = Operation.Convert, SourceFormat = route.Source };
            }
        }
    }
}
