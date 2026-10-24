using MouseFix.Core;

namespace MouseFix.Core.Tests;

public class SettingsChangeTests
{
    private static readonly MouseSettings Expected =
        new(0, 0, 0, 10, SmoothMouseCurve.LinearX(100), SmoothMouseCurve.LinearY(), 3);

    [Fact]
    public void Describes_a_game_turning_acceleration_on_and_changing_speed()
    {
        var current = Expected with { Acceleration = 1, Threshold1 = 6, Threshold2 = 10, Speed = 6 };

        Assert.Equal("aceleração ligada, velocidade 6/20 (era 10/20), limites de aceleração 6/10",
            SettingsChange.Describe(current, Expected));
    }

    [Fact]
    public void Describes_curve_and_scroll_changes()
    {
        var current = Expected with { SmoothMouseYCurve = SmoothMouseCurve.WindowsDefaultY, ScrollLines = 5 };

        Assert.Equal("rolagem 5 linha(s) (era 3), curvas de aceleração", SettingsChange.Describe(current, Expected));
    }

    [Fact]
    public void Same_settings_mean_nothing_changed()
    {
        Assert.Equal("nada", SettingsChange.Describe(Expected, Expected));
    }
}
