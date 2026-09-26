using System.Diagnostics;
using Lucitex.Core.Execution;

namespace Lucitex.Fuzz;

internal static class FuzzApplication
{
    public static int Run(string[] args)
    {
        try {
            return Dispatch(args);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException) {
            return UsageError(exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static int Dispatch(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help") {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        return args[0] switch {
            "run" => RunFuzz(args[1..]),
            "replay" => Replay(args[1..]),
            "generate" => Generate(args[1..]),
            "generate-bench" => GenerateBenchmark(args[1..]),
            "bench" => Benchmark(args[1..]),
            _ => UsageError($"Unknown command '{args[0]}'."),
        };
    }

    private static int RunFuzz(string[] args)
    {
        var options = RunOptionsParser.Parse(args);
        return FuzzRunner.Run(options);
    }

    private static int Replay(string[] args)
    {
        if (args.Length is not (2 or 4) || !ImageFormatExtensions.TryParse(args[0], out var format)) {
            return UsageError("replay requires: <png|exr|hdr|ktx2|jpg|webp> <path> [--oracle LIBRARY].");
        }
        var oraclePath = ParseOracleOption(args.AsSpan(2));
        var bytes = CorpusLoader.ReadInput(args[1]);
        var managed = ManagedDecoder.Decode(format, bytes);
        using var oracle = oraclePath is null ? null : new FfiOracle(oraclePath);
        var native = oracle?.Decode(format, bytes);
        var comparison = DifferentialComparison.Compare(managed, native);
        Console.WriteLine($"managed={managed.Status}: {managed.Detail}");
        if (native is not null) {
            Console.WriteLine($"native={native.Status}: {native.Detail}");
        }
        Console.WriteLine($"comparison={comparison}");
        return comparison is ComparisonKind.Failure or ComparisonKind.AcceptanceMismatch ? 1 : 0;
    }

    private static int Generate(string[] args)
    {
        if (args.Length != 1) {
            return UsageError("generate requires: <directory>.");
        }

        Directory.CreateDirectory(args[0]);
        foreach (var seed in SeedCorpus.Create()) {
            File.WriteAllBytes(Path.Combine(args[0], seed.Name), seed.Bytes);
        }

        return 0;
    }

    private static int Benchmark(string[] args)
    {
        if (args.Length is not (3 or 5) || !ImageFormatExtensions.TryParse(args[0], out var format) ||
            !int.TryParse(args[2], out var iterations) || iterations <= 0) {
            return UsageError("bench requires: <png|exr|hdr|ktx2|jpg|webp> <path> <iterations> [--oracle LIBRARY].");
        }
        var oraclePath = args.Length == 5 ? ParseOracleOption(args.AsSpan(3)) : null;
        using var oracle = oraclePath is null ? null : new FfiOracle(oraclePath);
        var data = File.ReadAllBytes(args[1]);
        var limits = DecodeLimits.Default with { MaxDecodedBytes = 512 * 1024 * 1024, MaxWorkingSet = 512 * 1024 * 1024 };
        DecodeOutcome Decode() => oracle is null ? ManagedDecoder.Decode(format, data, DecodeLimits.Default) : oracle.Decode(format, data, limits);
        for (var i = 0; i < Math.Min(10, iterations); i++) {
            var result = Decode();
            if (!result.Accepted) {
                Console.Error.WriteLine($"Benchmark input: {result.Status}: {result.Detail}");
                return 1;
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++) {
            var result = Decode();
            if (!result.Accepted) {
                Console.Error.WriteLine($"Benchmark input: {result.Status}: {result.Detail}");
                return 1;
            }
        }

        stopwatch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        Console.WriteLine($"backend={(oracle is null ? "managed" : "ffi")} iterations={iterations} elapsed_ns={stopwatch.Elapsed.TotalNanoseconds:F0} ns_per_iteration={stopwatch.Elapsed.TotalNanoseconds / iterations:F0} managed_allocated_bytes_per_iteration={allocated / iterations}");
        return 0;
    }

    private static int GenerateBenchmark(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[1], out var width) || !int.TryParse(args[2], out var height) ||
            width <= 0 || height <= 0) {
            return UsageError("generate-bench requires: <directory> <width> <height>.");
        }

        Directory.CreateDirectory(args[0]);
        foreach (var seed in SeedCorpus.CreateBenchmark(width, height)) {
            File.WriteAllBytes(Path.Combine(args[0], seed.Name), seed.Bytes);
        }

        return 0;
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 2;
    }

    private static string? ParseOracleOption(ReadOnlySpan<string> args)
    {
        if (args.IsEmpty) {
            return null;
        }
        if (args.Length == 2 && args[0] == "--oracle" && !string.IsNullOrWhiteSpace(args[1])) {
            return args[1];
        }
        throw new ArgumentException("Expected [--oracle LIBRARY].");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Lucitex.Fuzz run [--iterations N] [--seed N] [--oracle LIBRARY] [--artifacts DIR] [--corpus DIR] [--mutation mixed|raw|structured]");
        Console.WriteLine("Lucitex.Fuzz replay <png|exr|hdr|ktx2|jpg|webp> <path> [--oracle LIBRARY]");
        Console.WriteLine("Lucitex.Fuzz generate <directory>");
        Console.WriteLine("Lucitex.Fuzz generate-bench <directory> <width> <height>");
        Console.WriteLine("Lucitex.Fuzz bench <png|exr|hdr|ktx2|jpg|webp> <path> <iterations> [--oracle LIBRARY]");
    }
}
