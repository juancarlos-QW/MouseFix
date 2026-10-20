using System.Diagnostics;

namespace MouseFix.Core;

/// <summary>
/// Detects running mouse vendor software. These can apply acceleration or "enhanced" movement in the driver,
/// before Windows sees the input, so neither the Windows settings nor the precision test can see it.
/// </summary>
public static class MouseSoftware
{
    private static readonly (string Process, string Name)[] Known =
    [
        ("RazerAppEngine", "Razer Synapse"),
        ("Razer Synapse 3", "Razer Synapse"),
        ("Razer Synapse Service", "Razer Synapse"),
        ("lghub", "Logitech G HUB"),
        ("LCore", "Logitech Gaming Software"),
        ("iCUE", "Corsair iCUE"),
        ("SteelSeriesGG", "SteelSeries GG"),
        ("SteelSeriesEngine", "SteelSeries Engine"),
        ("NGenuity2", "HyperX NGENUITY"),
        ("Swarm", "Roccat Swarm"),
        ("ArmouryCrate", "ASUS Armoury Crate"),
        ("Bloody7", "Bloody"),
    ];

    public static IReadOnlyList<string> FindRunning() =>
        Known.Where(k => Process.GetProcessesByName(k.Process).Length > 0)
             .Select(k => k.Name)
             .Distinct()
             .ToList();
}
