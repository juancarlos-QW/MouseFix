using Microsoft.Win32;

namespace MouseFix.Core;

/// <summary>"Start with Windows" via the per-user Run key (no admin needed).</summary>
public static class AutoStart
{
    public const string MinimizedArgument = "--minimized";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MouseFix";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Enable(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{exePath}\" {MinimizedArgument}");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>The exe that starts with Windows, or null when "start with Windows" is off.</summary>
    public static string? RegisteredExe
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string command ? ExeFromCommand(command) : null;
        }
    }

    /// <summary>The exe path from a Run command such as <c>"C:\x\MouseFix.exe" --minimized</c>.</summary>
    public static string ExeFromCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 0 ? command[1..end] : command.Trim('"');
        }
        int space = command.IndexOf(" --", StringComparison.Ordinal);
        return space > 0 ? command[..space] : command;
    }

    /// <summary>
    /// True when "start with Windows" opens another MouseFix that is older than this one or no longer exists,
    /// so this copy should take its place. A newer copy, or this same exe, is left alone.
    /// </summary>
    /// <param name="versionOf">Version of an exe on disk, or null if the file doesn't exist.</param>
    public static bool ShouldPointHere(string? registeredExe, string currentExe, Version currentVersion, Func<string, Version?> versionOf)
    {
        if (registeredExe is null || string.Equals(Path.GetFullPath(registeredExe), Path.GetFullPath(currentExe), StringComparison.OrdinalIgnoreCase))
            return false;
        return versionOf(registeredExe) is not Version registered || registered < currentVersion;
    }
}
