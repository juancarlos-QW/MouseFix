using MouseFix.Core;
using Xunit;

namespace MouseFix.Core.Tests;

public class OtherEmulatorsTests
{
    [Fact]
    public void FindsKnownEmulatorsByInstalledName()
    {
        var found = OtherEmulators.Match(["LDPlayer9", "MuMuPlayer-12.0", "Google Chrome", "Gameloop"]);
        Assert.Equal(["LDPlayer", "MuMu Player", "GameLoop"], found);
    }

    [Fact]
    public void OldGameLoopNameCountsOnce()
    {
        var found = OtherEmulators.Match(["Tencent Gaming Buddy", "GameLoop"]);
        Assert.Equal(["GameLoop"], found);
    }

    [Fact]
    public void IgnoresBlueStacksAndOtherPrograms()
    {
        Assert.Empty(OtherEmulators.Match(["BlueStacks 5", "MSI App Player", "Razer Synapse"]));
    }
}
