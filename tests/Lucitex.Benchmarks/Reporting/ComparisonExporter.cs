using System.Globalization;
using System.Net;
using System.Text;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Validation;

namespace Lucitex.Benchmarks.Reporting;

internal sealed class ComparisonExporter : IExporter
{
    public string Name => nameof(ComparisonExporter);
    public void ExportToLog(Summary summary, ILogger logger) { }

    public IEnumerable<string> ExportToFiles(Summary summary, ILogger logger)
    {
        var title = summary.BenchmarksCases.Length > 0 ? summary.BenchmarksCases[0].Descriptor.Type.Name : summary.Title;
        var html = Header(title);
        html.Append("<nav><a href='../index.html'>All results</a> · <a href='../validation.json'>Validation data</a></nav>");
        if (RunOptions.Current.Smoke) html.Append("<p class='warning'>SMOKE RUN: pipeline verification only; no performance conclusions.</p>");
        var markdown = new StringBuilder("## ").AppendLine(title).AppendLine();
        html.Append(PerformanceTables(summary, markdown)).Append(Legend).Append("</body></html>");
        var path = Path.Combine(summary.ResultsDirectoryPath, title + "-comparison.html");
        File.WriteAllText(path, html.ToString());
        var markdownPath = Path.ChangeExtension(path, ".md");
        File.WriteAllText(markdownPath, markdown.ToString());
        return [path, markdownPath];
    }

    private static string PerformanceTables(Summary summary, StringBuilder? markdown = null)
    {
        var html = new StringBuilder();
        var columns = summary.Table.Columns.Where(c => c.NeedToShow).ToArray();
        var parameterNames = summary.BenchmarksCases.SelectMany(b => b.Parameters.Items).Select(p => p.Name).ToHashSet();
        var summaryColumns = columns.Where(c => parameterNames.Contains(c.Header)
            || c.Header is "Method" or "Job" or "Runtime" or "Mean" or "Error" or "Allocated"
                or "Process peak MiB" or "Encoded B" or "Raw/encoded" or "Workload MSE" or "Delta %"
                or "Decoder policy" or "Pairing" or "Size acceptance" or "Encoder settings" or "All threads B").ToArray();
        foreach (var group in summary.BenchmarksCases.Select((benchmark, index) => (benchmark, index))
            .GroupBy(item => MeasurementGroup(ComparisonColumn.Find(item.benchmark)))) {
            var visible = summaryColumns.Where(c => group.Any(item => summary.Table.FullContent[item.index][c.Index] != "N/A")).ToArray();
            markdown?.Append("### ").AppendLine(group.Key).AppendLine()
                .Append("| ").Append(string.Join(" | ", visible.Select(c => Markdown(c.Header)))).AppendLine(" |")
                .Append("| ").Append(string.Join(" | ", visible.Select(_ => "---"))).AppendLine(" |");
            html.Append("<h3>").Append(Escape(group.Key)).Append("</h3><div class='scroll'><table><thead><tr>");
            foreach (var column in visible) html.Append("<th>").Append(Escape(column.Header)).Append("</th>");
            html.Append("<th>Details</th></tr></thead><tbody>");
            foreach (var (benchmark, index) in group) {
                html.Append("<tr>");
                foreach (var column in visible)
                    html.Append("<td>").Append(Escape(summary.Table.FullContent[index][column.Index])).Append("</td>");
                html.Append("<td><details><summary>Measurements</summary><dl>");
                if (summary[benchmark]?.ResultStatistics is null)
                    html.Append("<dt>Timing</dt><dd>No measurement; inspect the BenchmarkDotNet log.</dd>");
                foreach (var column in columns.Except(visible))
                    html.Append("<dt>").Append(Escape(column.Header)).Append("</dt><dd>")
                        .Append(Escape(summary.Table.FullContent[index][column.Index])).Append("</dd>");
                html.Append("</dl></details></td></tr>");
                markdown?.Append("| ").Append(string.Join(" | ", visible.Select(c => Markdown(summary.Table.FullContent[index][c.Index])))).AppendLine(" |");
            }
            html.Append("</tbody></table></div>");
            markdown?.AppendLine();
        }
        return html.ToString();
    }

    internal static string MeasurementGroup(ValidationResult? result)
    {
        if (result is null) return "Performance";
        var label = result switch {
            { Case.Operation: Operation.Decode } => "Decode · identical input",
            { PairingMatched: false } => "Unmatched target · diagnostic performance only",
            { Case.RateMatched: true } => "File size matched within 2% · compare quality and performance",
            { Selection: not null } => "MSE matched within 5% · compare size and performance",
            _ => "Lossless · compare size and performance",
        };
        return label + " · " + result.DecoderPolicy;
    }

    public static void WriteIndex(RunOptions options, IEnumerable<Summary>? summaries = null)
    {
        var reports = summaries?.ToArray() ?? [];
        var measured = reports.Sum(s => s.Reports.Count(r => r.Success && r.ResultStatistics is not null));
        var unmatched = ValidationStore.Results.Values.Count(r => !r.PairingMatched);
        var acceptanceFailures = ValidationStore.Results.Values.Count(r => r.AcceptanceFailed);
        var html = Header("Lucitex benchmark results");
        html.Append($"<p>{measured} measured workloads · {ValidationStore.Results.Count} codec checks · {ValidationStore.Errors.Count} correctness issues · {acceptanceFailures} size acceptance failures · {unmatched} unmatched · {ValidationStore.Skipped.Count} unsupported</p>");
        html.Append("<nav><a href='#measurements'>Measurements</a> · <a href='#diagnostics'>Diagnostics</a> · <a href='validation.json'>Validation JSON</a></nav>");
        if (options.Smoke) html.Append("<p class='warning'>SMOKE RUN: pipeline verification only; no performance conclusions.</p>");
        if (options.VerifyOnly) html.Append("<p>Verification only: encoding and reconstruction data below; no timing or memory measurements.</p>");
        if (options.Tradeoffs) html.Append("<p>Tradeoff phase: ").Append(options.TradeoffSelections.Length == 0 ? "calibration" : "evaluation")
            .Append(". Candidate sizes are advisory for every library. <a href='../index.html'>Three-objective rankings and frozen selection policy</a>.</p>");
        html.Append("<section id='measurements'>");
        foreach (var report in reports) {
            var title = report.BenchmarksCases.Length > 0 ? report.BenchmarksCases[0].Descriptor.Type.Name : report.Title;
            var relative = Path.GetRelativePath(options.Artifacts, Path.Combine(report.ResultsDirectoryPath, title + "-comparison.html")).Replace('\\', '/');
            html.Append("<h2>").Append(Escape(title)).Append("</h2><p><a href='").Append(Escape(relative))
                .Append("'>Standalone report</a></p>").Append(PerformanceTables(report));
        }
        if (!options.VerifyOnly && measured == 0) html.Append("<p>No timing measurements were produced. Check diagnostics and the BenchmarkDotNet log.</p>");
        html.Append("</section>").Append(Legend);
        html.Append("<details><summary>Inputs and comparison method</summary>");
        html.Append("<p>Encode uses identical pixels; decode uses identical encoded bytes. Conversion uses identical encoded input and compares end-to-end quality against common canonical decoded pixels. Lossless preservation is checked against each actual encoder input.</p>");
        html.Append("<p>JPEG scans integer Q1-100 against a common Q90 reference: MSE within 5% (exact at zero), or file bytes within 2%. The closest point is chosen. Unmatched points are timed in a separate diagnostic group. Calibration is excluded from measured time. Lucitex's 1.2x size acceptance check can fail the run but does not exclude timings; WebP Fast is advisory. Encoder effort is library-specific, not an equal CPU budget.</p>");
        html.Append("<p>Parallelism is unrestricted: no benchmark-imposed thread limits or CPU affinity. Libraries may use all CPUs available to the process with their own schedulers. EXR uses normalized byte/HALF samples, not full HDR fidelity; KTX2 uses one RGBA8 level, not GPU block encoding. Versions, hashes and decoder checks are retained in validation.json.</p></details>");
        var keys = ValidationStore.Results.Keys.Concat(ValidationStore.Errors.Keys).Concat(ValidationStore.Skipped.Keys)
            .Distinct().OrderByDescending(k => ValidationStore.Errors.ContainsKey(k))
            .ThenByDescending(k => ValidationStore.Results.TryGetValue(k, out var r) && !r.PairingMatched).ThenBy(k => k).ToArray();
        html.Append("<details id='diagnostics'").Append(options.VerifyOnly || measured == 0 ? " open" : "")
            .Append("><summary>Preflight diagnostics and encoding data (").Append(keys.Length).Append(" workloads)</summary>");
        html.Append("<p>Preflight validation does not mean a workload was timed: benchmark filters can exclude it. Pairing and size acceptance do not suppress correct workloads.</p>");
        html.Append("<div class='scroll'><table><thead><tr><th>Workload</th><th>Preflight</th><th>Encoded B</th><th>Raw/encoded</th><th>Workload MSE</th><th>Pairing target</th><th>Delta %</th><th>Details</th></tr></thead><tbody>");
        foreach (var key in keys) {
            ValidationStore.Results.TryGetValue(key, out var result);
            ValidationStore.Errors.TryGetValue(key, out var error);
            ValidationStore.Skipped.TryGetValue(key, out var skipped);
            var state = error is not null ? "Issue" : skipped is not null ? "Unsupported"
                : result?.AcceptanceFailed == true ? "Size acceptance failed; timing eligible"
                : result?.PairingMatched == false ? "Unmatched; timing eligible" : result?.EligibleForTiming == true ? "Validated" : "Incomplete";
            var cells = new[] { key, state, result?.EncodedBytes?.ToString(CultureInfo.InvariantCulture) ?? "N/A",
                Number(result?.Compression?.RawToEncodedRatio), Number(result?.WorkloadQuality.Mse),
                result?.Selection is { } pairing ? $"{pairing.Metric} {Number(pairing.Target)}" : "N/A", Number(result?.Selection?.DeviationPercent) };
            html.Append("<tr>");
            foreach (var cell in cells) html.Append("<td>").Append(Escape(cell)).Append("</td>");
            html.Append("<td><details><summary>Evidence</summary><pre>")
                .Append(Escape(error ?? skipped ?? result?.QualityContract ?? "No result"));
            if (result is not null) {
                html.Append("\n").Append(Escape(result.ComparisonContract)).Append("\n").Append(Escape(result.CompressionBasis));
                if (result.SizeTarget is { } target)
                    html.Append("\n").Append(Escape($"Size reference: {target.Encoder}, {target.Bytes} B; output/reference={Number(result.BestSizeRatio)}. {result.SizePolicy}."));
                foreach (var check in result.DecoderChecks)
                    html.Append("\n").Append(Escape($"{check.Stage}/{check.Decoder}: {check.Policy}; required={check.Required}; MSE={check.Agreement?.Mse}; tolerance={check.Tolerance}; {check.Error}"));
                if (result.Selection is { } selection)
                    html.Append("\n").Append(Escape($"Q{selection.Quality}; target {selection.Metric}={selection.Target} +/-{selection.Tolerance}; actual={selection.Actual}; {selection.Candidates.Count} trials; calibration={selection.CalibrationMilliseconds:F1} ms; reused={selection.ReusedCandidates}."));
            }
            html.Append("</pre></details></td></tr>");
        }
        html.Append("</tbody></table></div></details></body></html>");
        File.WriteAllText(Path.Combine(options.Artifacts, "index.html"), html.ToString());
    }

    private const string Legend = "<details><summary>Reading the measurements</summary><p>Mean is time per operation; Error is the half-width of its 99.9% confidence interval. Allocated is managed memory per operation. Process peak includes runtime, pools and native heaps; sampling gives a lower bound, not native allocation per operation.</p><p>Encoded B is complete output size. Raw/encoded is compression ratio (higher is smaller). Workload MSE is reconstruction error (lower is better). Delta % is deviation from the group's MSE or file-size target. N/A means unavailable, never zero. Compare only within the same workload, profile, job and decoder policy; details retain the policy and calibration settings.</p></details>";

    private static string Number(double? value) => value?.ToString("G5", CultureInfo.InvariantCulture) ?? "N/A";
    private static string Markdown(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    private static string Escape(string value) => WebUtility.HtmlEncode(value);
    private static StringBuilder Header(string title) => new StringBuilder("<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>")
        .Append(Escape(title)).Append("</title><style>body{font:15px system-ui,sans-serif;margin:32px auto;padding:0 24px;max-width:1600px;color:#17232c;background:#f7f9fa}h1{font-size:28px}h2{margin-top:32px}h3{font-size:17px}p{max-width:1100px;line-height:1.6}nav{margin:16px 0}.scroll{overflow:auto;margin:12px 0 24px}table{border-collapse:collapse;background:white;font-variant-numeric:tabular-nums;width:100%}th,td{padding:10px 12px;border-bottom:1px solid #dce2e7;white-space:nowrap;text-align:right;vertical-align:top}th{background:#e8eef2;position:sticky;top:0}td:first-child,th:first-child{text-align:left}tbody tr:hover{background:#f1f6fa}.warning{background:#fff0c2;padding:12px;border-radius:6px}details{margin:12px 0}td details{margin:0;text-align:left;min-width:90px}summary{cursor:pointer;color:#185c9a}dl{display:grid;grid-template-columns:max-content minmax(150px,1fr);gap:8px 16px;max-width:640px;white-space:normal}dt{font-weight:600}dd{margin:0;overflow-wrap:anywhere}pre{white-space:pre-wrap;text-align:left;max-width:640px;min-width:300px;overflow-wrap:anywhere}a{color:#185c9a}@media(max-width:700px){body{margin:16px auto;padding:0 12px}th,td{padding:8px}}</style></head><body><h1>")
        .Append(Escape(title)).Append("</h1>");
}
