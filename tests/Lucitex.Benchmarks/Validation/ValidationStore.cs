using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public static IReadOnlyDictionary<string, string> Errors => s_Errors;
    public static IReadOnlyDictionary<string, string> Skipped => s_Skipped;
    public static bool HasErrors => s_Errors.Count > 0;
    public static bool HasAcceptanceFailures => s_Results.Values.Any(r => r.AcceptanceFailed);
    private static Guid s_RunId = Guid.NewGuid();
    private static DateTimeOffset s_StartedAt = DateTimeOffset.UtcNow;
    private static readonly Stopwatch s_Checkpoint = Stopwatch.StartNew();

    internal static void Reset()
    {
        s_Results.Clear(); s_Errors.Clear(); s_Skipped.Clear(); s_FileHashes.Clear();
        s_RunId = Guid.NewGuid(); s_StartedAt = DateTimeOffset.UtcNow; s_Checkpoint.Restart();
        CodecSession.ResetCalibration();
    }

    public static void Skip(ComparisonCase comparison, string reason) => s_Skipped[comparison.Key] = reason;

    private sealed record WorkerPlan(string CaseKey, bool Approved,
        string InputSha256, string EncodedInputSha256, string QualityInputSha256, string ReferenceEncodedSha256,
        string ResultSha256, int? SelectedQuality, string? Metric, double? Target, double? Tolerance, double? Actual);

    private static string PlanPath(ComparisonCase comparison) => Path.Combine(RunOptions.Current.Artifacts,
        "workloads", Hash(System.Text.Encoding.UTF8.GetBytes(comparison.Key)) + ".json");

    private static void Freeze(ValidationResult result, bool approved)
    {
        var path = PlanPath(result.Case);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new WorkerPlan(result.Case.Key,
            approved, result.InputSha256, result.EncodedInputSha256,
            result.QualityInputSha256, result.ReferenceEncodedSha256, result.ResultSha256,
            result.Selection?.Quality, result.Selection?.Metric, result.Selection?.Target, result.Selection?.Tolerance, result.Selection?.Actual)));
    }

    private static WorkerPlan ReadPlan(ComparisonCase comparison)
    {
        var plan = JsonSerializer.Deserialize<WorkerPlan>(File.ReadAllText(PlanPath(comparison)))
            ?? throw new InvalidDataException("Missing frozen workload plan.");
        if (!plan.Approved || plan.CaseKey != comparison.Key)
            throw new InvalidDataException("Unapproved or mismatched workload.");
        return plan;
    }

    private static string InputPath(string hash) => Path.Combine(RunOptions.Current.Artifacts, "fixtures", hash + ".bin");

    public static (byte[] Encoded, byte[] Reference, byte[] Canonical, PairingSelection? Pairing) ReadInputs(ComparisonCase comparison)
    {
        var plan = ReadPlan(comparison);
        if (plan.SelectedQuality is not null && (plan.Metric is null || plan.Target is null || plan.Tolerance is null || plan.Actual is null))
            throw new InvalidDataException("Frozen pairing evidence is incomplete.");
        byte[] Read(string hash) {
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid fixture hash.");
            var bytes = File.ReadAllBytes(InputPath(hash));
            if (Hash(bytes) != hash) throw new InvalidDataException("Frozen fixture hash mismatch.");
            return bytes;
        }
        return (Read(plan.EncodedInputSha256), Read(plan.ReferenceEncodedSha256), Read(plan.QualityInputSha256),
            plan.SelectedQuality is { } quality ? new(plan.Metric!, plan.Target!.Value, plan.Tolerance!.Value,
                plan.Actual!.Value, quality, [], 0, true) : null);
    }

    private static void SaveInputs(CodecSession session)
    {
        Directory.CreateDirectory(Path.Combine(RunOptions.Current.Artifacts, "fixtures"));
        foreach (var bytes in new[] { session.DecodeInput, session.ReferenceEncoded, session.QualitySource.Pixels }) {
            var path = InputPath(Hash(bytes));
            if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        }
    }

    public static void CheckWorker(CodecSession session)
    {
        var expected = ReadPlan(session.Case);
        session.VerifyInputIntegrity();
        var outputHash = Hash(session.Run());
        if (outputHash != Hash(session.Run())) throw new InvalidDataException("Worker output is not repeatable.");
        session.VerifyInputIntegrity();
        if (expected.InputSha256 != session.Image.Sha256
            || expected.EncodedInputSha256 != Hash(session.DecodeInput)
            || expected.QualityInputSha256 != session.QualitySource.Sha256
            || expected.ReferenceEncodedSha256 != Hash(session.ReferenceEncoded)
            || expected.ResultSha256 != outputHash
            || expected.SelectedQuality != session.Selection?.Quality
            || expected.Metric != session.Selection?.Metric || expected.Target != session.Selection?.Target
            || expected.Tolerance != session.Selection?.Tolerance || expected.Actual != session.Selection?.Actual)
            throw new InvalidDataException("Worker inputs, settings or output differ from the approved plan.");
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    public static bool TryValidate(ComparisonCase comparison)
    {
        if (comparison.UnsupportedReason() is { } reason) {
            Skip(comparison, reason);
            return false;
        }
        try {
            return Validate(comparison).EligibleForTiming;
        }
        catch { return false; } // Validate retains the error; required failures still fail the run.
    }

    private static ValidationResult Validate(ComparisonCase comparison)
    {
        if (s_Errors.TryGetValue(comparison.Key, out var priorError)) {
            throw new InvalidDataException(priorError);
        }
        if (s_Results.TryGetValue(comparison.Key, out var existing)) {
            existing.EnsurePassed();
            return existing;
        }
        CodecSession? session = null;
        try {
            session = new CodecSession(comparison);
            var result = AccuracyCheck.Run(session);
            s_Results.Add(comparison.Key, result);
            foreach (var peer in s_Results.Values.Where(r => r.Case.GroupKey == comparison.GroupKey)) {
                if (peer.InputSha256 != result.InputSha256 || peer.EncodedInputSha256 != result.EncodedInputSha256
                    || peer.QualityInputSha256 != result.QualityInputSha256 || peer.ReferenceEncodedSha256 != result.ReferenceEncodedSha256)
                    throw new InvalidDataException("Comparison group does not share identical source, input and reference fixtures.");
            }
            result = SelectSizeTarget(result);
            s_Results[comparison.Key] = result;
            result.EnsurePassed();
            SaveInputs(session);
            Freeze(result, approved: result.EligibleForTiming);
            Save(force: false);
            if (result.EligibleForTiming && !RunOptions.Current.VerifyOnly && RunOptions.Current.Memory == "process") {
                result = result with { ProcessMemory = ProcessMemoryProbe.Measure(comparison) };
            }
            s_Results[comparison.Key] = result;
            Save(force: false);
            return result;
        }
        catch (Exception exception) {
            s_Errors[comparison.Key] = exception.ToString();
            if (s_Results.TryGetValue(comparison.Key, out var rejected)) Freeze(rejected, approved: false);
            if (session is not null) {
                var directory = Path.Combine(RunOptions.Current.Artifacts, "failed-inputs");
                Directory.CreateDirectory(directory);
                var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(comparison.Key)));
                var stem = Path.Combine(directory, hash);
                File.WriteAllBytes(stem + ".input." + comparison.InputFormat, session.DecodeInput);
                File.WriteAllBytes(stem + ".source.raw", session.Image.Pixels);
                File.WriteAllBytes(stem + ".canonical.raw", session.QualitySource.Pixels);
                File.WriteAllBytes(stem + ".reference." + comparison.Format, session.ReferenceEncoded);
                try {
                    // Label this as a reproduction, not the original failing output.
                    File.WriteAllBytes(stem + ".reproduced-output", session.Run());
                }
                catch (Exception reproduction) { s_Errors[comparison.Key] += "\nReproduction: " + reproduction.Message; }
                File.WriteAllText(stem + ".json", JsonSerializer.Serialize(new {
                    Case = comparison, session.Image.Width, session.Image.Height, session.Image.Channels,
                    session.SelectedQuality, session.Selection, Error = s_Errors[comparison.Key],
                }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
                s_Errors[comparison.Key] += $"\nFailure artifacts: failed-inputs/{hash}.json";
            }
            Save();
            throw;
        }
    }

    private static ValidationResult SelectSizeTarget(ValidationResult result)
    {
        if (result.Case.Candidate != "default" || result.Case.Library != Library.Lucitex || result.Case.RateMatched || !result.PairingMatched
            || result.Compression is not { } compression) return result;
        var target = new CompressionSizeTarget(compression.ReferenceEncoder, compression.ReferenceBytes,
            result.CompressionReferenceSha256);
        if (result.CorrectnessPassed && compression.FidelityPassed
            && result.EncodedBytes is { } ownSize && ownSize < target.Bytes)
            target = new("Lucitex", ownSize, result.ResultSha256);
        foreach (var library in RunOptions.Current.Libraries.Select(Enum.Parse<Library>).Where(l => l != Library.Lucitex)) {
            var comparison = result.Case with { Library = library };
            if (!TryValidate(comparison)) continue;
            var peer = s_Results[comparison.Key];
            if (!peer.EligibleForTiming || !peer.PairingMatched || peer.EncodedBytes is not { } size) continue;
            if (!result.Case.RequiresPairing && peer.EncoderInputSha256 != result.EncoderInputSha256) continue;
            if (size < target.Bytes) target = new(comparison.Library.ToString(), size, peer.ResultSha256);
        }
        return result with { SizeTarget = target };
    }

    public static void Save(bool force = true)
    {
        if (!force && s_Checkpoint.Elapsed < TimeSpan.FromSeconds(5)) return;
        s_Checkpoint.Restart();
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
            SchemaVersion = 6, RunId = s_RunId, StartedAtUtc = s_StartedAt,
            Options = options, Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Processors = Environment.ProcessorCount,
            MeasurementContract = options.VerifyOnly ? "Verification only: correctness and quality checks; no timing or memory measurements."
                : options.Smoke ? "Smoke only: BenchmarkDotNet Dry job; pipeline checks, no performance conclusions."
                : "Full: adaptive BenchmarkDotNet measurement and exhaustive JPEG Q1..100 pairing calibration. Calibration is excluded from timing.",
            Assemblies = assemblies, NativeModules = nativeModules, Results = s_Results.Values, Errors = s_Errors, Skipped = s_Skipped,
            AcceptanceFailures = s_Results.Values.Where(r => r.AcceptanceFailed).Select(r => r.Case.Key).ToArray(),
            NetVipsCache = "Operation cache disabled (Cache.Max = 0); all lazy results materialized inside timing.",
            ExecutionPolicy = "Unrestricted library parallelism: no benchmark-imposed thread limits or CPU affinity. Each library may use all CPUs available to the process through its own scheduler. This compares end-to-end latency, not equal CPU consumption. Host OS and externally configured limits still apply.",
            CompressionContract = "Correctness alone controls timing eligibility. JPEG: target is common reference Q90 MSE ±5% (zero requires exact) or file bytes ±2%. Closest evaluated value wins; ties use smaller bytes for quality or lower MSE for rate, then lower Q. Both profiles share a Q1..100 scan. Unmatched points are timed separately and never labeled equal quality/rate. Only Lucitex quality/exact comparisons enforce <=1.2x the smallest matched selected peer or fixed reference; this independent acceptance check can fail the run but cannot remove timings. Fast is advisory. Rate comparisons measure distortion without a size gate. No perceptual-equivalence or global optimum claim.",
            FairnessContract = "Encode: identical source pixels. Decode: identical encoded bytes. Convert: identical encoded bytes, common canonical decoded pixels for end-to-end quality; native decoded encoder input may differ and its hash is separate. No minimum-error reference picking. All decoder checks retained; designated policy selected before measuring error. Tradeoff mode ranks frozen representatives by time and size; candidate size is advisory for all libraries. See selection.json and tradeoffs.json in the parent output directory.",
            ExrIo = "Magick.NET's EXR byte-array API uses temporary files internally; this native implementation cost is included. Lucitex uses MemoryStream. EXR results are end-to-end API comparisons, not disk-free codec kernel comparisons.",
            NativeMemoryStatus = "Native allocation bytes unavailable. Process mode measures managed+native process memory; never interpret missing native values as zero.",
            Contract = "RGB8 JPEG reference Q90 with matched quality or rate; RGBA8 PNG/WebP/KTX2 or EXR HALF RGBA with normalized byte/255 samples. EXR is not full HDR fidelity. KTX2 is one 2D RGBA8 UNORM mip, None/Zlib, not GPU block encoding. Fresh handles; adaptation and output copies included; corpus construction and quality calibration excluded. No equal-CPU-budget or perceptual-equivalence claim.",
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
