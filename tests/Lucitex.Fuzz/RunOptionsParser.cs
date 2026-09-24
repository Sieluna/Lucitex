using System.Globalization;

namespace Lucitex.Fuzz;

internal static class RunOptionsParser
{
    public static FuzzOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2) {
            if (args[i] is not ("--iterations" or "--seed" or "--oracle" or "--artifacts" or "--corpus" or "--mutation")) {
                throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
            if (i + 1 == args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
                throw new ArgumentException($"Missing value for {args[i]}.");
            }
            if (!values.TryAdd(args[i], args[i + 1])) {
                throw new ArgumentException($"Duplicate option '{args[i]}'.");
            }
        }
        var iterations = Integer("--iterations", 10_000);
        if (iterations <= 0) {
            throw new ArgumentException("Iterations must be positive.");
        }
        var mutation = values.GetValueOrDefault("--mutation", "mixed") switch {
            "mixed" => MutationMode.Mixed,
            "raw" => MutationMode.Raw,
            "structured" => MutationMode.Structured,
            var value => throw new ArgumentException($"Unknown mutation mode '{value}'."),
        };
        return new FuzzOptions(iterations, Integer("--seed", 0x5eed),
            values.GetValueOrDefault("--artifacts", Path.Combine(AppContext.BaseDirectory, "fuzz-artifacts")),
            values.GetValueOrDefault("--oracle"), values.GetValueOrDefault("--corpus"), mutation);

        int Integer(string name, int fallback) => values.TryGetValue(name, out var value)
            ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;
    }
}
