using System.Text.Json;
using Lucitex.Benchmarks.Formats;

namespace Lucitex.Benchmarks;

internal sealed record RunOptions
{
    public string[] Cases { get; init; } = ["ramp@128x128", "checker@129x97"];
    public string[] Profiles { get; init; } = ["jpeg-default420", "png-default", "exr-half-zip", "ktx2-rgba8-none", "webp-lossless"];
    public string[] Libraries { get; init; } = ["Lucitex", "ImageSharp", "SkiaSharp", "NetVips", "MagickNet"];
    public string Suite { get; init; } = "all";
    public string Memory { get; init; } = "process";
    public string? DatasetManifest { get; init; }
    public string Artifacts { get; init; } = Path.GetFullPath("artifacts/comparisons/" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
    public bool VerifyOnly { get; init; }
    public bool Quick { get; init; }
    public static RunOptions Current => JsonSerializer.Deserialize<RunOptions>(
        Environment.GetEnvironmentVariable("LUCITEX_COMPARISON_OPTIONS") ?? "{}")!;

    public static (RunOptions Options, string[] BenchmarkArguments) Parse(string[] args)
    {
        var options = new RunOptions();
        var remaining = new List<string>();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value for {arg}.");
            options = arg switch {
                "--cases" => options with { Cases = Value().Split(',') },
                "--profiles" => options with { Profiles = Value().Split(',') },
                "--libraries" => options with { Libraries = Value().Split(',').Select(v => v == "Magick.NET" ? "MagickNet" : v).ToArray() },
                "--suite" => options with { Suite = Value() },
                "--memory" => options with { Memory = Value() },
                "--dataset-manifest" => options with { DatasetManifest = Path.GetFullPath(Value()) },
                "--output" => options with { Artifacts = Path.GetFullPath(Value()) },
                "--verify" => options with { VerifyOnly = true },
                "--quick" => options with { Quick = true },
                _ => options,
            };
            if (arg is not ("--cases" or "--profiles" or "--libraries" or "--suite" or "--memory" or "--dataset-manifest" or "--output" or "--verify" or "--quick")) {
                remaining.Add(arg);
            }
        }
        if (options.Profiles is ["all"]) {
            options = options with { Profiles = FormatCatalog.Profiles.ToArray() };
        }
        if (options.Suite is not ("all" or "convert" or "kernels") && !FormatCatalog.Modules.Any(m => m.Id == options.Suite)
            || options.Memory is not ("managed" or "process" or "etw")) {
            throw new ArgumentException("Suites: all/convert/jpeg/png/exr/ktx2/webp/kernels. Memory modes: managed/process/etw.");
        }
        if (!options.Libraries.Contains("Lucitex")) {
            throw new ArgumentException("Include Lucitex as the BenchmarkDotNet baseline.");
        }
        if (options.Cases.Length == 0 || options.Cases.Any(string.IsNullOrWhiteSpace)
            || options.Profiles.Length == 0 || options.Profiles.Any(p => !Codecs.ComparisonCase.Profiles.Contains(p))
            || options.Libraries.Any(l => !Enum.GetNames<Codecs.Library>().Contains(l))) {
            throw new ArgumentException("Invalid case, profile or library. Use --help for supported values.");
        }
        if (FormatCatalog.Modules.Any(m => m.Id == options.Suite) && !options.Profiles.Intersect(FormatCatalog.Get(options.Suite).Profiles).Any()) {
            throw new ArgumentException("The requested suite has no matching profiles.");
        }
        return (options, remaining.ToArray());
    }
}
