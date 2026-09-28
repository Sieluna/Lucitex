using System.Text.Json;
using System.Text.Json.Serialization;
using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Data;
using Lucitex.Benchmarks.Formats;
using Lucitex.Benchmarks.Reporting;

namespace Lucitex.Benchmarks;

internal sealed record RunOptions
{
    public string[] Cases { get; init; } = ["flat@256x256", "ramp@128x128", "checker@129x97", "alpha@256x256", "noise@2048x2048"];
    public string[] Profiles { get; init; } = ["jpeg-quality420", "jpeg-rate420", "png-default", "exr-half-zip", "ktx2-rgba8-none", "webp-lossless-fast", "webp-lossless", "webp-lossless-best"];
    public string[] Libraries { get; init; } = ["Lucitex", "ImageSharp", "SkiaSharp", "NetVips", "MagickNet"];
    public Suite Suites { get; init; } = Suite.All;
    public bool ComparableOnly { get; init; }
    public string Memory { get; init; } = "process";
    public string? DatasetManifest { get; init; }
    public string Artifacts { get; init; } = Path.GetFullPath("artifacts/comparisons/" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff"));
    public bool VerifyOnly { get; init; }
    public string Mode { get; init; } = "full";
    public bool Tradeoffs { get; init; } = true;
    public string[] CalibrationCases { get; init; } = ["ramp@193x127", "checker@191x131", "noise@257x193"];
    public TradeoffSelection[] TradeoffSelections { get; init; } = [];
    [JsonIgnore] public bool Smoke => Mode == "smoke";
    public static RunOptions Current => JsonSerializer.Deserialize<RunOptions>(
        Environment.GetEnvironmentVariable("LUCITEX_COMPARISON_OPTIONS") ?? "{}")!;

    public bool IncludesConvert() => Suites.HasFlag(Suite.Convert);
    public bool IncludesKernels() => Suites.HasFlag(Suite.Kernels);
    public bool IncludesFormat(string id) => Suites.HasFlag(ToSuite(id)) && PassesComparableFilter(id, id);
    public bool IncludesConvertRoute(string source, string destination) => Suites.HasFlag(Suite.Convert) && PassesComparableFilter(source, destination);
    private bool PassesComparableFilter(string source, string destination) => !ComparableOnly
        || Libraries.Select(Enum.Parse<Library>).Count(l => CodecCapabilities.Supports(l, source) && CodecCapabilities.Supports(l, destination)) >= 2;

    internal static Suite ToSuite(string formatId) => Enum.Parse<Suite>(formatId, ignoreCase: true);

    public static (RunOptions Options, string[] BenchmarkArguments) Parse(string[] args)
    {
        var options = new RunOptions();
        string[] remaining = [];
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (arg == "--") {
                remaining = args[(i + 1)..];
                break;
            }
            if (!supplied.Add(arg)) throw new ArgumentException($"Duplicate option: {arg}.");
            string Value() => ++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(args[i]) ? args[i] : throw new ArgumentException($"Missing value for {arg}.");
            string[] Values() => Value().Split(',', StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray();
            options = arg switch {
                "--cases" => options with { Cases = Values() },
                "--profiles" => options with { Profiles = Values() },
                "--libraries" => options with { Libraries = Values().Select(v => v == "Magick.NET" ? "MagickNet" : v).Distinct().ToArray() },
                "--suite" => options with { Suites = ParseSuites(Value()) },
                "--memory" => options with { Memory = Value() },
                "--mode" => options with { Mode = Value() },
                "--dataset-manifest" => options with { DatasetManifest = Path.GetFullPath(Value()) },
                "--output" => options with { Artifacts = Path.GetFullPath(Value()) },
                "--verify" => options with { VerifyOnly = true },
                "--comparison" => options with { Tradeoffs = false },
                "--calibration-cases" => options with { CalibrationCases = Values() },
                "--comparable" => options with { ComparableOnly = true },
                _ => throw new ArgumentException($"Unknown option '{arg}'. Use --help; BenchmarkDotNet arguments follow --."),
            };
        }
        if (options.VerifyOnly) options = options with { Tradeoffs = false };
        var allProfiles = options.Profiles is ["all"];
        if (allProfiles) {
            options = options with { Profiles = FormatCatalog.Profiles.ToArray() };
        }
        if (options.Tradeoffs) {
            if (remaining.Length > 0)
                throw new ArgumentException("Default rankings perform calibration and evaluation timing; use --comparison for BDN overrides.");
            if (supplied.Contains("--suite") && (options.Suites & (Suite.Core | Suite.Convert | Suite.Kernels)) != 0
                && options.Suites != Suite.All)
                throw new ArgumentException("Rankings support encoding suites only; use --comparison for core, convert or kernels.");
            if (!allProfiles && supplied.Contains("--profiles") && options.Profiles.Contains("jpeg-rate420"))
                throw new ArgumentException("Tradeoff ranking requires equal quality; use jpeg-quality420 instead of jpeg-rate420.");
            options = options with { Profiles = options.Profiles.Where(p => p != "jpeg-rate420")
                .Select(p => p is "webp-lossless-fast" or "webp-lossless-best" ? "webp-lossless" : p).Distinct().ToArray() };
            if (options.Cases.Intersect(options.CalibrationCases).Any())
                throw new ArgumentException("Calibration and evaluation case IDs must be disjoint.");
        }
        else if (supplied.Contains("--calibration-cases"))
            throw new ArgumentException("--calibration-cases is only used by the default rankings; omit --comparison and --verify.");
        if (options.Memory is not ("managed" or "process")) {
            throw new ArgumentException("Memory modes: managed/process.");
        }
        if (options.Mode is not ("full" or "smoke"))
            throw new ArgumentException("--mode must be full/smoke.");
        if (options.VerifyOnly && (options.Smoke || remaining.Length != 0))
            throw new ArgumentException("--verify does not accept smoke mode or BenchmarkDotNet arguments.");
        if (options.VerifyOnly && supplied.Contains("--memory"))
            throw new ArgumentException("--verify does not measure memory; omit --memory.");
        if (options.Cases.Length == 0 || options.Cases.Any(string.IsNullOrWhiteSpace)
            || options.Profiles.Length == 0 || options.Profiles.Any(p => !FormatCatalog.Profiles.Contains(p))
            || options.Libraries.Length == 0 || options.Libraries.Any(l => !Enum.GetNames<Library>().Contains(l))) {
            throw new ArgumentException("Invalid case, profile or library. Use --help for supported values.");
        }
        foreach (var id in options.Cases.Concat(options.Tradeoffs ? options.CalibrationCases : [])) {
            if (id.StartsWith("external:", StringComparison.Ordinal)) {
                if (string.IsNullOrWhiteSpace(id[9..]) || options.DatasetManifest is null || !File.Exists(options.DatasetManifest))
                    throw new ArgumentException("External cases require an ID and an existing --dataset-manifest.");
            }
            else if (id.Split('@')[0] is not ("ramp" or "checker" or "impulse" or "stripes" or "chroma" or "flat" or "alpha" or "noise")
                || TestImage.DeclaredDimensions(id) is not { Width: >= 1 and <= 8192, Height: >= 1 and <= 8192 })
                throw new ArgumentException($"Invalid case '{id}'; use pattern@widthxheight with dimensions 1..8192.");
        }
        if (options.DatasetManifest is not null && !options.Cases.Concat(options.Tradeoffs ? options.CalibrationCases : []).Any(c => c.StartsWith("external:", StringComparison.Ordinal)))
            throw new ArgumentException("--dataset-manifest requires at least one external:ID case.");
        if (!options.Suites.HasFlag(Suite.All)) {
            foreach (var module in FormatCatalog.Modules.Where(m => options.Suites.HasFlag(ToSuite(m.Id)))) {
                if (!options.Profiles.Intersect(module.Profiles).Any()) {
                    throw new ArgumentException($"Suite '{module.Id}' has no matching profiles.");
                }
            }
        }
        if (options.Tradeoffs) options = options with {
            Profiles = options.Profiles.Where(p => options.IncludesFormat(FormatCatalog.ForProfile(p).Id)).ToArray(),
        };
        if (!FormatCatalog.SelectBenchmarks(options).Any())
            throw new ArgumentException("No benchmarks match the selected suites, profiles and libraries.");
        return (options, remaining);
    }

    private static Suite ParseSuites(string value)
    {
        var result = (Suite)0;
        foreach (var name in value.Split(',', StringSplitOptions.TrimEntries)) {
            if (!Enum.GetNames<Suite>().Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Suites: comma-separated all/core/convert/jpeg/png/exr/ktx2/webp/kernels.");
            result |= Enum.Parse<Suite>(name, ignoreCase: true);
        }
        return result;
    }
}
