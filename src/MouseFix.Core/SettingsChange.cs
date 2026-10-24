namespace MouseFix.Core;

/// <summary>Human-readable summary of how Windows' current mouse settings differ from the expected ones.</summary>
public static class SettingsChange
{
    /// <param name="expectedWord">
    /// How to label the expected values: "era" (was) for outside changes, "escolhida" (chosen) when an apply didn't take,
    /// "original" for a restore.
    /// </param>
    public static string Describe(MouseSettings current, MouseSettings expected, string expectedWord = "era")
    {
        var parts = new List<string>();
        if (current.Acceleration != expected.Acceleration)
            parts.Add($"aceleração {(current.IsAccelerationEnabled ? "ligada" : "desligada")}");
        if (current.Speed != expected.Speed)
            parts.Add($"velocidade {current.Speed}/20 ({expectedWord} {expected.Speed}/20)");
        if (current.Threshold1 != expected.Threshold1 || current.Threshold2 != expected.Threshold2)
            parts.Add($"limites de aceleração {current.Threshold1}/{current.Threshold2}");
        if (current.ScrollLines != expected.ScrollLines)
            parts.Add($"rolagem {current.ScrollLines?.ToString() ?? "?"} linha(s) ({expectedWord} {expected.ScrollLines?.ToString() ?? "?"})");
        if (!CurvesMatch(current, expected))
            parts.Add("curvas de aceleração");
        return parts.Count == 0 ? "nada" : string.Join(", ", parts);
    }

    /// <summary>
    /// True when only the protection curves differ. Programs that change the mouse touch acceleration or speed;
    /// a curves-only difference comes from MouseFix itself (an update with new curves, or a display scaling change).
    /// </summary>
    public static bool OnlyCurvesDiffer(MouseSettings current, MouseSettings expected) =>
        !CurvesMatch(current, expected) &&
        current with { SmoothMouseXCurve = expected.SmoothMouseXCurve, SmoothMouseYCurve = expected.SmoothMouseYCurve } == expected;

    private static bool CurvesMatch(MouseSettings a, MouseSettings b) =>
        a.SmoothMouseXCurve.AsSpan().SequenceEqual(b.SmoothMouseXCurve) &&
        a.SmoothMouseYCurve.AsSpan().SequenceEqual(b.SmoothMouseYCurve);
}
