namespace MouseFix.Core;

/// <summary>What the user chose in MouseFix; the protection keeps Windows at these settings.</summary>
/// <param name="Speed">Pointer speed 1-20 (10 = exact 1:1).</param>
/// <param name="AccelerationEnabled">Off by default. When the user turns it on, MouseFix keeps it on.</param>
/// <param name="ScrollLines">Lines per wheel notch, or null to leave whatever Windows has.</param>
public sealed record TargetOptions(int Speed = MouseSettings.OneToOneSpeed, bool AccelerationEnabled = false, int? ScrollLines = null)
{
    public const int MinScrollLines = 1;
    public const int MaxScrollLines = 20;
}
