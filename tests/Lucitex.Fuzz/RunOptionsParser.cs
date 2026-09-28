using System.Globalization;

namespace Lucitex.Fuzz;

internal static class RunOptionsParser
{
    public static FuzzOptions Parse(string[] args, bool qualityOnly = false)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2) {
            if (args[i] is not ("--iterations" or "--seed" or "--oracle" or "--artifacts" or "--corpus" or "--mutation" or "--quality-iterations")) {
                throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
            if (qualityOnly && args[i] is not ("--quality-iterations" or "--seed" or "--artifacts" or "--oracle")) {
                throw new ArgumentException($"Option '{args[i]}' is not supported by the quality command.");
            }
            if (i + 1 == args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
                throw new ArgumentException($"Missing value for {args[i]}.");
            }
            if (!values.TryAdd(args[i], args[i + 1])) {
                throw new ArgumentException($"Duplicate option '{args[i]}'.");
            }
        }
        var iterations = Integer("--iterations", 256);
        var qualityIterations = Integer("--quality-iterations", 8);
        if (iterations <= 0 || qualityIterations <= 0) {
            throw new ArgumentException("Iterations must be positive.");
        }
        if (string.IsNullOrWhiteSpace(values.GetValueOrDefault("--oracle")))
            throw new ArgumentException("Encoding quality checks require --oracle LIBRARY (native ABI 2).");
        var mutation = values.GetValueOrDefault("--mutation", "mixed") switch {
            "mixed" => MutationMode.Mixed,
            "raw" => MutationMode.Raw,
            "structured" => MutationMode.Structured,
            var value => throw new ArgumentException($"Unknown mutation mode '{value}'."),
        };
        return new FuzzOptions(iterations, Integer("--seed", 0x5eed),
            values.GetValueOrDefault("--artifacts", Path.Combine(AppContext.BaseDirectory, "fuzz-artifacts")),
            values.GetValueOrDefault("--oracle"), values.GetValueOrDefault("--corpus"), mutation, qualityIterations);

        int Integer(string name, int fallback) => values.TryGetValue(name, out var value)
            ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;
    }
}
