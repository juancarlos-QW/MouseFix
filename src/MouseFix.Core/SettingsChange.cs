namespace MouseFix.Core;

/// <summary>Human-readable summary of how Windows' current mouse settings differ from MouseFix's, for the log.</summary>
public static class SettingsChange
{
    public static string Describe(MouseSettings current, MouseSettings expected)
    {
        var parts = new List<string>();
        if (current.Acceleration != expected.Acceleration)
            parts.Add($"aceleração {(current.IsAccelerationEnabled ? "ligada" : "desligada")}");
        if (current.Speed != expected.Speed)
            parts.Add($"velocidade {current.Speed}/20 (era {expected.Speed}/20)");
        if (current.Threshold1 != expected.Threshold1 || current.Threshold2 != expected.Threshold2)
            parts.Add($"limites de aceleração {current.Threshold1}/{current.Threshold2}");
        if (current.ScrollLines != expected.ScrollLines)
            parts.Add($"rolagem {current.ScrollLines?.ToString() ?? "?"} linha(s) (era {expected.ScrollLines?.ToString() ?? "?"})");
        if (!current.SmoothMouseXCurve.AsSpan().SequenceEqual(expected.SmoothMouseXCurve) ||
            !current.SmoothMouseYCurve.AsSpan().SequenceEqual(expected.SmoothMouseYCurve))
            parts.Add("curvas de aceleração");
        return parts.Count == 0 ? "nada" : string.Join(", ", parts);
    }
}
