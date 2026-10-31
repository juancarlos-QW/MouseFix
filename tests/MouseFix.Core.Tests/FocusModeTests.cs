using MouseFix.Core;
using Xunit;

namespace MouseFix.Core.Tests;

public class FocusModeTests
{
    private const string Windows = @"C:\Windows";

    [Theory]
    [InlineData("Code", @"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe")]
    [InlineData("msedge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")]
    [InlineData("EpicGamesLauncher", @"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe")]
    public void OrdinaryProgramsAreSlowedDown(string name, string exe) =>
        Assert.True(FocusMode.MayThrottle(name, exe, Windows));

    [Theory]
    [InlineData("explorer", @"C:\Windows\explorer.exe")]
    [InlineData("ctfmon", @"C:\Windows\System32\ctfmon.exe")]
    [InlineData("SearchHost", @"C:\WINDOWS\SystemApps\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\SearchHost.exe")]
    public void WindowsItselfIsLeftAlone(string name, string exe) =>
        Assert.False(FocusMode.MayThrottle(name, exe, Windows + @"\"));

    [Theory]
    [InlineData("HD-Player")]
    [InlineData("BstkSVC")]
    [InlineData("dnplayer")]
    [InlineData("RazerAppEngine")]
    [InlineData("Discord")]
    [InlineData("obs64")]
    [InlineData("AutoHotkey64")]
    [InlineData("voicemeeter8x64")]
    [InlineData("MouseFix")]
    public void EmulatorMouseVoiceAndStreamingAreLeftAlone(string name) =>
        Assert.False(FocusMode.MayThrottle(name, $@"C:\Program Files\X\{name}.exe", Windows));

    [Fact]
    public void UnknownPathIsLeftAlone() =>
        Assert.False(FocusMode.MayThrottle("something", "", Windows));

    [Fact]
    public void ClosableAppKeysAreUnique() =>
        Assert.Equal(FocusMode.ClosableApps.Count, FocusMode.ClosableApps.Select(a => a.Key).Distinct().Count());

    [Fact]
    public void DefaultsAreClosableApps() =>
        Assert.All(FocusMode.DefaultCloseApps, key => Assert.Contains(FocusMode.ClosableApps, a => a.Key == key));

    [Theory]
    [InlineData(1, "1 programa")]
    [InlineData(41, "41 programas")]
    public void CountsPrograms(int count, string text) =>
        Assert.Equal(text, FocusMode.Programs(count));

    [Theory]
    [InlineData(350L << 20, "350 MB")]
    [InlineData(1288490189L, "1,2 GB")]
    public void FormatsMemory(long bytes, string text) =>
        Assert.Equal(text, FocusMode.FormatBytes(bytes));
}
