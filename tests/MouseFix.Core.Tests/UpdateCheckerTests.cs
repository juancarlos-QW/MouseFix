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
