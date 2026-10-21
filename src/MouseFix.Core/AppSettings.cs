using System.Text.Json;

namespace MouseFix.Core;

/// <summary>User preferences for MouseFix itself (not the Windows mouse settings).</summary>
public sealed class AppSettings
{
    /// <summary>Re-apply 1:1 automatically when another program turns acceleration back on.</summary>
    public bool AutoReapply { get; set; } = true;

    /// <summary>Pointer speed to keep (1-20). Acceleration stays off at any speed; only 10 is exact 1:1.</summary>
    public int PointerSpeed { get; set; } = MouseSettings.OneToOneSpeed;

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
        catch (JsonException)
        {
            // A corrupt settings file should not keep the app from starting.
            return new();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
