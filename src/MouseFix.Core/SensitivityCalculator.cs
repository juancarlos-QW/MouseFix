namespace MouseFix.Core;

/// <summary>Suggested Windows speed for a new DPI, and how close it gets to the old feel (1 = identical).</summary>
public sealed record SpeedSuggestion(int Speed, double FeelRatio)
{
    /// <summary>Within 2% of the old feel, which nobody can tell apart.</summary>
    public bool IsExact => Math.Abs(FeelRatio - 1) <= 0.02;
}

/// <summary>
/// Desktop sensitivity numbers from the mouse DPI and the Windows pointer speed, with acceleration off.
/// With acceleration off, each count from the mouse moves the cursor by the speed's multiplier in pixels.
/// </summary>
public static class SensitivityCalculator
{
    public const double CmPerInch = 2.54;

    /// <summary>eDPI: DPI times the Windows multiplier (800 DPI at speed 10 = 800).</summary>
    public static double EffectiveDpi(double dpi, int speed) => dpi * MouseSettings.SpeedMultiplier(speed);

    /// <summary>How many pixels the cursor moves per centimetre of hand movement.</summary>
    public static double PixelsPerCm(double dpi, int speed) => EffectiveDpi(dpi, speed) / CmPerInch;

    /// <summary>Centimetres of hand movement to move the cursor across <paramref name="pixels"/>.</summary>
    public static double CmForPixels(double pixels, double dpi, int speed) => pixels / PixelsPerCm(dpi, speed);

    /// <summary>Windows speed that keeps the cursor moving the same with a new DPI.</summary>
    public static SpeedSuggestion SpeedForNewDpi(double oldDpi, int oldSpeed, double newDpi)
    {
        double target = EffectiveDpi(oldDpi, oldSpeed);
        int speed = MouseSettings.SpeedForMultiplier(target / newDpi);
        return new SpeedSuggestion(speed, EffectiveDpi(newDpi, speed) / target);
    }

    /// <summary>DPI to set in the mouse software to keep the same feel at speed 10 (exact 1:1).</summary>
    public static double DpiForOneToOne(double dpi, int speed) => EffectiveDpi(dpi, speed);
}
