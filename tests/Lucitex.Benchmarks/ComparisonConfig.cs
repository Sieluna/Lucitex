using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.DotNetCli;
using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Reporting;
using Lucitex.Benchmarks.Validation;

namespace Lucitex.Benchmarks;

internal static class ComparisonConfig
{
    public static IConfig Create(RunOptions options)
    {
        var job = options.Smoke ? Job.Dry.WithId("Smoke") : Job.Default.WithId("Comparison");
        if (options.Tradeoffs) {
            var standard = CsProjCoreToolchain.NetCoreApp10_0;
            job = job.WithToolchain(new Toolchain("net10.0", new SourceProjectGenerator(), standard.Builder, standard.Executor));
        }
        job = job.WithEnvironmentVariable("LUCITEX_COMPARISON_OPTIONS", Environment.GetEnvironmentVariable("LUCITEX_COMPARISON_OPTIONS")!);
        return ManualConfig.Create(DefaultConfig.Instance)
            .WithArtifactsPath(options.Artifacts)
            .AddJob(job)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddColumn(ComparisonColumn.Create())
            .AddExporter(JsonExporter.Full, new ComparisonExporter())
            .AddFilter(new CapabilityFilter(options));
    }

    // The workspace can contain archived source snapshots under artifacts. Resolve
    // this executable's source project instead of searching the entire solution.
    private sealed class SourceProjectGenerator() : CsProjGenerator("net10.0",
        NetCoreAppSettings.NetCoreApp10_0.CustomDotNetCliPath!,
        NetCoreAppSettings.NetCoreApp10_0.PackagesPath!,
        NetCoreAppSettings.NetCoreApp10_0.RuntimeFrameworkVersion!)
    {
        protected override FileInfo GetProjectFilePath(Type benchmarkTarget, ILogger logger)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null) {
                var path = Path.Combine(directory.FullName, "Lucitex.Benchmarks.csproj");
                if (File.Exists(path)) return new FileInfo(path);
                directory = directory.Parent;
            }
            throw new FileNotFoundException("Cannot locate the benchmark source project.");
        }
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
            if (!EncoderCandidates.Includes(comparison, options)) return false;
            return ValidationStore.TryValidate(comparison);
        }
    }
}
