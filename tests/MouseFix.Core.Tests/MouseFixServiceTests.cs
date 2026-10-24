using MouseFix.Core;

namespace MouseFix.Core.Tests;

public sealed class MouseFixServiceTests : IDisposable
{
    private static readonly MouseSettings Accelerated =
        new(1, 6, 10, 14, SmoothMouseCurve.WindowsDefaultX, SmoothMouseCurve.WindowsDefaultY);

    private readonly string _backupPath = Path.Combine(Path.GetTempPath(), $"mousefix-test-{Guid.NewGuid()}.json");
    private readonly FakeStore _store = new() { Settings = Accelerated };
    private readonly MouseFixService _service;

    public MouseFixServiceTests() => _service = new MouseFixService(_store, new BackupStore(_backupPath));

    public void Dispose() => File.Delete(_backupPath);

    [Fact]
    public void ApplyTarget_by_default_applies_one_to_one()
    {
        _service.ApplyTarget();

        Assert.True(_store.Settings.IsOneToOne);
        Assert.Equal(SmoothMouseCurve.LinearX(125), _store.Settings.SmoothMouseXCurve);
        Assert.True(_service.IsFullyFixed);
    }

    [Fact]
    public void Restore_brings_back_original_settings()
    {
        _service.ApplyTarget();
        _service.Restore();

        Assert.Equal(Accelerated, _store.Settings);
        Assert.False(_service.HasBackup);
    }

    [Fact]
    public void Applying_twice_keeps_the_original_backup()
    {
        _service.ApplyTarget();
        _service.ApplyTarget();
        _service.Restore();

        Assert.Equal(Accelerated, _store.Settings);
    }

    [Fact]
    public void EnsureFixed_does_nothing_before_the_fix_was_ever_applied()
    {
        Assert.False(_service.EnsureFixed());
        Assert.Equal(Accelerated, _store.Settings);
    }

    [Fact]
    public void EnsureFixed_reapplies_when_another_program_turns_acceleration_back_on()
    {
        _service.ApplyTarget();
        _store.Settings = _store.Settings with { Acceleration = 1 };

        Assert.True(_service.EnsureFixed());
        Assert.True(_service.IsFullyFixed);
    }

    [Fact]
    public void EnsureFixed_returns_false_when_already_fixed()
    {
        _service.ApplyTarget();

        Assert.False(_service.EnsureFixed());
    }

    [Fact]
    public void Reapplying_does_not_overwrite_the_original_backup()
    {
        _service.ApplyTarget();
        _store.Settings = _store.Settings with { Speed = 3 };
        _service.EnsureFixed();
        _service.Restore();

        Assert.Equal(Accelerated, _store.Settings);
    }

    [Fact]
    public void Chosen_speed_is_applied_with_acceleration_off()
    {
        var service = new MouseFixService(_store, new BackupStore(_backupPath), () => new TargetOptions(6));
        service.ApplyTarget();

        Assert.Equal(6, _store.Settings.Speed);
        Assert.False(_store.Settings.IsAccelerationEnabled);
        Assert.Equal((0, 0), (_store.Settings.Threshold1, _store.Settings.Threshold2));
        Assert.True(service.IsFullyFixed);
    }

    [Fact]
    public void Protection_keeps_the_chosen_speed_instead_of_forcing_10()
    {
        var service = new MouseFixService(_store, new BackupStore(_backupPath), () => new TargetOptions(6));
        service.ApplyTarget();

        Assert.False(service.EnsureFixed());

        _store.Settings = _store.Settings with { Acceleration = 1, Speed = 10 };
        Assert.True(service.EnsureFixed());
        Assert.Equal(6, _store.Settings.Speed);
        Assert.False(_store.Settings.IsAccelerationEnabled);
    }

    [Fact]
    public void Changing_the_chosen_speed_takes_effect_on_the_next_check()
    {
        int speed = 10;
        var service = new MouseFixService(_store, new BackupStore(_backupPath), () => new TargetOptions(speed));
        service.ApplyTarget();

        speed = 14;
        Assert.True(service.EnsureFixed());
        Assert.Equal(14, _store.Settings.Speed);
    }

    [Fact]
    public void Protection_curves_match_the_chosen_speed()
    {
        var service = new MouseFixService(_store, new BackupStore(_backupPath), () => new TargetOptions(6));

        Assert.Equal(SmoothMouseCurve.LinearY(SmoothMouseCurve.GainForSpeed(6)), service.BuildTarget().SmoothMouseYCurve);
        Assert.Equal(SmoothMouseCurve.LinearY(), _service.BuildTarget().SmoothMouseYCurve);
    }

    [Fact]
    public void Curves_written_by_older_versions_are_replaced_by_the_protection()
    {
        _service.ApplyTarget();
        // What 1.2.1 and earlier wrote: Y steps 16x too small.
        _store.Settings = _store.Settings with { SmoothMouseYCurve = SmoothMouseCurve.FromPoints(0, 0x38000, 0x70000, 0xA8000, 0xE0000) };

        Assert.True(_service.EnsureFixed());
        Assert.Equal(SmoothMouseCurve.LinearY(), _store.Settings.SmoothMouseYCurve);
    }

    [Fact]
    public void Acceleration_turned_on_by_the_user_uses_windows_defaults_and_is_kept()
    {
        var service = new MouseFixService(_store, new BackupStore(_backupPath), () => new TargetOptions(10, AccelerationEnabled: true));
        service.ApplyTarget();

        Assert.True(_store.Settings.IsAccelerationEnabled);
        Assert.Equal((6, 10), (_store.Settings.Threshold1, _store.Settings.Threshold2));
        // Linear curves would cancel the acceleration, so Windows' own curves are used.
        Assert.Equal(SmoothMouseCurve.WindowsDefaultX, _store.Settings.SmoothMouseXCurve);

        _store.Settings = _store.Settings with { Acceleration = 0 };
        Assert.True(service.EnsureFixed());
        Assert.True(_store.Settings.IsAccelerationEnabled);
    }

    [Fact]
    public void Scroll_lines_are_left_alone_until_the_user_chooses_them()
    {
        _store.Settings = _store.Settings with { ScrollLines = 5 };
        _service.ApplyTarget();
        Assert.Equal(5, _store.Settings.ScrollLines);

        var service = new MouseFixService(_store, new BackupStore(_backupPath), () => new TargetOptions(ScrollLines: 3));
        Assert.True(service.EnsureFixed());
        Assert.Equal(3, _store.Settings.ScrollLines);
    }

    [Fact]
    public void Scroll_lines_are_kept_within_limits()
    {
        var service = new MouseFixService(_store, new BackupStore(_backupPath), () => new TargetOptions(ScrollLines: 500));

        Assert.Equal(TargetOptions.MaxScrollLines, service.BuildTarget().ScrollLines);
    }

    [Fact]
    public void Old_backups_without_scroll_lines_do_not_change_scrolling_on_restore()
    {
        File.WriteAllText(_backupPath, """
            { "Acceleration": 1, "Threshold1": 6, "Threshold2": 10, "Speed": 6,
              "SmoothMouseXCurve": "AAAAAAAAAAAVbgAAAAAAAABAAQAAAAAAKdwDAAAAAAAAACgAAAAAAA==",
              "SmoothMouseYCurve": "AAAAAAAAAAD9EQEAAAAAAAAkBAAAAAAAAPwSAAAAAAAAwLsBAAAAAA==" }
            """);
        _store.Settings = _store.Settings with { ScrollLines = 7 };
        _service.Restore();

        Assert.Equal(6, _store.Settings.Speed);
        Assert.Null(_store.Settings.ScrollLines);
    }

    [Theory]
    [InlineData(0.5, 6)]
    [InlineData(1.0, 10)]
    [InlineData(0.875, 9)]
    [InlineData(0.44, 6)]
    public void SpeedForMultiplier_finds_the_closest_speed(double multiplier, int expected)
    {
        Assert.Equal(expected, MouseSettings.SpeedForMultiplier(multiplier));
    }

    private sealed class FakeStore : IMouseSettingsStore
    {
        public required MouseSettings Settings { get; set; }

        public MouseSettings Read() => Settings;

        public void Write(MouseSettings settings) => Settings = settings;

        public int GetDisplayScalePercent() => 125;
    }
}
