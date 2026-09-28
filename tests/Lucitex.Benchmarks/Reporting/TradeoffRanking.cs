using Lucitex.Benchmarks.Codecs;

namespace Lucitex.Benchmarks.Reporting;

internal enum TradeoffGoal { Compression, Balanced, Speed, Reference }

internal sealed record TradeoffSelection(string Group, string Profile, Library Library,
    TradeoffGoal Goal, string Candidate);

internal sealed record TradeoffMeasurement(string Group, string Profile, string Image, Library Library,
    string Candidate, string Settings, double Nanoseconds, double? ErrorNanoseconds,
    long Bytes, long RawBytes, string InputHash)
{
    public string Configuration => $"{Library}/{Candidate}";
}

internal sealed record TradeoffPoint(string Group, Library Library, string Candidate,
    double TimeRatio, double SizeRatio, int Images)
{
    public double Cost => Math.Sqrt(TimeRatio * SizeRatio);
}

internal sealed record TradeoffRank(TradeoffGoal Goal, int Rank, TradeoffPoint Point);

internal static class TradeoffRanking
{
    public const double SizeTolerance = 0.01;
    public const double TimeTolerance = 0.05;

    public static double GeometricMean(IEnumerable<double> values)
    {
        var items = values.ToArray();
        if (items.Length == 0 || items.Any(v => !double.IsFinite(v) || v <= 0))
            throw new ArgumentException("Geometric mean requires finite, positive observations.");
        return Math.Exp(items.Average(Math.Log));
    }

    public static bool Dominates(TradeoffPoint left, TradeoffPoint right) =>
        left.TimeRatio <= right.TimeRatio && left.SizeRatio <= right.SizeRatio
        && (left.TimeRatio < right.TimeRatio || left.SizeRatio < right.SizeRatio);

    // Bands are anchored to the best REMAINING value, never pairwise approximate
    // comparisons (which would be non-transitive and depend on enumeration order).
    public static IReadOnlyList<TradeoffPoint> Order(IEnumerable<TradeoffPoint> points, TradeoffGoal goal)
    {
        var remaining = points.ToList();
        var ordered = new List<TradeoffPoint>();
        while (remaining.Count > 0) {
            var limit = goal switch {
                TradeoffGoal.Compression => remaining.Min(p => p.SizeRatio) * (1 + SizeTolerance),
                TradeoffGoal.Speed => remaining.Min(p => p.TimeRatio) * (1 + TimeTolerance),
                _ => 0,
            };
            IOrderedEnumerable<TradeoffPoint> band = goal switch {
                TradeoffGoal.Compression => remaining.Where(p => p.SizeRatio <= limit)
                    .OrderBy(p => p.TimeRatio).ThenBy(p => p.SizeRatio),
                TradeoffGoal.Speed => remaining.Where(p => p.TimeRatio <= limit)
                    .OrderBy(p => p.SizeRatio).ThenBy(p => p.TimeRatio),
                TradeoffGoal.Balanced => remaining.OrderBy(p => p.Cost).ThenBy(p => p.SizeRatio).ThenBy(p => p.TimeRatio),
                _ => throw new ArgumentException("Reference is not a ranking goal."),
            };
            var batch = band.ThenBy(p => p.Library).ThenBy(p => p.Candidate, StringComparer.Ordinal).ToArray();
            ordered.AddRange(batch);
            foreach (var point in batch) remaining.Remove(point);
        }
        return ordered;
    }

    public static TradeoffGoal[] Goals => [TradeoffGoal.Compression, TradeoffGoal.Balanced, TradeoffGoal.Speed];

    public static IReadOnlyList<TradeoffPoint> Aggregate(IEnumerable<TradeoffMeasurement> measurements,
        TradeoffSelection reference, IReadOnlyList<string> images)
    {
        var rows = measurements.Where(m => m.Group == reference.Group).ToArray();
        var anchors = rows.Where(m => m.Library == reference.Library && m.Candidate == reference.Candidate)
            .ToDictionary(m => m.Image);
        if (images.Any(i => !anchors.ContainsKey(i))) return [];
        return rows.GroupBy(m => (m.Library, m.Candidate)).Where(g =>
            g.Count() == images.Count && images.All(i => g.Any(m => m.Image == i))).Select(g => {
                if (g.Any(m => m.InputHash != anchors[m.Image].InputHash || m.RawBytes != anchors[m.Image].RawBytes))
                    throw new InvalidDataException("Tradeoff candidates must use identical source pixels.");
                return new TradeoffPoint(reference.Group, g.Key.Library, g.Key.Candidate,
                    GeometricMean(g.Select(m => m.Nanoseconds / anchors[m.Image].Nanoseconds)),
                    GeometricMean(g.Select(m => (double)m.Bytes / anchors[m.Image].Bytes)), images.Count);
            }).ToArray();
    }

    public static IReadOnlyList<TradeoffSelection> Select(IReadOnlyList<TradeoffMeasurement> measurements, string[] images)
    {
        var selections = new List<TradeoffSelection>();
        foreach (var group in measurements.GroupBy(m => m.Group)) {
            // Stable reference preference, independent of the observed speed or size.
            var complete = group.GroupBy(m => m.Configuration).Where(g => g.Count() == images.Length
                && images.All(i => g.Any(m => m.Image == i))).Select(g => g.First()).ToArray();
            var anchor = complete.OrderBy(m => m.Library == Library.ImageSharp ? 0 : 1)
                .ThenBy(m => m.Candidate is "png-z6" or "webp-m4-q100" or "fixed" ? 0 : 1)
                .ThenBy(m => m.Library).ThenBy(m => m.Candidate, StringComparer.Ordinal).FirstOrDefault();
            if (anchor is null) continue;
            var reference = new TradeoffSelection(group.Key, anchor.Profile, anchor.Library, TradeoffGoal.Reference, anchor.Candidate);
            selections.Add(reference);
            var points = Aggregate(group, reference, images);
            var frontier = points.Where(p => !points.Any(q => q.Library == p.Library && Dominates(q, p))).ToArray();
            foreach (var goal in Goals) {
                foreach (var point in Order(frontier, goal).DistinctBy(p => p.Library))
                    selections.Add(new(group.Key, anchor.Profile, point.Library, goal, point.Candidate));
            }
        }
        return selections;
    }

    public static IReadOnlyList<TradeoffRank> Rank(IEnumerable<TradeoffPoint> points,
        IEnumerable<TradeoffSelection> selections)
    {
        var result = new List<TradeoffRank>();
        foreach (var goal in Goals) {
            var selected = points.Where(p => selections.Any(s => s.Group == p.Group && s.Library == p.Library
                && s.Candidate == p.Candidate && s.Goal == goal));
            var ordered = Order(selected, goal);
            for (var i = 0; i < ordered.Count; i++) {
                var previous = i > 0 ? ordered[i - 1] : null;
                var tied = previous is not null && previous.TimeRatio == ordered[i].TimeRatio
                    && previous.SizeRatio == ordered[i].SizeRatio;
                result.Add(new(goal, tied ? result[^1].Rank : i + 1, ordered[i]));
            }
        }
        return result;
    }
}
