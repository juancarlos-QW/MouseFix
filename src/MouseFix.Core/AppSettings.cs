using System.Text.Json;

namespace MouseFix.Core;

/// <summary>User preferences for MouseFix itself (not the Windows mouse settings).</summary>
public sealed class AppSettings
{
    /// <summary>Re-apply 1:1 automatically when another program turns acceleration back on.</summary>
    public bool AutoReapply { get; set; } = true;

    /// <summary>Pointer speed to keep (1-20). Acceleration stays off at any speed; only 10 is exact 1:1.</summary>
    public int PointerSpeed { get; set; } = MouseSettings.OneToOneSpeed;

    /// <summary>Off by default (the point of MouseFix); if the user turns it on, it is kept on.</summary>
    public bool AccelerationEnabled { get; set; }

    /// <summary>Lines per wheel notch, or null to leave Windows' value alone (until the user changes it here).</summary>
    public int? ScrollLines { get; set; }

    /// <summary>Per-program speeds; the first running one wins, otherwise <see cref="PointerSpeed"/> is used.</summary>
    public List<SpeedProfile> Profiles { get; set; } = [];

    /// <summary>Turn game mode (the "MouseFix Jogo" power plan) on while an emulator runs, and back off when it closes.</summary>
    public bool GameModeWithEmulator { get; set; } = true;

    /// <summary>Id of the "MouseFix Jogo" power plan once created.</summary>
    public string? GamePowerPlan { get; set; }

    /// <summary>The power plan that was active before game mode, to go back to.</summary>
    public string? PowerPlanBeforeGame { get; set; }

    /// <summary>True when game mode is on because an emulator opened (it turns off again when the emulator closes).</summary>
    public bool GameModeAutomatic { get; set; }

    /// <summary>Check GitHub for a newer release at most once a day.</summary>
    public bool CheckForUpdates { get; set; } = true;

    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>Newest release seen on GitHub (kept so the banner shows again after a restart).</summary>
    public string? AvailableUpdateVersion { get; set; }

    public string? AvailableUpdateUrl { get; set; }

    /// <summary>Version already announced in the tray, so each update is only announced once.</summary>
    public string? NotifiedUpdateVersion { get; set; }

    /// <summary>
    /// Result of the emulator test: true if the emulator's aim follows the Windows cursor
    /// (so Windows pointer settings affect the game), false if it reads the sensor directly, null if not tested.
    /// </summary>
    public bool? EmulatorUsesWindowsCursor { get; set; }

    /// <summary>The emulator's aim sensitivity before MouseFix adjusted it for 1:1, so it is only adjusted once.</summary>
    public double? EmulatorSensitivityBeforeAdjust { get; set; }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseFix", "settings.json");

    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable settings file should not keep the app from starting.
            return new();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
