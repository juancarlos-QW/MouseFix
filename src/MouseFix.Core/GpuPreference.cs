using Microsoft.Win32;

namespace MouseFix.Core;

/// <summary>
/// Windows' per-app graphics preference (Settings › System › Display › Graphics). On laptops with two GPUs,
/// "high performance" makes the emulator run on the dedicated GPU instead of the weaker integrated one.
/// Harmless on PCs with a single GPU.
/// </summary>
public static class GpuPreference
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string HighPerformance = "GpuPreference=2;";

    /// <summary>
    /// Sets "high performance" for the program, unless the user already chose something for it.
    /// Returns true when it was changed.
    /// </summary>
    public static bool EnsureHighPerformance(string exePath, RegistryKey? root = null)
    {
        using var key = (root ?? Registry.CurrentUser).CreateSubKey(KeyPath);
        if (key.GetValue(exePath) is string existing && existing.Length > 0)
            return false;
        key.SetValue(exePath, HighPerformance, RegistryValueKind.String);
        return true;
    }
}
