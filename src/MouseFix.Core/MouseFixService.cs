namespace MouseFix.Core;

public sealed class MouseFixService
{
    private readonly IMouseSettingsStore _store;
    private readonly BackupStore _backup;
    private readonly Func<int> _targetSpeed;

    /// <param name="targetSpeed">Pointer speed the user chose (1-20). Defaults to 10, the only exact 1:1 value.</param>
    public MouseFixService(IMouseSettingsStore store, BackupStore backup, Func<int>? targetSpeed = null)
    {
        _store = store;
        _backup = backup;
        _targetSpeed = targetSpeed ?? (() => MouseSettings.OneToOneSpeed);
    }

    public MouseSettings Current => _store.Read();

    public bool HasBackup => _backup.Exists;

    /// <summary>The user's settings from before the first fix, if a backup exists.</summary>
    public MouseSettings? Original => _backup.Load();

    public int TargetSpeed => Math.Clamp(_targetSpeed(), 1, 20);

    /// <summary>
    /// Settings with no acceleration at the chosen pointer speed. At speed 10 this is raw 1:1 movement;
    /// other speeds scale every movement by the same factor, so they never add acceleration.
    /// </summary>
    public MouseSettings BuildTarget() => new(
        Acceleration: 0,
        Threshold1: 0,
        Threshold2: 0,
        Speed: TargetSpeed,
        SmoothMouseXCurve: SmoothMouseCurve.LinearX(Math.Max(100, _store.GetDisplayScalePercent())),
        SmoothMouseYCurve: SmoothMouseCurve.LinearY());

    public bool IsFullyFixed => Current.Equals(BuildTarget());

    /// <summary>Writes the given settings as-is, without touching the backup (used by tests that change speed temporarily).</summary>
    public void Apply(MouseSettings settings) => _store.Write(settings);

    /// <summary>Applies the target settings. The first call saves the original settings; later calls keep that backup.</summary>
    public void RemoveAcceleration()
    {
        if (!_backup.Exists)
            _backup.Save(_store.Read());

        _store.Write(BuildTarget());
    }

    /// <summary>
    /// Re-applies the target settings if the user has applied the fix before and something changed it since.
    /// Returns true when settings had to be re-applied.
    /// </summary>
    public bool EnsureFixed()
    {
        if (!_backup.Exists)
            return false;

        var target = BuildTarget();
        if (_store.Read().Equals(target))
            return false;

        _store.Write(target);
        return true;
    }

    /// <summary>Restores the backed-up settings, or Windows defaults when there is no backup.</summary>
    public void Restore()
    {
        var original = _backup.Load() ?? new MouseSettings(
            Acceleration: 1,
            Threshold1: 6,
            Threshold2: 10,
            Speed: MouseSettings.OneToOneSpeed,
            SmoothMouseCurve.WindowsDefaultX,
            SmoothMouseCurve.WindowsDefaultY);

        _store.Write(original);
        _backup.Delete();
    }
}
