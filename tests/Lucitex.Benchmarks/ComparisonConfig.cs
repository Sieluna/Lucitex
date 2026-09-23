using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Diagnostics.Windows;
using BenchmarkDotNet.Exporters;
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
        var job = options.Quick ? Job.Dry.WithId("Smoke") : Job.Default.WithId("Comparison");
        job = job.WithEnvironmentVariable("LUCITEX_COMPARISON_OPTIONS", Environment.GetEnvironmentVariable("LUCITEX_COMPARISON_OPTIONS")!);
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .WithArtifactsPath(options.Artifacts)
            .AddJob(job)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddColumn(RankColumn.Arabic, StatisticalTestColumn.Create("5%"))
            .AddColumn(ComparisonColumn.Create())
            .AddExporter(JsonExporter.Full, new ComparisonExporter())
            .AddValidator(new AccuracyValidator())
            .AddFilter(new CapabilityFilter(options));
        if (options.Memory == "etw") {
            config.AddDiagnoser(new NativeMemoryProfiler());
        }
        return config;
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
            if (comparison.UnsupportedReason() is { } reason) {
                ValidationStore.Skip(comparison, reason);
                return false;
            }
            return true;
        }
    }
}
