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
    public void RemoveAcceleration_applies_one_to_one()
    {
        _service.RemoveAcceleration();

        Assert.True(_store.Settings.IsOneToOne);
        Assert.Equal(SmoothMouseCurve.LinearX(125), _store.Settings.SmoothMouseXCurve);
        Assert.True(_service.IsFullyFixed);
    }

    [Fact]
    public void Restore_brings_back_original_settings()
    {
        _service.RemoveAcceleration();
        _service.Restore();

        Assert.Equal(Accelerated, _store.Settings);
        Assert.False(_service.HasBackup);
    }

    [Fact]
    public void Applying_twice_keeps_the_original_backup()
    {
        _service.RemoveAcceleration();
        _service.RemoveAcceleration();
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
        _service.RemoveAcceleration();
        _store.Settings = _store.Settings with { Acceleration = 1 };

        Assert.True(_service.EnsureFixed());
        Assert.True(_service.IsFullyFixed);
    }

    [Fact]
    public void EnsureFixed_returns_false_when_already_fixed()
    {
        _service.RemoveAcceleration();

        Assert.False(_service.EnsureFixed());
    }

    [Fact]
    public void Reapplying_does_not_overwrite_the_original_backup()
    {
        _service.RemoveAcceleration();
        _store.Settings = _store.Settings with { Speed = 3 };
        _service.EnsureFixed();
        _service.Restore();

        Assert.Equal(Accelerated, _store.Settings);
    }

    private sealed class FakeStore : IMouseSettingsStore
    {
        public required MouseSettings Settings { get; set; }

        public MouseSettings Read() => Settings;

        public void Write(MouseSettings settings) => Settings = settings;

        public int GetDisplayScalePercent() => 125;
    }
}
