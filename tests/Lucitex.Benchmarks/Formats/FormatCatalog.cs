using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Suites;

namespace Lucitex.Benchmarks.Formats;

internal static class FormatCatalog
{
    public static IReadOnlyList<IFormatModule> Modules { get; } = [new JpegModule(), new PngModule(), new ExrModule(), new Ktx2Module()];
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
    ];
    public static IReadOnlyList<string> PlannedFormats { get; } = ["dds", "hdr"];
    public static IEnumerable<string> Profiles => Modules.SelectMany(m => m.Profiles);
    public static IFormatModule Get(string id) => Modules.Single(m => m.Id == id);
    public static IFormatModule ForProfile(string profile) => Modules.Single(m => m.Profiles.Contains(profile));

    public static IEnumerable<Type> SelectBenchmarks(RunOptions options)
    {
        foreach (var module in Modules.Where(m => m.Profiles.Intersect(options.Profiles).Any())) {
            if (options.Suite == "all" || options.Suite == module.Id) {
                foreach (var type in module.BenchmarkTypes) {
                    yield return type;
                }
            }
            if (options.Suite is "all" or "convert") {
                foreach (var route in Conversions.Where(r => r.Destination == module.Id)) {
                    yield return route.BenchmarkType;
                }
            }
        }
        if (options.Suite is "all" or "kernels") {
            yield return typeof(Crc32Benchmarks);
            yield return typeof(PngFilterBenchmarks);
        }
    }

    public static IEnumerable<ComparisonCase> SelectCases(RunOptions options)
    {
        foreach (var profile in options.Profiles) {
            var format = ForProfile(profile).Id;
            if (options.Suite is not ("all" or "convert") && options.Suite != format) {
                continue;
            }
            foreach (var image in options.Cases) {
                foreach (var library in options.Libraries.Select(Enum.Parse<Library>)) {
                    foreach (var operation in Enum.GetValues<Operation>()) {
                        if (operation == Operation.Convert && options.Suite is not ("all" or "convert")
                            || operation != Operation.Convert && options.Suite == "convert") {
                            continue;
                        }
                        if (operation == Operation.Convert) {
                            foreach (var route in Conversions.Where(r => r.Destination == format)) {
                                yield return new ComparisonCase(image, profile, library, operation, route.Source);
                            }
                        }
                        else {
                            yield return new ComparisonCase(image, profile, library, operation);
                        }
                    }
                }
            }
        }
    }
}
