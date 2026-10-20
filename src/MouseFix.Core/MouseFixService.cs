namespace MouseFix.Core;

public sealed class MouseFixService
{
    private readonly IMouseSettingsStore _store;
    private readonly BackupStore _backup;

    public MouseFixService(IMouseSettingsStore store, BackupStore backup)
    {
        _store = store;
        _backup = backup;
    }

    public MouseSettings Current => _store.Read();

    public bool HasBackup => _backup.Exists;

    /// <summary>The user's settings from before the first fix, if a backup exists.</summary>
    public MouseSettings? Original => _backup.Load();

    /// <summary>The exact settings that give raw 1:1 movement on this machine.</summary>
    public MouseSettings BuildOneToOne() => new(
        Acceleration: 0,
        Threshold1: 0,
        Threshold2: 0,
        Speed: MouseSettings.OneToOneSpeed,
        SmoothMouseXCurve: SmoothMouseCurve.LinearX(Math.Max(100, _store.GetDisplayScalePercent())),
        SmoothMouseYCurve: SmoothMouseCurve.LinearY());

    public bool IsFullyFixed => Current.Equals(BuildOneToOne());

    /// <summary>Writes the given settings as-is, without touching the backup (used by tests that change speed temporarily).</summary>
    public void Apply(MouseSettings settings) => _store.Write(settings);

    /// <summary>Applies 1:1 settings. The first call saves the original settings; later calls keep that backup.</summary>
    public void RemoveAcceleration()
    {
        if (!_backup.Exists)
            _backup.Save(_store.Read());

        _store.Write(BuildOneToOne());
    }

    /// <summary>
    /// Re-applies 1:1 if the user has applied the fix before and something changed it since.
    /// Returns true when settings had to be re-applied.
    /// </summary>
    public bool EnsureFixed()
    {
        if (!_backup.Exists)
            return false;

        var target = BuildOneToOne();
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
