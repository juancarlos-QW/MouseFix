using Microsoft.Win32;

namespace MouseFix.Core;

/// <summary>
/// Emulators other than BlueStacks, found by their entry in Windows' installed programs list.
/// MouseFix handles Windows for them (acceleration, speed, profiles, game mode), but not their own
/// key-mapping files, whose format couldn't be confirmed.
/// </summary>
public static class OtherEmulators
{
    // Text in the installed program's name → name shown to the user.
    private static readonly (string Match, string Name)[] Known =
    [
        ("LDPlayer", "LDPlayer"),
        ("MuMu", "MuMu Player"),
        ("GameLoop", "GameLoop"),
        ("Tencent Gaming Buddy", "GameLoop"),
    ];

    private static readonly string[] UninstallKeys =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
        @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    /// <summary>Names of the known emulators among the given installed program names, without repeats.</summary>
    public static IReadOnlyList<string> Match(IEnumerable<string> installedNames) =>
        Known.Where(k => installedNames.Any(n => n.Contains(k.Match, StringComparison.OrdinalIgnoreCase)))
            .Select(k => k.Name)
            .Distinct()
            .ToList();

    public static IReadOnlyList<string> FindInstalled() => Match(InstalledProgramNames());

    private static IEnumerable<string> InstalledProgramNames()
    {
        var names = new List<string>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var path in UninstallKeys)
            {
                try
                {
                    using var key = hive.OpenSubKey(path);
                    if (key is null)
                        continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        using var app = key.OpenSubKey(sub);
                        if (app?.GetValue("DisplayName") is string name)
                            names.Add(name);
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // A key we can't read just means one less place to look.
                }
            }
        }
        return names;
    }
}
