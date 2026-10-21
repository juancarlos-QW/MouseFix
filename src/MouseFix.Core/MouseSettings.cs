namespace MouseFix.Core;

/// <summary>
/// Snapshot of every Windows setting that affects how mouse counts become cursor pixels.
/// </summary>
/// <param name="Acceleration">0 = "Enhance pointer precision" off; 1 = on.</param>
/// <param name="Threshold1">Legacy acceleration threshold (should be 0 when off).</param>
/// <param name="Threshold2">Legacy acceleration threshold (should be 0 when off).</param>
/// <param name="Speed">Pointer speed slider, 1-20. Only 10 is an exact 1:1 multiplier.</param>
/// <param name="SmoothMouseXCurve">40-byte curve used when acceleration is on.</param>
/// <param name="SmoothMouseYCurve">40-byte curve used when acceleration is on.</param>
/// <param name="ScrollLines">Lines per mouse-wheel notch; null when unknown (left unchanged on write).</param>
public sealed record MouseSettings(
    int Acceleration,
    int Threshold1,
    int Threshold2,
    int Speed,
    byte[] SmoothMouseXCurve,
    byte[] SmoothMouseYCurve,
    int? ScrollLines = null)
{
    public const int OneToOneSpeed = 10;

    // Cursor pixels per sensor count for each pointer speed (index = speed 1-20), with acceleration off.
    private static readonly double[] SpeedMultipliers =
        [0, 0.03125, 0.0625, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875, 1, 1.25, 1.5, 1.75, 2, 2.25, 2.5, 2.75, 3, 3.25, 3.5];

    public static double SpeedMultiplier(int speed) => SpeedMultipliers[Math.Clamp(speed, 1, 20)];

    /// <summary>The pointer speed whose multiplier is closest to the given one.</summary>
    public static int SpeedForMultiplier(double multiplier) =>
        Enumerable.Range(1, 20).MinBy(s => Math.Abs(SpeedMultipliers[s] - multiplier));

    public bool IsAccelerationEnabled => Acceleration != 0;

    public bool IsOneToOne => !IsAccelerationEnabled && Speed == OneToOneSpeed;

    public bool Equals(MouseSettings? other) =>
        other is not null &&
        Acceleration == other.Acceleration &&
        Threshold1 == other.Threshold1 &&
        Threshold2 == other.Threshold2 &&
        Speed == other.Speed &&
        ScrollLines == other.ScrollLines &&
        SmoothMouseXCurve.AsSpan().SequenceEqual(other.SmoothMouseXCurve) &&
        SmoothMouseYCurve.AsSpan().SequenceEqual(other.SmoothMouseYCurve);

    public override int GetHashCode() => HashCode.Combine(Acceleration, Threshold1, Threshold2, Speed);
}
