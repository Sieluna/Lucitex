using System.Net;
using System.Text;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Lucitex.Benchmarks.Reporting;

internal sealed class ComparisonExporter : IExporter
{
    public string Name => nameof(ComparisonExporter);
    public void ExportToLog(Summary summary, ILogger logger) { }

    public IEnumerable<string> ExportToFiles(Summary summary, ILogger logger)
    {
        var title = summary.BenchmarksCases.Length > 0 ? summary.BenchmarksCases[0].Descriptor.Type.Name : summary.Title;
        var html = Header(title);
        html.Append("<table><thead><tr>");
        var columns = summary.Table.Columns.Where(c => c.NeedToShow).ToArray();
        var markdown = new StringBuilder("## ").AppendLine(title).AppendLine();
        markdown.Append("| ").Append(string.Join(" | ", columns.Select(c => Markdown(c.Header)))).AppendLine(" |");
        markdown.Append("| ").Append(string.Join(" | ", columns.Select(_ => "---"))).AppendLine(" |");
        foreach (var column in columns) {
            html.Append("<th>").Append(Escape(column.Header)).Append("</th>");
        }
        html.Append("</tr></thead><tbody>");
        for (var i = 0; i < summary.BenchmarksCases.Length; i++) {
            var benchmark = summary.BenchmarksCases[i];
            var validGroup = summary.GetLogicalGroupForBenchmark(benchmark).Where(b => summary[b]?.ResultStatistics is not null).ToArray();
            var valid = summary[benchmark]?.ResultStatistics is not null && validGroup.Length > 1;
            var fastest = valid && RankColumn.Arabic.GetValue(summary, benchmark) == "1";
            var allocation = summary[benchmark]?.GcStats.GetBytesAllocatedPerOperation(benchmark);
            var smallestAllocation = valid && allocation.HasValue && validGroup.All(b =>
                summary[b]?.GcStats.GetBytesAllocatedPerOperation(b) is { } bytes && bytes >= allocation.Value);
            var size = ComparisonColumn.Find(benchmark)?.EncodedBytes;
            var smallestSize = valid && size.HasValue && validGroup.All(b =>
                ComparisonColumn.Find(b)?.EncodedBytes is { } bytes && bytes >= size.Value);
            html.Append("<tr>");
            var cells = new string[columns.Length];
            for (var c = 0; c < columns.Length; c++) {
                var column = columns[c];
                var winner = column.Header == "Mean" && fastest || column.Header == "Allocated" && smallestAllocation
                    || column.Header == "Encoded B" && smallestSize;
                html.Append(winner ? "<td class='winner'>" : "<td>")
                    .Append(Escape(summary.Table.FullContent[i][column.Index])).Append("</td>");
                var value = Markdown(summary.Table.FullContent[i][column.Index]);
                cells[c] = winner ? $"**{value}**" : value;
            }
            html.Append("</tr>");
            markdown.Append("| ").Append(string.Join(" | ", cells)).AppendLine(" |");
        }
        html.Append("</tbody></table></body></html>");
        var path = Path.Combine(summary.ResultsDirectoryPath, title + "-comparison.html");
        File.WriteAllText(path, html.ToString());
        var markdownPath = Path.ChangeExtension(path, ".md");
        File.WriteAllText(markdownPath, markdown.ToString());
        return [path, markdownPath];
    }

    public static void WriteIndex(RunOptions options)
    {
        var html = Header("Lucitex codec comparisons");
        html.Append("<p>Each case/profile/job is a separate comparison. Green timing cells use BenchmarkDotNet Rank = 1 (ties allowed). Ratio and statistical tests compare against Lucitex; the equivalence threshold is 5%. Green allocation and size cells show observed minima, not statistical significance. Size alone does not imply equal image quality.</p>");
        html.Append("<p>Groups with only one supported implementation are standalone measurements and have no highlighted winner. EXR profiles measure normalized byte-to-HALF sample conversion, not full HDR quality. KTX2 profiles measure RGBA8 container conversion, not GPU block compression.</p>");
        if (options.Quick) {
            html.Append("<p class='warning'>SMOKE RUN: timing and rankings are for pipeline verification only.</p>");
        }
        html.Append("<p>Allocated: managed B/op from BenchmarkDotNet. All threads B: separate process workload. Process peak MiB: sampled total private bytes including managed/native heaps, pools and runtime; a lower bound, not native allocation bytes. Unavailable values remain N/A. Quality and exact encoded sizes are measured before timing.</p>");
        html.Append("<p>Full provenance, correctness checks, hashes and exclusions: <a href='validation.json'>validation.json</a>. BenchmarkDotNet's JSON/CSV reports retain raw statistics. No single overall winner is inferred across distinct workloads.</p><ul>");
        foreach (var path in Directory.EnumerateFiles(options.Artifacts, "*-comparison.html", SearchOption.AllDirectories)) {
            html.Append("<li><a href='").Append(Escape(Path.GetRelativePath(options.Artifacts, path).Replace('\\', '/')))
                .Append("'>").Append(Escape(Path.GetFileNameWithoutExtension(path))).Append("</a></li>");
        }
        html.Append("</ul></body></html>");
        File.WriteAllText(Path.Combine(options.Artifacts, "index.html"), html.ToString());
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
    private static string Markdown(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static StringBuilder Header(string title) => new StringBuilder("<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>")
        .Append(Escape(title)).Append("</title><style>body{font:15px system-ui,sans-serif;margin:32px;color:#17232c;background:#f7f9fa}h1{font-size:26px}p{max-width:1100px;line-height:1.6}table{border-collapse:collapse;background:white;font-variant-numeric:tabular-nums}th,td{padding:10px 14px;border-bottom:1px solid #dce2e7;white-space:nowrap;text-align:right}th{background:#e8eef2}td:first-child,th:first-child{text-align:left}.winner{background:#d5f3df;color:#11552c;font-weight:700}.warning{background:#fff0c2;padding:12px}a{color:#185c9a}</style></head><body><h1>")
        .Append(Escape(title)).Append("</h1>");
}
