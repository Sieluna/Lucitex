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
        new ComparisonColumn("Encoded B", r => r.EncodedBytes?.ToString(CultureInfo.InvariantCulture) ?? "N/A", "Exact encoded byte count, measured outside timing; not a quality-equivalent ranking."),
        new ComparisonColumn("PSNR dB", r => r.SourceQuality.PsnrDb?.ToString("F2", CultureInfo.InvariantCulture) ?? "Exact", "Pixel error against the analytic source after independent decoding; larger is better."),
        new ComparisonColumn("Ref MSE", r => r.ReferenceAgreement.Mse.ToString("F4", CultureInfo.InvariantCulture), "Independent reference agreement: lossless normalized samples must be exact; JPEG MSE <= 4 across allowed chroma policies. See Ref decoder."),
        new ComparisonColumn("Ref decoder", r => r.ReferenceDecoder, "Independent decoder producing the closest pixels, recorded explicitly because JPEG 4:2:0 upsampling policies differ."),
        new ComparisonColumn("All threads B", r => r.ProcessMemory?.ManagedBytesPerOperation.ToString(CultureInfo.InvariantCulture) ?? "N/A", "Separate isolated workload GC.GetTotalAllocatedBytes measurement, including worker threads."),
        new ComparisonColumn("Process peak MiB", r => r.ProcessMemory is { } p ? (p.SampledPeakPrivateBytes / 1048576d).ToString("F2", CultureInfo.InvariantCulture) : "N/A", "Isolated warmed process: Windows private commit / Linux private RSS. Includes runtime, pools, managed and native memory. Sampled lower bound, not native allocated B/op."),
    ];
}
