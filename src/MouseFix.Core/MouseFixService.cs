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
    /// <param name="current">Windows' current settings if already read, to avoid reading them again.</param>
    public MouseSettings BuildTarget(MouseSettings? current = null)
    {
        var options = _options();
        int? scroll = options.ScrollLines is int lines
            ? Math.Clamp(lines, TargetOptions.MinScrollLines, TargetOptions.MaxScrollLines)
            : (current ?? _store.Read()).ScrollLines;

        return options.AccelerationEnabled
            ? new MouseSettings(1, 6, 10, TargetSpeed, SmoothMouseCurve.WindowsDefaultX, SmoothMouseCurve.WindowsDefaultY, scroll)
            : new MouseSettings(0, 0, 0, TargetSpeed,
                SmoothMouseCurve.LinearX(Math.Max(100, _store.GetDisplayScalePercent())),
                SmoothMouseCurve.LinearY(SmoothMouseCurve.GainForSpeed(TargetSpeed)), scroll);
    }

    public bool IsFullyFixed => Current.Equals(BuildTarget());

    /// <summary>Writes the given settings as-is, without touching the backup (used by tests that change speed temporarily).</summary>
    public void Apply(MouseSettings settings) => _store.Write(settings);

    /// <summary>
    /// Applies the user's chosen settings and reads them back. The first call saves the original settings;
    /// later calls keep that backup. Returns null when Windows has exactly the chosen settings, otherwise what didn't take.
    /// </summary>
    public string? ApplyTarget()
    {
        if (!_backup.Exists)
            _backup.Save(_store.Read());

        _store.Write(BuildTarget());
        return VerifyTarget();
    }

    /// <summary>Null when Windows currently has the chosen settings; otherwise a description of what differs.</summary>
    public string? VerifyTarget()
    {
        var current = _store.Read();
        var target = BuildTarget(current);
        return current.Equals(target) ? null : SettingsChange.Describe(current, target, "escolhida");
    }

    /// <summary>
    /// Re-applies the target settings if the user has applied the fix before and something changed it since.
    /// Returns true when settings had to be re-applied.
    /// </summary>
    public bool EnsureFixed()
    {
        if (!_backup.Exists)
            return false;

        var current = _store.Read();
        var target = BuildTarget(current);
        if (current.Equals(target))
            return false;

        _store.Write(target);
        return true;
    }

    /// <summary>
    /// Restores the backed-up settings (or Windows defaults when there is no backup) and reads them back.
    /// Returns null when restored; otherwise what didn't take. Either way MouseFix stops managing the mouse afterwards
    /// (the backup is removed), so the protection never undoes a restore — even a partly failed one.
    /// </summary>
    public string? Restore()
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

        var current = _store.Read();
        // A backup without scroll lines (made by older versions) doesn't touch scrolling, so don't check it.
        var expected = original.ScrollLines is null ? original with { ScrollLines = current.ScrollLines } : original;
        return current.Equals(expected) ? null : SettingsChange.Describe(current, expected, "original");
    }
}
