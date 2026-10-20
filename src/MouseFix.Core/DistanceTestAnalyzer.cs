namespace MouseFix.Core;

public enum DistanceVerdict
{
    NotEnoughData,
    /// <summary>Same counts for the same distance at any speed: no acceleration in the mouse or its driver.</summary>
    Consistent,
    /// <summary>Fast strokes produce more counts than slow ones over the same distance.</summary>
    Accelerated,
    /// <summary>Fast strokes produce fewer counts (sensor losing tracking, or deceleration in software).</summary>
    LosingCounts,
}

/// <summary>One drag of the mouse across the known distance.</summary>
public sealed record Stroke(double Counts, double DurationMs, int Packets)
{
    public double PacketsPerSecond => DurationMs > 0 ? Packets * 1000 / DurationMs : 0;
}

/// <summary>
/// The user drags the mouse over the same physical distance several times, slow and fast.
/// Without acceleration before Windows (firmware or driver), every stroke gives the same counts.
/// This also yields the real DPI and the polling rate.
/// </summary>
public sealed class DistanceTestAnalyzer
{
    public const int MinStrokesPerGroup = 2;

    // Hand placement varies a few millimetres per stroke, so small differences are expected.
    private const double Tolerance = 0.08;
    // The fast strokes must be clearly faster than the slow ones for the comparison to mean anything.
    private const double MinSpeedRatio = 2.5;
    private const double MinCounts = 100;

    private readonly List<Stroke> _strokes = [];

    public DistanceTestAnalyzer(double distanceCm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(distanceCm);
        DistanceCm = distanceCm;
    }

    public double DistanceCm { get; set; }

    public IReadOnlyList<Stroke> Strokes => _strokes;

    /// <summary>Adds a stroke; returns false if it was too short to count (e.g. an accidental click).</summary>
    public bool Add(int dx, int dy, double durationMs, int packets)
    {
        double counts = Math.Sqrt((double)dx * dx + (double)dy * dy);
        if (counts < MinCounts || durationMs <= 0)
            return false;
        _strokes.Add(new Stroke(counts, durationMs, packets));
        return true;
    }

    public void Reset() => _strokes.Clear();

    public double? Dpi => _strokes.Count == 0 ? null : Median(_strokes.Select(s => s.Counts)) / (DistanceCm / 2.54);

    /// <summary>Fast strokes saturate the polling rate, so the highest packet rate is the best estimate.</summary>
    public double? PollingHz => _strokes.Count == 0 ? null : _strokes.Max(s => s.PacketsPerSecond);

    /// <summary>Counts of the fastest strokes relative to the slowest ones (1 = identical).</summary>
    public double? FastToSlowRatio => Groups() is var (slow, fast) ? fast.Average(s => s.Counts) / slow.Average(s => s.Counts) : null;

    public DistanceVerdict Verdict => FastToSlowRatio switch
    {
        null => DistanceVerdict.NotEnoughData,
        > 1 + Tolerance => DistanceVerdict.Accelerated,
        < 1 - Tolerance => DistanceVerdict.LosingCounts,
        _ => DistanceVerdict.Consistent,
    };

    private (List<Stroke> Slow, List<Stroke> Fast)? Groups()
    {
        if (_strokes.Count < MinStrokesPerGroup * 2)
            return null;

        var bySpeed = _strokes.OrderBy(s => s.DurationMs).ToList(); // shortest duration = fastest
        var fast = bySpeed.Take(MinStrokesPerGroup).ToList();
        var slow = bySpeed.TakeLast(MinStrokesPerGroup).ToList();
        if (slow.Average(s => s.DurationMs) < fast.Average(s => s.DurationMs) * MinSpeedRatio)
            return null;
        return (slow, fast);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
