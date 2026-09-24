using System.Diagnostics;

namespace Lucitex.Fuzz;

internal sealed record FuzzOptions(int Iterations, int RandomSeed, string Artifacts, string? Oracle,
    string? Corpus, MutationMode Mutation);

internal static class FuzzRunner
{
    public static int Run(FuzzOptions options)
    {
        var generated = SeedCorpus.Create();
        var seeds = generated.Concat(CorpusLoader.Load(options.Corpus)).ToArray();
        using IDecodeOracle? oracle = options.Oracle is null ? null : new FfiOracle(options.Oracle);
        foreach (var seed in generated) {
            var outcome = ManagedDecoder.Decode(seed.Format, seed.Bytes);
            if (!outcome.Accepted) {
                Console.Error.WriteLine($"Generated seed {seed.Name}: {outcome.Status}: {outcome.Detail}");
                return 1;
            }
            if (oracle?.Supports(seed.Format) == true) {
                var native = oracle.Decode(seed.Format, seed.Bytes);
                if (!native.Accepted && native.Status != DecodeStatus.Unsupported) {
                    Console.Error.WriteLine($"Oracle preflight {seed.Name}: {native.Status}: {native.Detail}");
                    return 1;
                }
                if (native.Status == DecodeStatus.Unsupported) {
                    Console.WriteLine($"Oracle preflight {seed.Name}: unsupported");
                }
            }
        }

        var store = new ArtifactStore(options.Artifacts);
        foreach (var sample in ConformanceCases.Create(generated)) {
            var managed = ManagedDecoder.Decode(sample.Format, sample.Bytes);
            var native = oracle?.Supports(sample.Format) == true ? oracle.Decode(sample.Format, sample.Bytes) : null;
            if (managed.Status != DecodeStatus.Rejected || (native is not null && native.Status != DecodeStatus.Rejected)) {
                var path = store.Save(sample, options.RandomSeed, -1, new(sample.Bytes, "conformance"), managed, native, ComparisonKind.Failure, options);
                Console.Error.WriteLine($"Conformance {sample.Name}: expected rejection, managed={managed.Status}, native={native?.Status} ({path})");
                return 1;
            }
        }
        var random = new Random(options.RandomSeed);
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        var failed = false;
        var stopwatch = Stopwatch.StartNew();
        var executed = 0;
        for (var iteration = 0; iteration < options.Iterations; iteration++) {
            var seed = seeds[random.Next(seeds.Length)];
            var mutation = MutationEngine.Mutate(seed, random, options.Mutation);
            var managed = ManagedDecoder.Decode(seed.Format, mutation.Bytes);
            var native = oracle?.Supports(seed.Format) == true ? oracle.Decode(seed.Format, mutation.Bytes) : null;
            var comparison = DifferentialComparison.Compare(managed, native);
            executed++;
            Count($"{seed.Format.Extension()}.{mutation.Strategy}.managed.{managed.Status}");
            if (native is not null) {
                Count($"native.{native.Status}");
            }
            Count($"comparison.{comparison}");
            if (comparison is ComparisonKind.Failure or ComparisonKind.AcceptanceMismatch) {
                failed = true;
                var path = store.Save(seed, options.RandomSeed, iteration, mutation, managed, native, comparison, options);
                Console.Error.WriteLine($"{comparison}: managed={managed.Status} native={native?.Status.ToString() ?? "disabled"} ({path})");
            }
            if (native?.Status == DecodeStatus.InfrastructureFailure) {
                break;
            }
        }
        Console.WriteLine($"iterations={executed} seed={options.RandomSeed} elapsed_ms={stopwatch.ElapsedMilliseconds}");
        foreach (var (key, value) in counters.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            Console.WriteLine($"{key}={value}");
        }
        return failed ? 1 : 0;

        void Count(string key) => counters[key] = counters.GetValueOrDefault(key) + 1;
    }
}
