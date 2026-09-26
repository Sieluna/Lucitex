using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lucitex.Fuzz;

internal sealed class ArtifactStore(string directory)
{
    private static readonly JsonSerializerOptions s_Json = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Save(SeedInput seed, int randomSeed, int iteration, MutationInput mutation,
        DecodeOutcome managed, DecodeOutcome? native, ComparisonKind comparison, FuzzOptions options)
    {
        Directory.CreateDirectory(directory);
        var hash = Convert.ToHexStringLower(SHA256.HashData(mutation.Bytes));
        var path = Path.Combine(directory, $"{seed.Format.Extension()}-{hash}.{seed.Format.Extension()}");
        File.WriteAllBytes(path, mutation.Bytes);
        var report = new {
            SchemaVersion = 1,
            HarnessVersion = typeof(ArtifactStore).Assembly.GetName().Version?.ToString(),
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            RunOptions = options,
            Seed = seed.Name,
            SeedSha256 = Convert.ToHexStringLower(SHA256.HashData(seed.Bytes)),
            RandomSeed = randomSeed,
            Iteration = iteration,
            Mutation = mutation.Strategy,
            Sha256 = hash,
            Format = seed.Format,
            Managed = managed,
            Native = native,
            Comparison = comparison,
            Limits = FuzzLimits.Decode,
            Replay = $"replay {seed.Format.Extension()} \"{Path.GetFullPath(path)}\"",
        };
        File.WriteAllText(path + ".json", JsonSerializer.Serialize(report, s_Json));
        return path;
    }
}
