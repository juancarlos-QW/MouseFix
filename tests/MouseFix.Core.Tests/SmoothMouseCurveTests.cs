using MouseFix.Core;

namespace MouseFix.Core.Tests;

public class SmoothMouseCurveTests
{
    [Theory]
    [InlineData(100, 0xCCCC0)]
    [InlineData(125, 0x100000)]
    [InlineData(150, 0x133330)]
    [InlineData(175, 0x166660)]
    [InlineData(200, 0x199990)]
    public void LinearX_matches_known_values_per_scaling(int scale, int expectedStep)
    {
        var points = SmoothMouseCurve.ToPoints(SmoothMouseCurve.LinearX(scale));

        Assert.Equal(new[] { 0, expectedStep, expectedStep * 2, expectedStep * 3, expectedStep * 4 }, points);
    }

    [Fact]
    public void LinearY_uses_fixed_steps_of_3_5()
    {
        Assert.Equal(new[] { 0, 0x38000, 0x70000, 0xA8000, 0xE0000 }, SmoothMouseCurve.ToPoints(SmoothMouseCurve.LinearY()));
    }

    [Fact]
    public void Curve_layout_is_five_little_endian_8_byte_points()
    {
        var bytes = SmoothMouseCurve.LinearX(100);

        Assert.Equal(40, bytes.Length);
        Assert.Equal(new byte[] { 0xC0, 0xCC, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00 }, bytes[8..16]);
    }
}
