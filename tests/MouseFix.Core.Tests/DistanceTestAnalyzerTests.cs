using MouseFix.Core;

namespace MouseFix.Core.Tests;

public class DistanceTestAnalyzerTests
{
    // 800 DPI over 10 cm = 3150 counts.
    private const int CountsAt800Dpi = 3150;

    [Fact]
    public void Same_counts_slow_and_fast_is_consistent_and_gives_dpi()
    {
        var analyzer = new DistanceTestAnalyzer(10);
        analyzer.Add(CountsAt800Dpi, 0, 2000, 2000);
        analyzer.Add(CountsAt800Dpi + 40, 0, 1800, 1800);
        analyzer.Add(CountsAt800Dpi - 30, 0, 150, 150);
        analyzer.Add(CountsAt800Dpi, 0, 120, 120);

        Assert.Equal(DistanceVerdict.Consistent, analyzer.Verdict);
        Assert.Equal(800, analyzer.Dpi!.Value, 0);
        Assert.Equal(1000, analyzer.PollingHz!.Value, 0);
    }

    [Fact]
    public void More_counts_when_fast_is_acceleration()
    {
        var analyzer = new DistanceTestAnalyzer(10);
        analyzer.Add(CountsAt800Dpi, 0, 2000, 2000);
        analyzer.Add(CountsAt800Dpi, 0, 1800, 1800);
        analyzer.Add((int)(CountsAt800Dpi * 1.4), 0, 150, 150);
        analyzer.Add((int)(CountsAt800Dpi * 1.5), 0, 120, 120);

        Assert.Equal(DistanceVerdict.Accelerated, analyzer.Verdict);
    }

    [Fact]
    public void Fewer_counts_when_fast_is_losing_tracking()
    {
        var analyzer = new DistanceTestAnalyzer(10);
        analyzer.Add(CountsAt800Dpi, 0, 2000, 2000);
        analyzer.Add(CountsAt800Dpi, 0, 1800, 1800);
        analyzer.Add(CountsAt800Dpi / 2, 0, 150, 150);
        analyzer.Add(CountsAt800Dpi / 2, 0, 120, 120);

        Assert.Equal(DistanceVerdict.LosingCounts, analyzer.Verdict);
    }

    [Fact]
    public void Needs_clearly_different_speeds()
    {
        var analyzer = new DistanceTestAnalyzer(10);
        for (int i = 0; i < 4; i++)
            analyzer.Add(CountsAt800Dpi, 0, 500 + i * 50, 500);

        Assert.Equal(DistanceVerdict.NotEnoughData, analyzer.Verdict);
    }

    [Fact]
    public void Accidental_clicks_are_ignored()
    {
        var analyzer = new DistanceTestAnalyzer(10);

        Assert.False(analyzer.Add(3, 2, 80, 4));
        Assert.Empty(analyzer.Strokes);
    }
}
