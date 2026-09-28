using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Reporting;
using Lucitex.Benchmarks.Validation;

namespace Lucitex.Benchmarks;

internal static class ComparisonConfig
{
    public static IConfig Create(RunOptions options)
    {
        var job = options.Smoke ? Job.Dry.WithId("Smoke") : Job.Default.WithId("Comparison");
        job = job.WithEnvironmentVariable("LUCITEX_COMPARISON_OPTIONS", Environment.GetEnvironmentVariable("LUCITEX_COMPARISON_OPTIONS")!);
        return ManualConfig.Create(DefaultConfig.Instance)
            .WithArtifactsPath(options.Artifacts)
            .AddJob(job)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddColumn(ComparisonColumn.Create())
            .AddExporter(JsonExporter.Full, new ComparisonExporter())
            .AddFilter(new CapabilityFilter(options));
    }

    private sealed class CapabilityFilter(RunOptions options) : IFilter
    {
        public bool Predicate(BenchmarkCase benchmarkCase)
        {
            if (ComparisonCase.From(benchmarkCase) is not { } comparison) {
                return true;
            }
            if (!options.Libraries.Contains(comparison.Library.ToString())) {
                return false;
            }
            return ValidationStore.TryValidate(comparison);
        }
    }
}
