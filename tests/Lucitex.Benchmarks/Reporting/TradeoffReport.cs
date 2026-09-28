using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Lucitex.Benchmarks.Codecs;

namespace Lucitex.Benchmarks.Reporting;

internal static class TradeoffReport
{
    public static void Regenerate(string directory)
    {
        directory = Path.GetFullPath(directory);
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "tradeoffs.json")));
        var validationPath = Path.Combine(directory, "evaluation", "validation.json");
        if (!File.Exists(validationPath)) validationPath = Path.Combine(directory, "calibration", "validation.json");
        using var validation = JsonDocument.Parse(File.ReadAllText(validationPath));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "selection.json")));
        var options = validation.RootElement.GetProperty("Options").Deserialize<RunOptions>(TradeoffRunner.JsonOptions)!
            with { Artifacts = directory, Cases = manifest.RootElement.GetProperty("EvaluationCases").Deserialize<string[]>()! };
        var calibration = data.RootElement.GetProperty("Calibration").Deserialize<TradeoffMeasurement[]>(TradeoffRunner.JsonOptions)!;
        var evaluation = data.RootElement.GetProperty("Measurements").Deserialize<TradeoffMeasurement[]>(TradeoffRunner.JsonOptions)!;
        var selections = data.RootElement.GetProperty("Selections").Deserialize<TradeoffSelection[]>(TradeoffRunner.JsonOptions)!;
        // Older smoke artifacts used zero for an unavailable confidence margin.
        if (options.Smoke) {
            calibration = calibration.Select(m => m with { ErrorNanoseconds = null }).ToArray();
            evaluation = evaluation.Select(m => m with { ErrorNanoseconds = null }).ToArray();
        }
        Write(options, calibration, evaluation, selections);
    }

    public static void Write(RunOptions options, IReadOnlyList<TradeoffMeasurement> calibration,
        IReadOnlyList<TradeoffMeasurement> evaluation, IReadOnlyList<TradeoffSelection> selections)
    {
        var overall = new List<TradeoffRank>();
        var perImage = new List<object>();
        var html = new StringBuilder("""
            <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Lucitex · Compression / Balanced / Speed</title>
            <style>body{font:15px system-ui;margin:32px auto;max-width:1400px;padding:0 24px;color:#17232c;background:#f7f9fa}p{line-height:1.6}table{border-collapse:collapse;background:white;width:100%;font-variant-numeric:tabular-nums}th,td{padding:10px;border-bottom:1px solid #dce2e7;text-align:right}th:first-child,td:first-child{text-align:left}th{background:#e8eef2}.scroll{overflow:auto}details{margin:20px 0}summary{cursor:pointer}svg{background:white;width:100%;max-width:760px;height:auto}.warning{background:#fff0c2;padding:12px}a{color:#185c9a}code{overflow-wrap:anywhere}h2{margin-top:36px}</style>
            <h1>Compression / Balanced / Speed</h1>
            <nav><a href="selection.json">Frozen selections</a> · <a href="tradeoffs.json">Ranking data</a> · <a href="calibration/index.html">Calibration diagnostics</a> · <a href="evaluation/index.html">Evaluation diagnostics</a></nav>
            <p>Each library's representative configurations are chosen on a separate calibration corpus and frozen before evaluation. The three goals compare both latency and output size; they do not imply equal effort or CPU budgets. Lower time, size and cost are better; higher raw/encoded ratio is better.</p>
            <p>Compression: anchored 1% size bands, faster first within each band. Speed: anchored 5% time bands, smaller first within each band. Balanced: √(time/reference × size/reference). Bands are repeatedly anchored to the best remaining value across competitors. Overall values are geometric means of per-image ratios, with equal weight per image. Missing images never count as wins.</p>
            <p>Ranks describe measured means, not statistical significance. The 5% rule is a policy tolerance, not a confidence interval. Per-image timing intervals show the BenchmarkDotNet 99.9% margin; overlapping intervals need more measurement before claiming a speed difference. Pareto labels describe only the measured candidate set.</p>
            <p>The main table shows absolute total time and output size for one encode of every evaluation image. Total time is the sum of per-image measured means, not benchmark wall-clock duration. Expand an image for its individual measurements. Units: 1 KB = 1,000 bytes; 1 MB = 1,000,000 bytes. Hover a value for exact nanoseconds or bytes. Ranking ratios remain geometric means.</p>
            """);
        if (options.Smoke) html.Append("<p class='warning'>SMOKE: ranks and selections are pipeline diagnostics only. Do not use them as performance conclusions.</p>");
        if (evaluation.Count == 0) html.Append("<p class='warning'>No eligible evaluation measurements. No rankings are available.</p>");
        foreach (var reference in selections.Where(s => s.Goal == TradeoffGoal.Reference)) {
            var chosen = selections.Where(s => s.Group == reference.Group).ToArray();
            var rows = evaluation.Where(m => m.Group == reference.Group).ToArray();
            var aggregate = TradeoffRanking.Aggregate(rows, reference, options.Cases);
            var ranks = TradeoffRanking.Rank(aggregate, chosen);
            overall.AddRange(ranks);
            html.Append("<h2>").Append(E(reference.Group)).Append("</h2><p>Reference: ")
                .Append(E($"{reference.Library}/{reference.Candidate}")).Append(". Required evaluation images: ")
                .Append(options.Cases.Length).Append(".</p>");
            html.Append("<p>Coverage: ");
            foreach (var library in options.Libraries.Select(Enum.Parse<Library>)) {
                var configured = chosen.Where(s => s.Library == library && s.Goal != TradeoffGoal.Reference).ToArray();
                var count = rows.Where(m => m.Library == library).Select(m => m.Image).Distinct().Count();
                html.Append(E($"{library}: {count}/{options.Cases.Length} images, {configured.Length}/3 calibrated goals; "));
            }
            html.Append("</p>");
            Table(html, ranks, rows, totals: true);
            foreach (var image in options.Cases) {
                var imageRows = rows.Where(m => m.Image == image).ToArray();
                var points = TradeoffRanking.Aggregate(imageRows, reference, [image]);
                var imageRanks = TradeoffRanking.Rank(points, chosen);
                perImage.Add(new { reference.Group, Image = image, Rankings = imageRanks });
                html.Append("<details><summary>").Append(E(image)).Append(" — timing, size, ranks and plot</summary>");
                Table(html, imageRanks, imageRows, totals: false);
                html.Append(Plot(imageRows.Select(m => new TradeoffPoint(m.Group, m.Library, m.Candidate,
                    m.Nanoseconds / 1e6, m.Bytes, 1)).ToArray(), "Time (log scale)", "Output size (log scale)", absolute: true));
                html.Append("</details>");
            }
            var tuning = TradeoffRanking.Aggregate(calibration, reference, options.CalibrationCases);
            html.Append("<details><summary>Calibration candidates and Pareto frontier (not evaluation results)</summary>")
                .Append(Plot(tuning, "Time/reference (log scale)", "Bytes/reference (log scale)"));
            html.Append("<div class='scroll'><table><tr><th>Library / candidate</th><th>Total time</th><th>Total size</th><th>Raw/encoded</th><th>Time/ref</th><th>Size/ref</th><th>Cost</th><th>Pareto</th><th>Selected goals</th></tr>");
            foreach (var point in tuning.OrderBy(p => p.Library).ThenBy(p => p.Candidate)) {
                var goals = chosen.Where(s => s.Library == point.Library && s.Candidate == point.Candidate).Select(s => s.Goal);
                html.Append("<tr><td>").Append(E($"{point.Library}/{point.Candidate}")).Append("</td>");
                ActualCells(html, calibration.Where(m => m.Group == point.Group && m.Library == point.Library && m.Candidate == point.Candidate).ToArray(), totals: true);
                html.Append("<td>").Append(N(point.TimeRatio)).Append("</td><td>").Append(N(point.SizeRatio)).Append("</td><td>")
                    .Append(N(point.Cost)).Append("</td><td>").Append(tuning.Any(q => TradeoffRanking.Dominates(q, point)) ? "Dominated" : "Frontier")
                    .Append("</td><td>").Append(E(string.Join(", ", goals))).Append("</td></tr>");
            }
            html.Append("</table></div></details>");
        }
        html.Append("<details><summary>Measured evaluation configurations</summary><div class='scroll'><table><tr><th>Profile / image</th><th>Library / candidate</th><th>Actual settings</th><th>Time ±99.9% margin</th><th>Output size</th><th>Raw/encoded</th></tr>");
        foreach (var row in evaluation) {
            html.Append("<tr><td>").Append(E($"{row.Profile}/{row.Image}"))
                .Append("</td><td>").Append(E(row.Configuration)).Append("</td><td>").Append(E(row.Settings)).Append("</td>");
            ActualCells(html, [row], totals: false);
            html.Append("</tr>");
        }
        html.Append("</table></div></details></html>");
        File.WriteAllText(Path.Combine(options.Artifacts, "index.html"), html.ToString());
        File.WriteAllText(Path.Combine(options.Artifacts, "tradeoffs.json"), JsonSerializer.Serialize(new {
            SchemaVersion = 1, options.Smoke, Overall = overall, PerImage = perImage, Measurements = evaluation,
            Calibration = calibration, Selections = selections,
        }, TradeoffRunner.JsonOptions));
    }

    private static void Table(StringBuilder html, IReadOnlyList<TradeoffRank> ranks,
        TradeoffMeasurement[] measurements, bool totals)
    {
        if (ranks.Count == 0) { html.Append("<p>No complete comparable measurements for this scope.</p>"); return; }
        html.Append("<div class='scroll'><table><tr><th>Goal</th><th>Rank</th><th>Library / candidate</th>");
        html.Append(totals ? "<th>Total time</th><th>Total size</th><th>Raw/encoded</th>"
            : "<th>Time ±99.9% margin</th><th>Output size</th><th>Raw/encoded</th>");
        html.Append("<th>Time/ref</th><th>Size/ref</th><th>Cost</th><th>Images</th></tr>");
        foreach (var row in ranks) {
            var p = row.Point;
            html.Append("<tr><td>").Append(row.Goal).Append("</td><td>").Append(row.Rank)
                .Append("</td><td>").Append(E($"{p.Library}/{p.Candidate}")).Append("</td>");
            ActualCells(html, measurements.Where(m => m.Library == p.Library && m.Candidate == p.Candidate).ToArray(), totals);
            html.Append("<td>").Append(N(p.TimeRatio)).Append("</td><td>").Append(N(p.SizeRatio))
                .Append("</td><td>").Append(N(p.Cost)).Append("</td><td>").Append(p.Images).Append("</td></tr>");
        }
        html.Append("</table></div>");
    }

    private static void ActualCells(StringBuilder html, IReadOnlyList<TradeoffMeasurement> samples, bool totals)
    {
        var time = samples.Sum(m => m.Nanoseconds);
        var bytes = samples.Sum(m => m.Bytes);
        html.Append("<td title='").Append(time.ToString("G17", CultureInfo.InvariantCulture)).Append(" ns'>").Append(Time(time));
        if (!totals) html.Append(" ± ").Append(samples.Single().ErrorNanoseconds is { } error ? Time(error) : "unavailable");
        html.Append("</td><td title='").Append(bytes.ToString(CultureInfo.InvariantCulture)).Append(" bytes'>").Append(Size(bytes))
            .Append("</td><td>").Append(N((double)samples.Sum(m => m.RawBytes) / bytes)).Append("</td>");
    }

    private static string Time(double nanoseconds) => nanoseconds >= 1e9 ? N(nanoseconds / 1e9) + " s"
        : nanoseconds >= 1e6 ? N(nanoseconds / 1e6) + " ms"
        : nanoseconds >= 1e3 ? N(nanoseconds / 1e3) + " μs" : N(nanoseconds) + " ns";

    private static string Size(double bytes) => bytes >= 1e9 ? N(bytes / 1e9) + " GB"
        : bytes >= 1e6 ? N(bytes / 1e6) + " MB"
        : bytes >= 1e3 ? N(bytes / 1e3) + " KB" : N(bytes) + " B";

    private static string Plot(IReadOnlyList<TradeoffPoint> points, string xLabel, string yLabel, bool absolute = false)
    {
        if (points.Count == 0) return "";
        var minX = Math.Log10(points.Min(p => p.TimeRatio)) - .05;
        var maxX = Math.Max(minX + .2, Math.Log10(points.Max(p => p.TimeRatio)) + .05);
        var minY = Math.Log10(points.Min(p => p.SizeRatio)) - .05;
        var maxY = Math.Max(minY + .2, Math.Log10(points.Max(p => p.SizeRatio)) + .05);
        double X(TradeoffPoint p) => 90 + 590 * (Math.Log10(p.TimeRatio) - minX) / (maxX - minX);
        double Y(TradeoffPoint p) => 340 - 290 * (Math.Log10(p.SizeRatio) - minY) / (maxY - minY);
        string[] colors = ["#1769aa", "#b34400", "#217a3c", "#8a43a5", "#c32852"];
        var svg = new StringBuilder("<svg viewBox='0 0 760 420' role='img' aria-label='Latency and encoded size; lower left is better'><path d='M90 45 V340 H690' fill='none' stroke='#555'/>");
        for (var i = 0; i <= 4; i++) {
            var f = i / 4d;
            var x = Math.Pow(10, minX + (maxX - minX) * f);
            var y = Math.Pow(10, minY + (maxY - minY) * f);
            svg.Append($"<text x='{N(90 + 590 * f)}' y='360' text-anchor='middle' font-size='12'>{(absolute ? Time(x * 1e6) : N(x))}</text>")
                .Append($"<text x='82' y='{N(344 - 290 * f)}' text-anchor='end' font-size='12'>{(absolute ? Size(y) : N(y))}</text>");
        }
        var frontier = points.Where(p => !points.Any(q => TradeoffRanking.Dominates(q, p))).OrderBy(p => p.TimeRatio).ToArray();
        svg.Append("<polyline fill='none' stroke='#555' stroke-dasharray='4 4' points='")
            .Append(string.Join(" ", frontier.Select(p => $"{N(X(p))},{N(Y(p))}"))).Append("'/>");
        foreach (var p in points) svg.Append($"<circle cx='{N(X(p))}' cy='{N(Y(p))}' r='{(frontier.Contains(p) ? 6 : 4)}' fill='{colors[(int)p.Library]}'><title>")
            .Append(E($"{p.Library}/{p.Candidate}: time={(absolute ? Time(p.TimeRatio * 1e6) : N(p.TimeRatio))}, size={(absolute ? Size(p.SizeRatio) : N(p.SizeRatio))}; {(frontier.Contains(p) ? "Pareto frontier" : "dominated")}"))
            .Append("</title></circle>");
        svg.Append("<text x='380' y='388' text-anchor='middle'>").Append(E(xLabel)).Append("</text><text x='90' y='25'>")
            .Append(E(yLabel)).Append(" · lower left is better</text>");
        foreach (var library in points.Select(p => p.Library).Distinct().Order()) svg.Append($"<text x='{20 + (int)library * 145}' y='412' fill='{colors[(int)library]}' font-size='12'>● {library}</text>");
        return svg.Append("</svg>").ToString();
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
    private static string N(double value) => value.ToString("G4", CultureInfo.InvariantCulture);
}
