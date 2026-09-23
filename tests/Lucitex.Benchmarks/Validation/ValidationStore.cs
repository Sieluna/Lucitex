using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Validators;
using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Measurement;

namespace Lucitex.Benchmarks.Validation;

internal static class ValidationStore
{
    private static readonly Dictionary<string, ValidationResult> s_Results = new();
    private static readonly Dictionary<string, string> s_Errors = new();
    private static readonly Dictionary<string, string> s_Skipped = new();
    private static readonly Dictionary<string, string> s_FileHashes = new();
    public static IReadOnlyDictionary<string, ValidationResult> Results => s_Results;
    public static bool HasErrors => s_Errors.Count > 0;

    public static void Skip(ComparisonCase comparison, string reason) => s_Skipped[comparison.Key] = reason;

    public static void CheckWorkerResult(ValidationResult actual)
    {
        var path = Path.Combine(RunOptions.Current.Artifacts, "validation.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var expected = document.RootElement.GetProperty("Results").EnumerateArray()
            .Single(r => r.GetProperty("Case").GetProperty("Key").GetString() == actual.Case.Key);
        if (expected.GetProperty("InputSha256").GetString() != actual.InputSha256
            || expected.GetProperty("EncodedInputSha256").GetString() != actual.EncodedInputSha256
            || expected.GetProperty("ResultSha256").GetString() != actual.ResultSha256) {
            throw new InvalidDataException("Benchmark worker data/output differs from host verification.");
        }
    }

    public static ValidationResult Validate(ComparisonCase comparison)
    {
        if (s_Results.TryGetValue(comparison.Key, out var existing)) {
            return existing;
        }
        CodecSession? session = null;
        try {
            session = new CodecSession(comparison);
            var result = AccuracyCheck.Run(session);
            if (RunOptions.Current.Memory == "process") {
                result = result with { ProcessMemory = ProcessMemoryProbe.Measure(comparison) };
            }
            s_Results.Add(comparison.Key, result);
            Save();
            return result;
        }
        catch (Exception exception) {
            s_Errors[comparison.Key] = exception.ToString();
            if (session is not null) {
                var directory = Path.Combine(RunOptions.Current.Artifacts, "failed-inputs");
                Directory.CreateDirectory(directory);
                var hash = Convert.ToHexString(SHA256.HashData(session.DecodeInput));
                File.WriteAllBytes(Path.Combine(directory, hash + "." + comparison.InputFormat), session.DecodeInput);
                s_Errors[comparison.Key] += $"\nCommon decode input: failed-inputs/{hash}.{comparison.InputFormat}";
            }
            Save();
            throw;
        }
    }

    public static void Save()
    {
        var options = RunOptions.Current;
        Directory.CreateDirectory(options.Artifacts);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && (a.GetName().Name!.StartsWith("Lucitex", StringComparison.Ordinal)
                || a.GetName().Name!.Contains("ImageSharp", StringComparison.Ordinal)
                || a.GetName().Name!.Contains("SkiaSharp", StringComparison.Ordinal)
                || a.GetName().Name!.Contains("NetVips", StringComparison.Ordinal)
                || a.GetName().Name!.Contains("Magick.NET", StringComparison.Ordinal)))
            .Select(a => new {
                Name = a.GetName().Name,
                Version = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? a.GetName().Version?.ToString(),
                Sha256 = HashFile(a.Location),
            }).ToArray();
        using var process = Process.GetCurrentProcess();
        var nativeModules = process.Modules.Cast<ProcessModule>()
            .Where(m => m.ModuleName.Contains("vips", StringComparison.OrdinalIgnoreCase)
                || m.ModuleName.Contains("Magick", StringComparison.OrdinalIgnoreCase)
                || m.ModuleName.Contains("libSkiaSharp", StringComparison.OrdinalIgnoreCase))
            .Select(m => new {
                Name = m.ModuleName, Version = m.FileVersionInfo.FileVersion,
                Sha256 = HashFile(m.FileName),
            }).ToArray();
        var metadata = new {
            Options = options, Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Processors = Environment.ProcessorCount,
            GitCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            Assemblies = assemblies, NativeModules = nativeModules, Results = s_Results.Values, Errors = s_Errors, Skipped = s_Skipped,
            NetVipsCache = "Operation cache disabled (Cache.Max = 0); all lazy results materialized inside timing.",
            ExrIo = "Magick.NET's EXR byte-array API uses temporary files internally; this native implementation cost is included. Lucitex uses MemoryStream. EXR results are end-to-end API comparisons, not disk-free codec kernel comparisons.",
            NativeMemoryStatus = options.Memory == "etw" ? "ETW heap allocation profiling requested; inspect BDN metrics and ETL."
                : "Native allocation bytes unavailable. Process mode measures managed+native process memory; never interpret missing native values as zero.",
            Contract = "RGB8 JPEG Q90, RGBA8 PNG/KTX2 or EXR HALF RGBA with normalized byte/255 samples. EXR tests preserve numeric values without transfer-function conversion; not full HDR fidelity. KTX2 is one 2D RGBA8 UNORM mip, None/Zlib, not GPU block encoding. Fresh handles; adaptation and output copies included; corpus construction excluded. Decode uses identical bytes. Default profiles allow different compression effort; no equal-CPU-budget claim.",
        };
        File.WriteAllText(Path.Combine(options.Artifacts, "validation.json"), JsonSerializer.Serialize(metadata,
            new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
    }

    private static string HashFile(string path)
    {
        if (!s_FileHashes.TryGetValue(path, out var hash)) {
            using var stream = File.OpenRead(path);
            hash = Convert.ToHexString(SHA256.HashData(stream));
            s_FileHashes.Add(path, hash);
        }
        return hash;
    }
}

internal sealed class AccuracyValidator : IValidator
{
    public bool TreatsWarningsAsErrors => true;

    public IEnumerable<ValidationError> Validate(ValidationParameters parameters)
    {
        foreach (var benchmark in parameters.Benchmarks) {
            if (ComparisonCase.From(benchmark) is not { } comparison) {
                continue;
            }
            ValidationError? error = null;
            try {
                ValidationStore.Validate(comparison);
            }
            catch (Exception exception) {
                error = new ValidationError(true, $"{comparison.Key}: {exception.Message}", benchmark);
            }
            if (error is not null) {
                yield return error;
            }
        }
        ValidationStore.Save();
    }
}
