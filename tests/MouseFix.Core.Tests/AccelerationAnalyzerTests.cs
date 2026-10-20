using MouseFix.Core;

namespace MouseFix.Core.Tests;

public class AccelerationAnalyzerTests
{
    // Simulates moving the mouse at a given speed (counts per ms) for many 40 ms windows.
    private static void Move(AccelerationAnalyzer analyzer, double countsPerMs, Func<double, double> cursorPerCount, int samples = 20)
    {
        for (int i = 0; i < samples; i++)
        {
            int raw = (int)Math.Round(countsPerMs * 40);
            int cursor = (int)Math.Round(raw * cursorPerCount(countsPerMs));
            analyzer.AddSample(raw, 0, raw, cursor, 0, 40);
        }
    }

    [Fact]
    public void Needs_both_slow_and_fast_movement()
    {
        var analyzer = new AccelerationAnalyzer();
        Move(analyzer, 0.3, _ => 1);

        Assert.Equal(AccelerationVerdict.NotEnoughData, analyzer.Verdict);
    }

    [Fact]
    public void Same_ratio_of_one_at_all_speeds_is_one_to_one()
    {
        var analyzer = new AccelerationAnalyzer();
        Move(analyzer, 0.3, _ => 1);
        Move(analyzer, 6, _ => 1);

        Assert.Equal(AccelerationVerdict.OneToOne, analyzer.Verdict);
    }

    [Fact]
    public void Constant_ratio_other_than_one_is_scaled()
    {
        var analyzer = new AccelerationAnalyzer();
        Move(analyzer, 0.3, _ => 0.5);
        Move(analyzer, 6, _ => 0.5);

        Assert.Equal(AccelerationVerdict.Scaled, analyzer.Verdict);
        Assert.Equal(0.5, analyzer.OverallRatio, 2);
    }

    [Fact]
    public void Ratio_growing_with_speed_is_acceleration()
    {
        var analyzer = new AccelerationAnalyzer();
        Move(analyzer, 0.3, speed => 0.6 + speed * 0.2);
        Move(analyzer, 6, speed => 0.6 + speed * 0.2);

        Assert.Equal(AccelerationVerdict.Accelerated, analyzer.Verdict);
        Assert.True(analyzer.Spread > 2);
    }

    [Fact]
    public void Samples_where_the_hand_changed_direction_are_ignored()
    {
        var analyzer = new AccelerationAnalyzer();
        // Went +90 then -60: net 30 counts over a 150-count path. Lag made the cursor show only 20.
        analyzer.AddSample(30, 0, 150, 20, 0, 40);

        Assert.All(analyzer.Buckets, b => Assert.Equal(0, b.Samples));
    }

    [Fact]
    public void Tiny_movements_are_ignored()
    {
        var analyzer = new AccelerationAnalyzer();
        analyzer.AddSample(2, 1, 3, 5, 3, 40);

        Assert.All(analyzer.Buckets, b => Assert.Equal(0, b.Samples));
    }
}
