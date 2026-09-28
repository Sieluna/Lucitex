using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Reports;
using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Formats;
using Lucitex.Benchmarks.Validation;

namespace Lucitex.Benchmarks.Reporting;

internal static class TradeoffRunner
{
    internal static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() },
    };

    public static int Run(RunOptions options)
    {
        EnsureSeparateCorpora(options);
        var calibration = options with { Cases = options.CalibrationCases, Memory = "managed",
            Artifacts = Path.Combine(options.Artifacts, "calibration") };
        Console.WriteLine("Tradeoffs: measuring calibration candidates; these are not the final rankings.");
        var first = Program.RunTimed(calibration, []);
        var measuredCalibration = Collect(first.Summaries);
        var selections = TradeoffRanking.Select(measuredCalibration, options.CalibrationCases).ToArray();
        var manifest = new {
            SchemaVersion = 1, CandidateCatalogVersion = EncoderCandidates.Version,
            options.CalibrationCases, EvaluationCases = options.Cases,
            TradeoffRanking.SizeTolerance, TradeoffRanking.TimeTolerance,
            SelectionRule = "Common anchored bands; within each band time breaks size ties / size breaks time ties. Balanced: sqrt(time ratio * size ratio). Geometric per-image aggregation. Per-library dominated candidates excluded from selection.",
            options.Smoke, Selections = selections,
            Candidates = options.Profiles.SelectMany(profile => options.Libraries.Select(library => new {
                Profile = profile, Library = library, Settings = EncoderCandidates.For(FormatCatalog.ForProfile(profile).Id, Enum.Parse<Library>(library)),
            })).ToArray(),
        };
        File.WriteAllText(Path.Combine(options.Artifacts, "selection.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        File.WriteAllText(Path.Combine(options.Artifacts, "calibration-measurements.json"), JsonSerializer.Serialize(measuredCalibration, JsonOptions));
        if (selections.Length == 0) {
            TradeoffReport.Write(options, measuredCalibration, [], selections);
            Console.Error.WriteLine("No configurations covered the complete calibration corpus. See calibration diagnostics.");
            return 1;
        }
        ValidationStore.Reset();
        var evaluation = options with { Artifacts = Path.Combine(options.Artifacts, "evaluation"),
            TradeoffSelections = selections };
        Console.WriteLine("Tradeoffs: selections frozen in selection.json; measuring independent evaluation cases.");
        var second = Program.RunTimed(evaluation, []);
        var measuredEvaluation = Collect(second.Summaries);
        TradeoffReport.Write(options, measuredCalibration, measuredEvaluation, selections);
        Console.WriteLine($"Tradeoff report: {Path.Combine(options.Artifacts, "index.html")}");
        return first.ExitCode != 0 || second.ExitCode != 0 || measuredEvaluation.Count == 0 ? 1 : 0;
    }

    internal static void EnsureSeparateCorpora(RunOptions options)
    {
        foreach (var format in options.Profiles.Select(p => FormatCatalog.ForProfile(p).Id).Distinct()) {
            var module = FormatCatalog.Get(format);
            string Identity(string id) {
                var image = module.CreateImage(id);
                return $"{image.Width}/{image.Height}/{image.Channels}/{image.Sha256}";
            }
            var tuning = options.CalibrationCases.Select(Identity).ToHashSet();
            if (options.Cases.Select(Identity).Any(tuning.Contains))
                throw new ArgumentException("Calibration and evaluation contain identical pixels (possibly under different IDs).");
        }
    }

    private static IReadOnlyList<TradeoffMeasurement> Collect(IEnumerable<Summary> summaries)
    {
        var rows = new List<TradeoffMeasurement>();
        foreach (var report in summaries.SelectMany(s => s.Reports)) {
            if (!report.Success || report.ResultStatistics is not { } stats
                || !double.IsFinite(stats.Mean) || stats.Mean <= 0
                || ComparisonColumn.Find(report.BenchmarkCase) is not { EligibleForTiming: true, PairingMatched: true,
                    Compression: { } compression, EncodedBytes: > 0 } validation
                || validation.Case.RateMatched || validation.Case.Operation != Operation.Encode) continue;
            var c = validation.Case;
            var group = $"{c.Profile} | {report.BenchmarkCase.Job.Id} | {validation.DecoderPolicy}";
            var margin = stats.ConfidenceInterval.Margin;
            rows.Add(new(group, c.Profile, c.Image, c.Library, c.Candidate, validation.EncoderSettings,
                stats.Mean, double.IsFinite(margin) ? margin : null, validation.EncodedBytes.Value,
                compression.RawBytes, validation.InputSha256));
        }
        return rows;
    }
}
