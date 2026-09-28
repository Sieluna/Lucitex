using System.Globalization;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Validation;

namespace Lucitex.Benchmarks.Reporting;

internal sealed class ComparisonColumn(string name, Func<ValidationResult, string> value, string legend) : IColumn
{
    public string Id => name;
    public string ColumnName => name;
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 0;
    public bool IsNumeric => false;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => legend;
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    public bool IsAvailable(Summary summary) => true;
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
        Find(benchmarkCase) is { } result ? value(result) : "N/A";

    public static ValidationResult? Find(BenchmarkCase benchmark) =>
        ComparisonCase.From(benchmark) is { } comparison && ValidationStore.Results.TryGetValue(comparison.Key, out var result) ? result : null;

    public static IColumn[] Create() => [
        new ComparisonColumn("Correctness", r => r.CorrectnessPassed ? "PASS" : "FAIL", "Structure, repeatability, input preservation and predeclared independent decoder policy."),
        new ComparisonColumn("Decoder policy", r => r.DecoderPolicy, "Compare only matching declared reconstruction policies."),
        new ComparisonColumn("Encoder settings", r => r.EncoderSettings, "Library-specific settings; shared profile names do not imply equal encoder effort. Setup, FFI, adaptation and result materialization are timed."),
        new ComparisonColumn("Quality policy", r => r.Compression is null ? "Decode" : r.Compression.Lossless ? "Lossless" : r.Selection!.Metric + (r.PairingMatched ? " matched" : " UNMATCHED"), "Decode, exact lossless samples, or a matched/unmatched MSE or file-size target."),
        new ComparisonColumn("Selected Q", r => r.Case.Format == "jpeg" && r.Case.Operation != Operation.Decode ? (r.Selection?.Quality ?? r.Case.EncoderQuality).ToString(CultureInfo.InvariantCulture) : "N/A", "Frozen encoder quality selected before timing. All integer settings Q1..100 are evaluated. Every evaluated candidate is retained."),
        new ComparisonColumn("Pairing", r => r.Selection is null ? "N/A" : r.PairingMatched ? "MATCHED" : "UNMATCHED", "Unmatched points are timed in a separate diagnostic group; they are not equal-quality/rate comparisons."),
        new ComparisonColumn("Size acceptance", r => !r.EnforceSize || !r.PairingMatched ? "N/A" : r.SizeTargetPassed ? "PASS" : "FAIL", "Independent Lucitex acceptance check; failures remain timed and make the run fail."),
        new ComparisonColumn("Target", r => r.Selection?.Target.ToString("F4", CultureInfo.InvariantCulture) ?? "N/A", "Reference Q90 MSE or complete file bytes, according to the quality policy."),
        new ComparisonColumn("Actual", r => r.Selection?.Actual.ToString("F4", CultureInfo.InvariantCulture) ?? "N/A", "Measured value at the selected encoder setting."),
        new ComparisonColumn("Delta %", r => r.Selection?.DeviationPercent?.ToString("F2", CultureInfo.InvariantCulture) ?? "N/A", "Signed deviation from target; MSE tolerance 5%, byte tolerance 2%. Zero MSE requires exactness."),
        new ComparisonColumn("Size/ref", r => r.Compression?.SizeRatio.ToString("F3", CultureInfo.InvariantCulture) ?? "N/A", "Size against the fixed reference. Only Lucitex has a size gate; other libraries remain eligible regardless of size."),
        new ComparisonColumn("Size/best", r => r.BestSizeRatio?.ToString("F3", CultureInfo.InvariantCulture) ?? "N/A", "Lucitex size / smallest qualifying comparable output among selected libraries and fixed reference. Non-calibrated conversions require identical actual encoder inputs; not a global optimum."),
        new ComparisonColumn("Size policy", r => r.SizePolicy, "Lucitex limit 1.2, Fast advisory; competing libraries have no size gate and remain in the comparison."),
        new ComparisonColumn("Size/common", r => r.CommonSizeRatio?.ToString("F3", CultureInfo.InvariantCulture) ?? "N/A", "Size against the common canonical reference. Diagnostic when input decoder pixels differ; see Size/ref for the efficiency gate."),
        new ComparisonColumn("Raw/encoded", r => r.Compression?.RawToEncodedRatio.ToString("F3", CultureInfo.InvariantCulture) ?? "N/A", "Raw sample bytes / encoded bytes; larger means better compression."),
        new ComparisonColumn("Fidelity", r => !r.FidelityPassed ? "FAIL" : r.Compression is null ? "N/A" : "PASS", "Correctness and fidelity apply to all libraries; size only gates Lucitex except Fast. See Size policy."),
        new ComparisonColumn("Encoded B", r => r.EncodedBytes?.ToString(CultureInfo.InvariantCulture) ?? "N/A", "Exact encoded byte count, measured outside timing; not a quality-equivalent ranking."),
        new ComparisonColumn("PSNR dB", r => r.SourceQuality.PsnrDb?.ToString("F2", CultureInfo.InvariantCulture) ?? "Exact", "Pixel error against the analytic source after independent decoding; larger is better."),
        new ComparisonColumn("Workload MSE", r => r.WorkloadQuality.Mse.ToString("F4", CultureInfo.InvariantCulture), "Against common source pixels, or common canonical decoded pixels for conversion. Separate from original-source PSNR."),
        new ComparisonColumn("Ref MSE", r => r.ReferenceAgreement.Mse.ToString("F4", CultureInfo.InvariantCulture), "Agreement with a reference fixed before measuring error; all decoder checks are retained in validation.json."),
        new ComparisonColumn("Ref decoder", r => r.ReferenceDecoder, "Predeclared decoder policy, never the minimum observed error. Nearest for ImageSharp and Lucitex widths <=4; interpolated references for other JPEG paths."),
        new ComparisonColumn("All threads B", r => r.ProcessMemory?.ManagedBytesPerOperation.ToString(CultureInfo.InvariantCulture) ?? "N/A", "Separate isolated workload GC.GetTotalAllocatedBytes measurement, including worker threads."),
        new ComparisonColumn("Process peak MiB", r => r.ProcessMemory is { } p ? (p.SampledPeakPrivateBytes / 1048576d).ToString("F2", CultureInfo.InvariantCulture) : "N/A", "Isolated warmed process: Windows private commit / Linux private RSS. Includes runtime, pools, managed and native memory. Sampled lower bound, not native allocated B/op."),
    ];
}
