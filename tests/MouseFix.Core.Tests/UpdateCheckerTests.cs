using MouseFix.Core;

namespace MouseFix.Core.Tests;

public class UpdateCheckerTests
{
    [Fact]
    public void Reads_version_and_link_from_a_github_release()
    {
        var info = UpdateChecker.Parse("""
            { "tag_name": "v1.2.0", "html_url": "https://github.com/juancarlos-QW/MouseFix/releases/tag/v1.2.0", "name": "MouseFix 1.2.0" }
            """);

        Assert.Equal(new Version(1, 2, 0), info!.Version);
        Assert.Equal("https://github.com/juancarlos-QW/MouseFix/releases/tag/v1.2.0", info.Url);
    }

    [Fact]
    public void Finds_the_installer_and_portable_files_with_their_checksums()
    {
        var info = UpdateChecker.Parse("""
            { "tag_name": "v1.5.0", "html_url": "https://github.com/x/releases/tag/v1.5.0", "assets": [
              { "name": "MouseFix-Setup-1.5.0.exe", "browser_download_url": "https://github.com/d/setup.exe" },
              { "name": "MouseFix-Setup-1.5.0.exe.sha256", "browser_download_url": "https://github.com/d/setup.sha256" },
              { "name": "MouseFix.exe", "browser_download_url": "https://github.com/d/MouseFix.exe" },
              { "name": "MouseFix.exe.sha256", "browser_download_url": "https://github.com/d/MouseFix.sha256" } ] }
            """)!;

        Assert.Equal(("https://github.com/d/setup.exe", "https://github.com/d/setup.sha256"), info.Installer);
        Assert.Equal(("https://github.com/d/MouseFix.exe", "https://github.com/d/MouseFix.sha256"), info.Portable);
    }

    [Fact]
    public void A_file_without_its_checksum_is_not_offered()
    {
        var info = UpdateChecker.Parse("""
            { "tag_name": "v1.5.0", "html_url": "x", "assets": [ { "name": "MouseFix.exe", "browser_download_url": "y" } ] }
            """)!;

        Assert.Null(info.Portable);
        Assert.Null(info.Installer);
    }

    [Theory]
    [InlineData("84DE1952526CB756427118A0E3705103CEDA807068E3A9F9CD7C46970ACF57AA  MouseFix.exe", true)]
    [InlineData("84de1952526cb756427118a0e3705103ceda807068e3a9f9cd7c46970acf57aa", true)]
    [InlineData("not a hash", false)]
    [InlineData("", false)]
    public void Checksum_files_are_parsed(string text, bool valid)
    {
        Assert.Equal(valid, UpdateDownloader.ParseSha256(text) is not null);
    }

    [Fact]
    public void Hash_check_detects_a_changed_file()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "MouseFix");
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("MouseFix"u8.ToArray()));

            Assert.True(UpdateDownloader.HashMatches(file, hash));
            File.WriteAllText(file, "MouseFix (alterado)");
            Assert.False(UpdateDownloader.HashMatches(file, hash));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("{ \"message\": \"Not Found\" }")]
    [InlineData("{ \"tag_name\": \"latest\", \"html_url\": \"x\" }")]
    [InlineData("not json")]
    public void Unexpected_responses_are_ignored(string json)
    {
        Assert.Null(UpdateChecker.Parse(json));
    }

    [Theory]
    [InlineData("1.2.0", "1.1.0.0", true)]
    [InlineData("1.1.0", "1.1.0.0", false)]
    [InlineData("1.0.9", "1.1.0.0", false)]
    [InlineData("2.0.0", "1.9.9.0", true)]
    public void Only_strictly_newer_versions_count(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(Version.Parse(latest), Version.Parse(current)));
    }

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.2.0+abc123", "1.2.0")]
    [InlineData("V1.3.0-beta", "1.3.0")]
    public void Tags_with_prefix_or_suffix_are_understood(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));
    }
}
