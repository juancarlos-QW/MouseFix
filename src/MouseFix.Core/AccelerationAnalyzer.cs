namespace MouseFix.Core;

public enum AccelerationVerdict
{
    NotEnoughData,
    /// <summary>Same ratio at every speed and that ratio is 1: each sensor count moves the cursor one pixel.</summary>
    OneToOne,
    /// <summary>Same ratio at every speed, but not 1 (pointer speed slider is not 10).</summary>
    Scaled,
    /// <summary>The ratio grows with hand speed.</summary>
    Accelerated,
}

public sealed record SpeedBucket(string Name, int Samples, double? MedianRatio);

/// <summary>
/// Compares raw sensor movement with cursor movement, grouped by hand speed.
/// Without acceleration, cursor/raw is the same at every speed.
/// </summary>
public sealed class AccelerationAnalyzer
{
    public const int MinSamplesPerBucket = 10;

    // Upper limits of each bucket, in sensor counts per millisecond.
    private static readonly double[] BucketLimits = [0.5, 2, 5];
    private static string[] BucketNames => [L.T("Lento", "Slow"), L.T("Médio", "Medium"), L.T("Rápido", "Fast"), L.T("Muito rápido", "Very fast")];

    // Tiny movements are dominated by pixel rounding, so they are ignored.
    private const double MinCounts = 8;
    private const double MinStraightness = 0.9;
    private const double SpreadTolerance = 0.05;
    private const double OneToOneTolerance = 0.03;

    private readonly List<double>[] _ratios = BucketNames.Select(_ => new List<double>()).ToArray();

    /// <param name="rawPath">Sum of |dx| + |dy| of every sensor packet in the sample.</param>
    public void AddSample(int rawDx, int rawDy, long rawPath, int cursorDx, int cursorDy, double elapsedMs)
    {
        double raw = Math.Sqrt((double)rawDx * rawDx + (double)rawDy * rawDy);
        if (raw < MinCounts || elapsedMs <= 0)
            return;

        // If the hand changed direction inside the sample, the counts partly cancel out: the sample looks
        // slower than it was and any timing jitter becomes a large ratio error. Skip those.
        if (Math.Abs(rawDx) + Math.Abs(rawDy) < rawPath * MinStraightness)
            return;

        double cursor = Math.Sqrt((double)cursorDx * cursorDx + (double)cursorDy * cursorDy);
        double speed = raw / elapsedMs;
        int bucket = Array.FindIndex(BucketLimits, limit => speed < limit);
        _ratios[bucket < 0 ? BucketLimits.Length : bucket].Add(cursor / raw);
    }

    public void Reset()
    {
        foreach (var list in _ratios)
            list.Clear();
    }

    public IReadOnlyList<SpeedBucket> Buckets =>
        _ratios.Select((r, i) => new SpeedBucket(BucketNames[i], r.Count, r.Count >= MinSamplesPerBucket ? Median(r) : null)).ToList();

    /// <summary>How much faster the cursor moves per count at the fastest speed vs. the slowest (1 = no acceleration).</summary>
    public double Spread
    {
        get
        {
            var medians = Buckets.Where(b => b.MedianRatio is not null).Select(b => b.MedianRatio!.Value).ToList();
            return medians.Count < 2 || medians.Min() <= 0 ? 1 : medians.Max() / medians.Min();
        }
    }

    public double OverallRatio => Median(_ratios.SelectMany(r => r).ToList());

    public AccelerationVerdict Verdict
    {
        get
        {
            var buckets = Buckets;
            // Need both a slow and a fast group to compare.
            bool hasSlow = buckets[0].MedianRatio is not null || buckets[1].MedianRatio is not null;
            bool hasFast = buckets[2].MedianRatio is not null || buckets[3].MedianRatio is not null;
            if (!hasSlow || !hasFast)
                return AccelerationVerdict.NotEnoughData;

            if (Spread > 1 + SpreadTolerance)
                return AccelerationVerdict.Accelerated;

            return Math.Abs(OverallRatio - 1) <= OneToOneTolerance ? AccelerationVerdict.OneToOne : AccelerationVerdict.Scaled;
        }
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
            return 0;
        var sorted = values.Order().ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
