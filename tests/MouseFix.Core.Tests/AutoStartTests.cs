using MouseFix.Core;
using Xunit;

namespace MouseFix.Core.Tests;

public class AutoStartTests
{
    private const string Installed = @"C:\Users\x\AppData\Local\Programs\MouseFix\MouseFix.exe";
    private const string Downloads = @"C:\Users\x\Downloads\MouseFix.exe";
    private static readonly Version Current = new(1, 6, 0);

    [Theory]
    [InlineData("\"C:\\Users\\x\\Downloads\\MouseFix.exe\" --minimized", Downloads)]
    [InlineData("\"C:\\Users\\x\\Downloads\\MouseFix.exe\"", Downloads)]
    [InlineData("C:\\Users\\x\\Downloads\\MouseFix.exe --minimized", Downloads)]
    public void ReadsTheExeFromTheRunCommand(string command, string exe) =>
        Assert.Equal(exe, AutoStart.ExeFromCommand(command));

    [Fact]
    public void OlderCopyStartingWithWindowsIsReplaced() =>
        Assert.True(AutoStart.ShouldPointHere(Downloads, Installed, Current, _ => new Version(1, 0, 0)));

    [Fact]
    public void MissingCopyIsReplaced() =>
        Assert.True(AutoStart.ShouldPointHere(Downloads, Installed, Current, _ => null));

    [Fact]
    public void NewerCopyIsLeftAlone() =>
        Assert.False(AutoStart.ShouldPointHere(Downloads, Installed, Current, _ => new Version(1, 7, 0)));

    [Fact]
    public void SameVersionElsewhereIsLeftAlone() =>
        Assert.False(AutoStart.ShouldPointHere(Downloads, Installed, Current, _ => Current));

    [Fact]
    public void SameExeIsLeftAlone() =>
        Assert.False(AutoStart.ShouldPointHere(Installed.ToUpperInvariant(), Installed, Current, _ => new Version(1, 0, 0)));

    [Fact]
    public void StartWithWindowsOffStaysOff() =>
        Assert.False(AutoStart.ShouldPointHere(null, Installed, Current, _ => null));
}
