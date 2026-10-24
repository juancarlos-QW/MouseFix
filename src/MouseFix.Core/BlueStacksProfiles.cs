using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MouseFix.Core;

/// <summary>Aim ("Pan") control of one key-mapping scheme.</summary>
public sealed record EmulatorAim(string SchemeName, bool Selected, double Sensitivity, double SensitivityRatioY, bool MouseAcceleration);

public sealed record EmulatorGameProfile(string EmulatorName, string GameName, string FilePath, IReadOnlyList<EmulatorAim> Aims)
{
    public bool AnyAcceleration => Aims.Any(a => a.MouseAcceleration);

    /// <summary>The aim control of the scheme the user has selected, if any.</summary>
    public EmulatorAim? SelectedAim => Aims.FirstOrDefault(a => a.Selected) ?? Aims.FirstOrDefault();
}

/// <summary>
/// Reads and fixes the key-mapping files of BlueStacks 5 and its rebrands (e.g. MSI App Player).
/// Each game has a JSON file in Engine\UserData\InputMapper\UserFiles; aim controls are of type "Pan".
/// </summary>
public static partial class BlueStacksProfiles
{
    public static readonly IReadOnlyDictionary<string, string> Games = new Dictionary<string, string>
    {
        ["com.dts.freefireth"] = "Free Fire",
        ["com.dts.freefiremax"] = "Free Fire MAX",
    };

    private const string BuiltInPath = @"Engine\UserData\InputMapper";
    private const string UserFilesPath = @"Engine\UserData\InputMapper\UserFiles";
    public const string BackupSuffix = ".mousefix.bak";

    public static string DefaultRoot => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    /// <summary>The emulator overwrites its mapping files, so they must only be edited while it is closed.</summary>
    public static bool IsEmulatorRunning => Process.GetProcessesByName("HD-Player").Length > 0;

    public static IReadOnlyList<EmulatorGameProfile> Find(string root)
    {
        var profiles = new List<EmulatorGameProfile>();
        if (!Directory.Exists(root))
            return profiles;

        foreach (var dir in Directory.EnumerateDirectories(root, "BlueStacks*"))
        {
            foreach (var (package, gameName) in Games)
            {
                // The user's customised mapping wins; without one, the emulator uses its built-in mapping.
                var file = Path.Combine(dir, UserFilesPath, package + ".cfg");
                if (!File.Exists(file))
                    file = Path.Combine(dir, BuiltInPath, package + ".cfg");
                if (!File.Exists(file))
                    continue;
                try
                {
                    profiles.Add(Load(EmulatorName(Path.GetFileName(dir)), gameName, file));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    // Unreadable or mid-write file: skip it rather than failing the whole scan.
                }
            }
        }
        return profiles;
    }

    public static EmulatorGameProfile Load(string emulatorName, string gameName, string file)
    {
        var root = JsonNode.Parse(File.ReadAllText(file));
        var aims = new List<EmulatorAim>();

        foreach (var scheme in root?["ControlSchemes"]?.AsArray() ?? [])
        {
            foreach (var control in scheme?["GameControls"]?.AsArray() ?? [])
            {
                if ((string?)control?["Type"] != "Pan")
                    continue;
                aims.Add(new EmulatorAim(
                    (string?)scheme!["Name"] ?? "?",
                    (bool?)scheme["Selected"] ?? false,
                    (double?)control!["Sensitivity"] ?? 1,
                    (double?)control["SensitivityRatioY"] ?? 1,
                    (bool?)control["MouseAcceleration"] ?? false));
            }
        }
        return new EmulatorGameProfile(emulatorName, gameName, file, aims);
    }

    /// <summary>
    /// Turns off "Mouse acceleration" in every aim control. Edits only those values in the text,
    /// so the rest of the file stays byte-for-byte identical. Keeps a backup of the original file.
    /// Returns how many controls were changed.
    /// </summary>
    public static int DisableMouseAcceleration(string file)
    {
        var text = File.ReadAllText(file);
        int changed = 0;
        var updated = MouseAccelerationTrue().Replace(text, m =>
        {
            changed++;
            return m.Groups[1].Value + "false";
        });

        if (changed == 0)
            return 0;

        var backup = file + BackupSuffix;
        if (!File.Exists(backup))
            File.Copy(file, backup);
        File.WriteAllText(file, updated);
        return changed;
    }

    /// <summary>
    /// Sets the aim sensitivity (X) of the selected scheme. The file is rewritten as JSON (same data,
    /// different spacing), so a backup of the original is kept. Returns how many aim controls were changed.
    /// </summary>
    public static int SetSelectedSensitivity(string file, double sensitivity)
    {
        var root = JsonNode.Parse(File.ReadAllText(file)) ?? throw new InvalidDataException("Arquivo de controles vazio.");
        var scheme = (root["ControlSchemes"]?.AsArray() ?? [])
            .FirstOrDefault(s => (bool?)s?["Selected"] == true)
            ?? throw new InvalidDataException("Nenhum esquema de controles selecionado.");

        int changed = 0;
        foreach (var control in scheme["GameControls"]?.AsArray() ?? [])
        {
            if ((string?)control?["Type"] != "Pan")
                continue;
            control!["Sensitivity"] = Math.Round(sensitivity, 2);
            changed++;
        }
        if (changed == 0)
            return 0;

        var backup = file + BackupSuffix;
        if (!File.Exists(backup))
            File.Copy(file, backup);
        File.WriteAllText(file, root.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            // Keep accented and non-Latin strings readable instead of \u escapes.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        return changed;
    }

    private static string EmulatorName(string dirName) => dirName switch
    {
        "BlueStacks_nxt" => "BlueStacks 5",
        _ when dirName.StartsWith("BlueStacks_msi", StringComparison.OrdinalIgnoreCase) => "MSI App Player",
        _ => dirName,
    };

    [GeneratedRegex(@"(""MouseAcceleration""\s*:\s*)true")]
    private static partial Regex MouseAccelerationTrue();
}
