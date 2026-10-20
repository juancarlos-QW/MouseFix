using MouseFix.Core;

namespace MouseFix.Core.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mousefix-settings-{Guid.NewGuid()}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        Assert.True(AppSettings.Load(_path).AutoReapply);
    }

    [Fact]
    public void Settings_round_trip()
    {
        new AppSettings { AutoReapply = false }.Save(_path);

        Assert.False(AppSettings.Load(_path).AutoReapply);
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        File.WriteAllText(_path, "{ not json");

        Assert.True(AppSettings.Load(_path).AutoReapply);
    }
}
