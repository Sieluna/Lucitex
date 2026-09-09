using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Lucitex.Benchmarks;

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args, new BenchmarkConfig());

namespace Lucitex.Benchmarks
{
    internal sealed class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            AddJob(Job.Default
                .WithLaunchCount(1)
                .WithWarmupCount(3)
                .WithIterationCount(5)
                .WithEnvironmentVariable("DOTNET_TieredCompilation", "0")
                .WithEnvironmentVariable("DOTNET_ReadyToRun", "0")
                .WithId("Lucitex"));

            WithOptions(ConfigOptions.DisableOptimizationsValidator);
            AddExporter(DefaultConfig.Instance.GetExporters().ToArray());
            AddLogger(DefaultConfig.Instance.GetLoggers().ToArray());
            AddColumnProvider(DefaultConfig.Instance.GetColumnProviders().ToArray());
            AddDiagnoser(DefaultConfig.Instance.GetDiagnosers().ToArray());
            AddAnalyser(DefaultConfig.Instance.GetAnalysers().ToArray());
            AddValidator(DefaultConfig.Instance.GetValidators().ToArray());
        }
    }
}

public partial class Program;
