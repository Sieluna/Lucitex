using Lucitex.Benchmarks.Formats;
using Lucitex.Benchmarks.Suites;
using Lucitex.Png.Format;

namespace Lucitex.Benchmarks.Validation;

internal static class VerificationRunner
{
    public static int Run(RunOptions options)
    {
        foreach (var comparison in FormatCatalog.SelectCases(options)) {
            if (ValidationStore.TryValidate(comparison)) {
                var result = ValidationStore.Results[comparison.Key];
                var status = result.AcceptanceFailed ? "SIZE-FAIL" : !result.PairingMatched ? "UNMATCHED" : "PASS";
                Console.WriteLine($"{status} {comparison.Key}; correctness passed; eligible for timing");
            }
            else if (ValidationStore.Skipped.TryGetValue(comparison.Key, out var reason)) Console.WriteLine($"SKIP {comparison.Key}: {reason}");
            else Console.Error.WriteLine($"FAIL {comparison.Key}: {ValidationStore.Errors[comparison.Key]}");
        }
        if (options.Suites.HasFlag(Suite.Core)) {
            foreach (var image in options.Cases) {
                new CoreExecutionBenchmarks { Case = image }.Setup().GetAwaiter().GetResult();
                Console.WriteLine($"PASS core sync/async equivalence {image}");
            }
        }
        if (options.IncludesKernels()) {
            foreach (var image in options.Cases) {
                new Crc32Benchmarks { Case = image }.Setup();
                foreach (var filter in new[] { PngFilterType.Sub, PngFilterType.Up, PngFilterType.Average, PngFilterType.Paeth }) {
                    new PngFilterBenchmarks { Case = image, Filter = filter }.Setup();
                }
                Console.WriteLine($"PASS PNG.CRC32 and PNG.Filter.Apply/Reconstruct {image}");
            }
        }
        ValidationStore.Save();
        Reporting.ComparisonExporter.WriteIndex(options);
        Console.WriteLine($"Validation: {Path.Combine(options.Artifacts, "validation.json")}");
        return ValidationStore.HasErrors || ValidationStore.HasAcceptanceFailures ? 1 : 0;
    }
}
