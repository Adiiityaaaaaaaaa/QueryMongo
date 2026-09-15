using MongoDB.Bson;

namespace QueryMongo.Core.Services;

/// <summary>One bar in a field's distribution chart.</summary>
public sealed record HistogramBucket(string Label, int Count, double Fraction);

/// <summary>
/// The distribution of one field's values, ready to draw.
/// </summary>
public sealed record FieldDistribution(
    string Path,
    DistributionKind Kind,
    IReadOnlyList<HistogramBucket> Buckets,
    string Summary)
{
    public bool HasBars => Buckets.Count > 0;
}

public enum DistributionKind { Categorical, Numeric, Temporal, Boolean, None }

/// <summary>
/// Turns sampled values into histograms, which is what Compass draws under each field
/// in its Schema tab.
/// </summary>
public static class SchemaCharts
{
    private const int MaxBars = 12;

    public static FieldDistribution Build(string path, IReadOnlyList<BsonValue> values)
    {
        var present = values.Where(v => v is not BsonNull).ToList();
        if (present.Count == 0)
            return new FieldDistribution(path, DistributionKind.None, [], "No values sampled.");

        if (present.All(v => v.IsBoolean))
            return Categorical(path, present, DistributionKind.Boolean);

        if (present.All(v => v.IsNumeric))
            return Numeric(path, present);

        if (present.All(v => v.IsValidDateTime))
            return Temporal(path, present);

        return Categorical(path, present, DistributionKind.Categorical);
    }

    private static FieldDistribution Categorical(
        string path, List<BsonValue> values, DistributionKind kind)
    {
        var groups = values
            .GroupBy(v => Render(v))
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        var shown = groups.Take(MaxBars).ToList();
        var others = groups.Skip(MaxBars).Sum(g => g.Count);

        var buckets = shown
            .Select(g => new HistogramBucket(g.Label, g.Count, (double)g.Count / values.Count))
            .ToList();

        if (others > 0)
            buckets.Add(new HistogramBucket(
                $"({groups.Count - MaxBars} more)", others, (double)others / values.Count));

        var summary = groups.Count == 1
            ? $"1 distinct value across {values.Count} sampled"
            : $"{groups.Count} distinct values across {values.Count} sampled";

        return new FieldDistribution(path, kind, buckets, summary);
    }

    private static FieldDistribution Numeric(string path, List<BsonValue> values)
    {
        var numbers = values.Select(v => v.ToDouble()).OrderBy(n => n).ToList();

        var min = numbers[0];
        var max = numbers[^1];

        var summary =
            $"min {Format(min)} · median {Format(Median(numbers))} · " +
            $"max {Format(max)} · mean {Format(numbers.Average())}";

        // All values identical: a histogram would be one full-width bar, so say it instead.
        if (max - min < double.Epsilon)
            return new FieldDistribution(path, DistributionKind.Numeric,
                [new HistogramBucket(Format(min), numbers.Count, 1.0)],
                $"every sampled value is {Format(min)}");

        var width = (max - min) / MaxBars;
        var counts = new int[MaxBars];

        foreach (var n in numbers)
        {
            // The maximum value would land one past the last bucket.
            var index = Math.Min((int)((n - min) / width), MaxBars - 1);
            counts[index]++;
        }

        var buckets = counts
            .Select((count, i) => new HistogramBucket(
                Format(min + (i * width)), count, (double)count / numbers.Count))
            .ToList();

        return new FieldDistribution(path, DistributionKind.Numeric, buckets, summary);
    }

    private static FieldDistribution Temporal(string path, List<BsonValue> values)
    {
        var dates = values.Select(v => v.ToUniversalTime()).OrderBy(d => d).ToList();

        var min = dates[0];
        var max = dates[^1];
        var summary = $"{min:yyyy-MM-dd} to {max:yyyy-MM-dd} across {dates.Count} sampled";

        var span = max - min;
        if (span.TotalSeconds < 1)
            return new FieldDistribution(path, DistributionKind.Temporal,
                [new HistogramBucket($"{min:yyyy-MM-dd}", dates.Count, 1.0)], summary);

        var width = span.TotalSeconds / MaxBars;
        var counts = new int[MaxBars];

        foreach (var d in dates)
        {
            var index = Math.Min((int)((d - min).TotalSeconds / width), MaxBars - 1);
            counts[index]++;
        }

        var buckets = counts
            .Select((count, i) => new HistogramBucket(
                min.AddSeconds(i * width).ToString("yyyy-MM-dd"), count, (double)count / dates.Count))
            .ToList();

        return new FieldDistribution(path, DistributionKind.Temporal, buckets, summary);
    }

    private static double Median(List<double> sorted) =>
        sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;

    private static string Format(double value) =>
        Math.Abs(value % 1) < 0.0001 && Math.Abs(value) < 1e15
            ? value.ToString("N0")
            : value.ToString("N2");

    private static string Render(BsonValue value) => value switch
    {
        BsonString s => s.Value.Length <= 28 ? s.Value : string.Concat(s.Value.AsSpan(0, 28), "…"),
        BsonBoolean b => b.Value ? "true" : "false",
        BsonDocument => "{document}",
        BsonArray a => $"[{a.Count} items]",
        _ => value.ToString() ?? ""
    };
}
