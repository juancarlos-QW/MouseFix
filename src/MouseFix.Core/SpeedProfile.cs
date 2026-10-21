namespace MouseFix.Core;

/// <summary>Pointer speed to use while a given program is running (acceleration always stays off).</summary>
/// <param name="ProcessName">Process name without ".exe", e.g. "HD-Player".</param>
public sealed record SpeedProfile(string ProcessName, string DisplayName, int Speed);

public static class SpeedProfiles
{
    /// <summary>Friendly names for programs people are likely to add.</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownPrograms =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HD-Player"] = "MSI App Player / BlueStacks",
            ["dnplayer"] = "LDPlayer",
            ["MuMuPlayer"] = "MuMu Player",
            ["AndroidEmulatorEn"] = "GameLoop",
            ["AndroidEmulatorEx"] = "GameLoop",
        };

    /// <summary>"HD-Player.exe", " hd-player " → "HD-Player" style name used by Process.GetProcessesByName.</summary>
    public static string NormalizeProcessName(string name)
    {
        name = name.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>The first profile whose program is running (list order = priority), or null for the default speed.</summary>
    public static SpeedProfile? FindActive(IEnumerable<SpeedProfile> profiles, Func<string, bool> isRunning) =>
        profiles.FirstOrDefault(p => isRunning(p.ProcessName));
}
