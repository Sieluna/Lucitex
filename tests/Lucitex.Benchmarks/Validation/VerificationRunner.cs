using Lucitex.Benchmarks.Formats;
using Lucitex.Benchmarks.Suites;
using Lucitex.Png.Format;

namespace Lucitex.Benchmarks.Validation;

internal static class VerificationRunner
{
    public static int Run(RunOptions options)
    {
        foreach (var comparison in FormatCatalog.SelectCases(options)) {
            if (comparison.UnsupportedReason() is { } reason) {
                ValidationStore.Skip(comparison, reason);
                Console.WriteLine($"SKIP {comparison.Key}: {reason}");
                continue;
            }
            try {
                ValidationStore.Validate(comparison);
                Console.WriteLine($"PASS {comparison.Key}");
            }
            catch (Exception exception) {
                Console.Error.WriteLine($"FAIL {comparison.Key}: {exception.Message}");
            }
        }
        if (options.Suite is "all" or "kernels") {
            foreach (var image in options.Cases) {
                new Crc32Benchmarks { Case = image }.Setup();
                foreach (var filter in new[] { PngFilterType.Sub, PngFilterType.Up, PngFilterType.Average, PngFilterType.Paeth }) {
                    new PngFilterBenchmarks { Case = image, Filter = filter }.Setup();
                }
                Console.WriteLine($"PASS PNG.CRC32 and PNG.Filter.Apply/Reconstruct {image}");
            }
        }
        ValidationStore.Save();
        Console.WriteLine($"Validation: {Path.Combine(options.Artifacts, "validation.json")}");
        return ValidationStore.HasErrors ? 1 : 0;
    }
}
