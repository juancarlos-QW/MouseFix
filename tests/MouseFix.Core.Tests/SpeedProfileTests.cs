using MouseFix.Core;

namespace MouseFix.Core.Tests;

public sealed class SpeedProfileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mousefix-profiles-{Guid.NewGuid()}.json");

    public void Dispose() => File.Delete(_path);

    [Theory]
    [InlineData("HD-Player.exe", "HD-Player")]
    [InlineData("  dnplayer.EXE ", "dnplayer")]
    [InlineData("HD-Player", "HD-Player")]
    public void Process_names_are_normalized(string input, string expected)
    {
        Assert.Equal(expected, SpeedProfiles.NormalizeProcessName(input));
    }

    [Fact]
    public void First_running_program_wins_and_none_means_default()
    {
        var emulator = new SpeedProfile("HD-Player", "MSI App Player", 6);
        var browser = new SpeedProfile("chrome", "Chrome", 12);
        var profiles = new[] { emulator, browser };

        Assert.Equal(emulator, SpeedProfiles.FindActive(profiles, _ => true));
        Assert.Equal(browser, SpeedProfiles.FindActive(profiles, name => name == "chrome"));
        Assert.Null(SpeedProfiles.FindActive(profiles, _ => false));
    }

    [Fact]
    public void Profiles_are_saved_with_the_settings()
    {
        new AppSettings { Profiles = [new SpeedProfile("HD-Player", "MSI App Player", 6)] }.Save(_path);

        Assert.Equal([new SpeedProfile("HD-Player", "MSI App Player", 6)], AppSettings.Load(_path).Profiles);
    }

    [Fact]
    public void Old_settings_files_without_profiles_still_load()
    {
        File.WriteAllText(_path, """{ "AutoReapply": true, "PointerSpeed": 8 }""");

        var settings = AppSettings.Load(_path);
        Assert.Empty(settings.Profiles);
        Assert.Equal(8, settings.PointerSpeed);
        Assert.True(settings.CheckForUpdates);
    }
}
