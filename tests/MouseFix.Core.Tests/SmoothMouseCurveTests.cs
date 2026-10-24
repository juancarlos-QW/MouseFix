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
    public void LinearY_matches_the_markc_fix_byte_for_byte()
    {
        // "SmoothMouseYCurve"=hex:00,00,00,00,00,00,00,00, 00,00,38,00,00,00,00,00, 00,00,70,00,00,00,00,00,
        //                         00,00,A8,00,00,00,00,00, 00,00,E0,00,00,00,00,00
        byte[] markC =
        [
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0x38, 0, 0, 0, 0, 0,
            0, 0, 0x70, 0, 0, 0, 0, 0,
            0, 0, 0xA8, 0, 0, 0, 0, 0,
            0, 0, 0xE0, 0, 0, 0, 0, 0,
        ];

        Assert.Equal(markC, SmoothMouseCurve.LinearY());
    }

    // Measured on Windows 11 (see the 1.2.2 fix): with acceleration on the pointer moves speed/10, with it off it
    // follows the speed table. The gain makes both the same.
    [Theory]
    [InlineData(10, 1.0)]
    [InlineData(6, 0.5 / 0.6)]
    [InlineData(7, 0.625 / 0.7)]
    [InlineData(14, 2.0 / 1.4)]
    [InlineData(20, 3.5 / 2.0)]
    public void Gain_makes_acceleration_on_feel_like_off(int speed, double expected)
    {
        Assert.Equal(expected, SmoothMouseCurve.GainForSpeed(speed), 6);
    }

    [Fact]
    public void LinearY_gain_scales_every_point()
    {
        var points = SmoothMouseCurve.ToPoints(SmoothMouseCurve.LinearY(0.5));

        Assert.Equal(new[] { 0, 0x1C0000, 0x380000, 0x540000, 0x700000 }, points);
    }

    [Fact]
    public void Curve_layout_is_five_little_endian_8_byte_points()
    {
        var bytes = SmoothMouseCurve.LinearX(100);

        Assert.Equal(40, bytes.Length);
        Assert.Equal(new byte[] { 0xC0, 0xCC, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00 }, bytes[8..16]);
    }
}
