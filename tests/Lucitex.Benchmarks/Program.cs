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
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException) {
            Console.Error.WriteLine(exception.Message);
            return 1;
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
        if (args is ["--list-formats"]) {
            foreach (var module in FormatCatalog.Modules) {
                Console.WriteLine($"READY {module.Id}: {string.Join(',', module.Profiles)}");
            }
            return 0;
        }
        if (args is ["--help"]) {
            Console.WriteLine("""
                Lucitex benchmarks — quality-gated codec comparisons (BenchmarkDotNet)
                --suite all|core,convert,jpeg,png,exr,ktx2,webp,kernels (default: all)
                --mode full|smoke (default: full)
                    full: adaptive measurement and exhaustive JPEG quality search.
                    smoke: pipeline check only; no performance conclusions.
                --cases <comma-separated pattern@widthxheight>
                    Default: flat/alpha 256x256, ramp 128x128, checker 129x97, noise 2048x2048.
                    Explicit cases are preserved in every mode; dimensions 1..8192.
                    Patterns: ramp,checker,impulse,stripes,chroma,flat,alpha,noise.
                --profiles all|<comma-separated profiles; see --list-formats>
                    Default: JPEG quality/rate pairing, PNG, EXR ZIP, KTX2, WebP Fast/Balanced/Best.
                --libraries Lucitex,ImageSharp,SkiaSharp,NetVips,MagickNet (default: all five)
                --comparable Only formats/routes supported by 2+ selected libraries.
                --threads N (default: 1; adapter budget, not an OS thread cap)
                --memory process|managed (default: process; timing runs only)
                    process includes sampled native memory.
                --dataset-manifest <path> Required for external:ID cases.
                --output <empty directory>
                --verify   Quality/correctness checks only; no timing or memory probes.
                --list-formats

                Quality gates: every library must meet fidelity requirements. Only Lucitex
                must stay within 1.2x the smallest matched-quality/exact output; WebP Fast
                size is advisory. JPEG pairing: reference Q90 MSE ±5% or file bytes ±2%.
                Both profiles share a Q1..100 scan. Unmatched is not an error.
                Failed cases remain in reports and make the run fail; valid peers still run.

                BenchmarkDotNet options follow --, e.g. --suite png --mode smoke -- --filter *Encode*
                --verify accepts neither smoke mode nor BDN options.
                """);
            return 0;
        }
        var (options, benchmarkArguments) = RunOptions.Parse(args);
        Environment.SetEnvironmentVariable("LUCITEX_COMPARISON_OPTIONS", JsonSerializer.Serialize(options));
        if (!options.IncludesKernels() && !options.Suites.HasFlag(Suite.Core)
            && !FormatCatalog.SelectCases(options).Any(c => c.UnsupportedReason() is null))
            throw new ArgumentException("No supported cases match the selected profiles, inputs and libraries.");
        if (Directory.Exists(options.Artifacts) && Directory.EnumerateFileSystemEntries(options.Artifacts).Any())
            throw new ArgumentException("Use an empty --output directory so results from different runs cannot mix.");
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
        ComparisonExporter.WriteIndex(options, summaries);
        Console.WriteLine($"Report: {Path.Combine(options.Artifacts, "index.html")}");
        return ValidationStore.HasErrors || summaries.Length == 0 || summaries.Any(s => s.HasCriticalValidationErrors
            || s.Reports.Any(r => !r.Success || r.ResultStatistics is null)) ? 1 : 0;
    }
}
