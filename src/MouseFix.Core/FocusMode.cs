namespace MouseFix.Core;

/// <summary>A background app that focus mode may close while the emulator runs, and how to close and reopen it.</summary>
/// <param name="Key">Saved in the settings when the user picks it.</param>
/// <param name="ProcessName">Process name without ".exe".</param>
/// <param name="CloseArguments">Arguments that ask the app to close itself, or null when it has to be ended.</param>
/// <param name="ReopenArguments">Arguments that reopen it in the background, as it starts with Windows.</param>
public sealed record ClosableApp(string Key, string DisplayName, string ProcessName, string? CloseArguments, string ReopenArguments);

/// <summary>
/// L.T("Modo foco", "Focus mode"): while an emulator runs, the other programs get less of the processor (below-normal
/// priority and Windows' efficiency mode) and give back memory they aren't using, and apps the user picked are closed.
/// When the emulator closes, everything goes back. Nothing is closed that the user didn't pick.
/// </summary>
public static class FocusMode
{
    /// <summary>Apps that only run in the background and come back the same after reopening.</summary>
    public static readonly IReadOnlyList<ClosableApp> ClosableApps =
    [
        new("OneDrive", "OneDrive", "OneDrive", "/shutdown", "/background"),
        new("Epic", "Epic Games Launcher", "EpicGamesLauncher", null, "-silent"),
        new("Steam", "Steam", "steam", "-shutdown", "-silent"),
    ];

    public static IReadOnlyList<string> DefaultCloseApps => ["OneDrive", "Epic"];

    // Never slowed down: the emulator and its helpers, mouse software, macros and controller mappers (input must not
    // lag), voice chat, music, streaming and recording (a call must not break up during the game), graphics and
    // audio tools, and MouseFix itself.
    private static readonly string[] ProtectedPrefixes =
    [
        "HD-", "Bstk", "BlueStacks", "dnplayer", "Ld", "MuMu", "Nemu", "AndroidEmulator", "aow_exe", "QMEmulator",
        "Razer", "lghub", "LCore", "iCUE", "SteelSeries", "NGenuity", "Swarm", "ArmouryCrate", "Bloody",
        "AutoHotkey", "DS4Windows", "JoyToKey", "x360ce",
        "Discord", "TeamSpeak", "ts3client", "Mumble", "Spotify", "obs", "Streamlabs", "XSplit", "Medal", "bdcam",
        "nvcontainer", "NVIDIA", "nvsphelper", "RadeonSoftware", "AMDRS", "amdow",
        "Rtk", "Nahimic", "Dolby", "voicemeeter",
        "MouseFix", "Taskmgr",
    ];

    /// <summary>
    /// True when focus mode may lower this program. Programs inside the Windows folder (explorer, the taskbar,
    /// typing and input services) and the protected ones above are left alone.
    /// </summary>
    public static bool MayThrottle(string processName, string exePath, string windowsFolder)
    {
        if (string.IsNullOrEmpty(exePath))
            return false;
        string windows = windowsFolder.TrimEnd('\\') + '\\';
        if (exePath.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
            return false;
        return !ProtectedPrefixes.Any(p => processName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"1 programa" / "41 programas" for the history.</summary>
    public static string Programs(int count) => count == 1 ? L.T("1 programa", "1 program") : L.T($"{count} programas", $"{count} programs");

    /// <summary>"1,2 GB" / "350 MB" for the history.</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1L << 30)
            return $"{bytes >> 20} MB";
        string gb = (bytes / (double)(1L << 30)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return $"{L.T(gb.Replace('.', ','), gb)} GB";
    }
}
