using System.Text.Json;
using Lucitex.Benchmarks.Formats;

namespace Lucitex.Benchmarks;

internal sealed record RunOptions
{
    public string[] Cases { get; init; } = ["ramp@128x128", "checker@129x97", "noise@2048x2048"];
    public string[] Profiles { get; init; } = ["jpeg-default420", "png-default", "exr-half-zip", "ktx2-rgba8-none", "webp-lossless"];
    public string[] Libraries { get; init; } = ["Lucitex", "ImageSharp", "SkiaSharp", "NetVips", "MagickNet"];
    public Suite Suites { get; init; } = Suite.All;
    public bool ComparableOnly { get; init; }
    public string Memory { get; init; } = "process";
    public string? DatasetManifest { get; init; }
    public string Artifacts { get; init; } = Path.GetFullPath("artifacts/comparisons/" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
    public bool VerifyOnly { get; init; }
    public bool Quick { get; init; }
    public static RunOptions Current => JsonSerializer.Deserialize<RunOptions>(
        Environment.GetEnvironmentVariable("LUCITEX_COMPARISON_OPTIONS") ?? "{}")!;

    public bool IncludesConvert() => Suites.HasFlag(Suite.Convert);
    public bool IncludesKernels() => Suites.HasFlag(Suite.Kernels);
    public bool IncludesFormat(string id) => Suites.HasFlag(ToSuite(id)) && PassesComparableFilter(id, id);
    public bool IncludesConvertRoute(string source, string destination) => Suites.HasFlag(Suite.Convert) && PassesComparableFilter(source, destination);
    private bool PassesComparableFilter(string source, string destination) => !ComparableOnly || Codecs.CodecCapabilities.IsComparable(source, destination);

    internal static Suite ToSuite(string formatId) => Enum.Parse<Suite>(formatId, ignoreCase: true);

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
                "--suite" => options with { Suites = ParseSuites(Value()) },
                "--memory" => options with { Memory = Value() },
                "--dataset-manifest" => options with { DatasetManifest = Path.GetFullPath(Value()) },
                "--output" => options with { Artifacts = Path.GetFullPath(Value()) },
                "--verify" => options with { VerifyOnly = true },
                "--quick" => options with { Quick = true },
                "--comparable" => options with { ComparableOnly = true },
                _ => options,
            };
            if (arg is not ("--cases" or "--profiles" or "--libraries" or "--suite" or "--memory" or "--dataset-manifest" or "--output" or "--verify" or "--quick" or "--comparable")) {
                remaining.Add(arg);
            }
        }
        if (options.Profiles is ["all"]) {
            options = options with { Profiles = FormatCatalog.Profiles.ToArray() };
        }
        if (options.Memory is not ("managed" or "process" or "etw")) {
            throw new ArgumentException("Memory modes: managed/process/etw.");
        }
        if (!options.Libraries.Contains("Lucitex")) {
            throw new ArgumentException("Include Lucitex as the BenchmarkDotNet baseline.");
        }
        if (options.Cases.Length == 0 || options.Cases.Any(string.IsNullOrWhiteSpace)
            || options.Profiles.Length == 0 || options.Profiles.Any(p => !Codecs.ComparisonCase.Profiles.Contains(p))
            || options.Libraries.Any(l => !Enum.GetNames<Codecs.Library>().Contains(l))) {
            throw new ArgumentException("Invalid case, profile or library. Use --help for supported values.");
        }
        if (!options.Suites.HasFlag(Suite.All)) {
            foreach (var module in FormatCatalog.Modules.Where(m => options.Suites.HasFlag(ToSuite(m.Id)))) {
                if (!options.Profiles.Intersect(module.Profiles).Any()) {
                    throw new ArgumentException($"Suite '{module.Id}' has no matching profiles.");
                }
            }
        }
        return (options, remaining.ToArray());
    }

    private static Suite ParseSuites(string value)
    {
        try {
            return Enum.Parse<Suite>(value, ignoreCase: true);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException) {
            throw new ArgumentException("Suites: comma-separated all/core/convert/jpeg/png/exr/ktx2/webp/kernels.", exception);
        }
    }
}
