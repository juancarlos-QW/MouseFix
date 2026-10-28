using MouseFix.Core;
using Xunit;

namespace MouseFix.Core.Tests;

public class SensitivityCalculatorTests
{
    [Fact]
    public void Speed10IsOneToOne()
    {
        Assert.Equal(800, SensitivityCalculator.EffectiveDpi(800, 10), 6);
        Assert.Equal(800 / 2.54, SensitivityCalculator.PixelsPerCm(800, 10), 6);
    }

    [Fact]
    public void SlowerSpeedLowersEffectiveDpi()
    {
        // Speed 6 is 0.5x.
        Assert.Equal(800, SensitivityCalculator.EffectiveDpi(1600, 6), 6);
    }

    [Fact]
    public void CmToCrossFullHdAt800Dpi()
    {
        // 1920 px / (800 / 2.54 px per cm) = 6.096 cm.
        Assert.Equal(6.096, SensitivityCalculator.CmForPixels(1920, 800, 10), 3);
    }

    [Fact]
    public void DoublingDpiHalvesTheSpeed()
    {
        var suggestion = SensitivityCalculator.SpeedForNewDpi(800, 10, 1600);
        Assert.Equal(6, suggestion.Speed);
        Assert.True(suggestion.IsExact);
    }

    [Fact]
    public void SameDpiKeepsSameSpeed()
    {
        var suggestion = SensitivityCalculator.SpeedForNewDpi(800, 8, 800);
        Assert.Equal(8, suggestion.Speed);
        Assert.Equal(1, suggestion.FeelRatio, 6);
    }

    [Fact]
    public void ReportsWhenNoSpeedMatchesExactly()
    {
        // 800 at speed 10 with 1000 DPI needs 0.8x, which no Windows speed gives (8 = 0.75x, 9 = 0.875x).
        var suggestion = SensitivityCalculator.SpeedForNewDpi(800, 10, 1000);
        Assert.False(suggestion.IsExact);
        Assert.InRange(suggestion.FeelRatio, 0.9, 1.1);
    }

    [Fact]
    public void DpiForOneToOneKeepsTheFeel()
    {
        // 1600 DPI at 0.5x feels like 800 DPI at 1:1.
        Assert.Equal(800, SensitivityCalculator.DpiForOneToOne(1600, 6), 6);
    }
}
