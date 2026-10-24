namespace MouseFix.Core;

public sealed class MouseFixService
{
    private readonly IMouseSettingsStore _store;
    private readonly BackupStore _backup;
    private readonly Func<TargetOptions> _options;

    /// <param name="options">What the user chose. Defaults to speed 10 (exact 1:1), no acceleration, scrolling untouched.</param>
    public MouseFixService(IMouseSettingsStore store, BackupStore backup, Func<TargetOptions>? options = null)
    {
        _store = store;
        _backup = backup;
        _options = options ?? (() => new TargetOptions());
    }

    public TargetOptions Options => _options();

    public MouseSettings Current => _store.Read();

    public bool HasBackup => _backup.Exists;

    /// <summary>The user's settings from before the first fix, if a backup exists.</summary>
    public MouseSettings? Original => _backup.Load();

    public int TargetSpeed => Math.Clamp(_options().Speed, 1, 20);

    /// <summary>
    /// The settings the user chose. Without acceleration, speed 10 is raw 1:1 movement and other speeds
    /// scale every movement by the same factor. With acceleration on, Windows' own defaults are used,
    /// including its curves (the linear protection curves would cancel the acceleration out).
    /// </summary>
    public MouseSettings BuildTarget()
    {
        var options = _options();
        int? scroll = options.ScrollLines is int lines
            ? Math.Clamp(lines, TargetOptions.MinScrollLines, TargetOptions.MaxScrollLines)
            : _store.Read().ScrollLines;

        return options.AccelerationEnabled
            ? new MouseSettings(1, 6, 10, TargetSpeed, SmoothMouseCurve.WindowsDefaultX, SmoothMouseCurve.WindowsDefaultY, scroll)
            : new MouseSettings(0, 0, 0, TargetSpeed,
                SmoothMouseCurve.LinearX(Math.Max(100, _store.GetDisplayScalePercent())),
                SmoothMouseCurve.LinearY(SmoothMouseCurve.GainForSpeed(TargetSpeed)), scroll);
    }

    public bool IsFullyFixed => Current.Equals(BuildTarget());

    /// <summary>Writes the given settings as-is, without touching the backup (used by tests that change speed temporarily).</summary>
    public void Apply(MouseSettings settings) => _store.Write(settings);

    /// <summary>Applies the user's chosen settings. The first call saves the original settings; later calls keep that backup.</summary>
    public void ApplyTarget()
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
