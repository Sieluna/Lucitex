using System.Security.Principal;
using System.Text.Json;
using BenchmarkDotNet.Running;
using Lucitex.Benchmarks.Formats;
using Lucitex.Benchmarks.Measurement;
using Lucitex.Benchmarks.Reporting;
using Lucitex.Benchmarks.Validation;

namespace Lucitex.Benchmarks;

internal static class Program
{
    private static int Main(string[] args)
    {
        try {
            return Run(args);
        }
        catch (Exception exception) {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args is ["--memory-worker", var payload]) {
            ProcessMemoryProbe.RunWorker(payload);
            return 0;
        }
        if (args.Contains("--list-formats")) {
            foreach (var module in FormatCatalog.Modules) {
                Console.WriteLine($"READY {module.Id}: {string.Join(',', module.Profiles)}");
            }
            foreach (var format in FormatCatalog.PlannedFormats) {
                Console.WriteLine($"PLANNED {format}: not implemented; no measurements claimed");
            }
            return 0;
        }
        if (args.Contains("--help")) {
            Console.WriteLine("""
                Lucitex format comparisons (BenchmarkDotNet; Windows/Linux)
                No arguments: run every registered suite with the default corpus/profiles.
                --suite all|convert,jpeg,png,exr,ktx2,webp,kernels
                    Comma-separated; "all" wins if present
                --comparable Filters --suite to formats/routes with 2+ supporting
                    libraries, e.g. "--suite all --comparable" or "--suite ktx2
                    --comparable" (empty: no other library implements ktx2).
                    Never affects kernels.
                --cases ramp@128x128,checker@129x97
                    Patterns: ramp,checker,impulse,stripes,chroma,flat,alpha,noise
                    External: external:<manifest-id>, with --dataset-manifest <path>
                --profiles all|<comma-separated profiles; see --list-formats>
                --libraries Lucitex,ImageSharp,SkiaSharp,NetVips,MagickNet
                    All five selected by default. Magick.NET is also accepted.
                --memory managed|process|etw (default: process)
                    process: isolated total-process sampling, including native memory
                    etw: Windows administrator required; native heap allocation profiling
                --verify     Validate selected cases without timing benchmarks
                --quick      Pipeline smoke run; not performance evidence
                --list-formats
                --output <directory>
                Other arguments go to BenchmarkDotNet, e.g. --filter *To*.
                DESIGN.txt documents contracts, format extension and corpus import.
                """);
            return 0;
        }
        var (options, benchmarkArguments) = RunOptions.Parse(args);
        if (options.Memory == "etw" && (!OperatingSystem.IsWindows()
            || !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))) {
            throw new InvalidOperationException("ETW native heap profiling requires an elevated Windows process. Use --memory process for unprivileged whole-process measurements.");
        }
        Environment.SetEnvironmentVariable("LUCITEX_COMPARISON_OPTIONS", JsonSerializer.Serialize(options));
        Directory.CreateDirectory(options.Artifacts);
        if (options.VerifyOnly) {
            return VerificationRunner.Run(options);
        }
        if (!benchmarkArguments.Contains("--filter") && !benchmarkArguments.Contains("-f")) {
            benchmarkArguments = [.. benchmarkArguments, "--filter", "*"];
        }
        var projectDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(projectDirectory.FullName, "Lucitex.Benchmarks.csproj"))) {
            projectDirectory = projectDirectory.Parent ?? throw new DirectoryNotFoundException("Timing benchmarks require the Lucitex.Benchmarks source project. Use --verify for a published executable.");
        }
        Directory.SetCurrentDirectory(projectDirectory.FullName);
        var summaries = BenchmarkSwitcher.FromTypes(FormatCatalog.SelectBenchmarks(options).ToArray())
            .Run(benchmarkArguments, ComparisonConfig.Create(options)).ToArray();
        ValidationStore.Save();
        ComparisonExporter.WriteIndex(options);
        Console.WriteLine($"Report: {Path.Combine(options.Artifacts, "index.html")}");
        return ValidationStore.HasErrors || summaries.Length == 0 || summaries.Any(s => s.HasCriticalValidationErrors
            || s.Reports.Any(r => !r.Success || r.ResultStatistics is null)) ? 1 : 0;
    }
}
