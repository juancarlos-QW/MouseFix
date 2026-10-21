using System.Net.Http.Headers;
using System.Text.Json;

namespace MouseFix.Core;

public sealed record UpdateInfo(Version Version, string Url);

/// <summary>Asks GitHub for the latest release. This is the only network access MouseFix makes.</summary>
public static class UpdateChecker
{
    public const string LatestReleaseApi = "https://api.github.com/repos/juancarlos-QW/MouseFix/releases/latest";
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>Returns the latest release if it is newer than <paramref name="current"/>; null otherwise or on any failure.</summary>
    public static async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // GitHub's API rejects requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MouseFix", current.ToString(3)));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await http.GetAsync(LatestReleaseApi, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var latest = Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return latest is not null && IsNewer(latest.Version, current) ? latest : null;
    }

    /// <summary>Reads tag_name ("v1.2.0") and html_url from a GitHub release JSON.</summary>
    public static UpdateInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tag) || !root.TryGetProperty("html_url", out var url))
                return null;
            var version = ParseVersion(tag.GetString());
            return version is null ? null : new UpdateInfo(version, url.GetString() ?? "");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Version? ParseVersion(string? text)
    {
        text = text?.Trim().TrimStart('v', 'V');
        // Drop build metadata or pre-release suffixes ("1.2.0+abc", "1.2.0-beta").
        int cut = text?.IndexOfAny(['+', '-']) ?? -1;
        if (cut >= 0)
            text = text![..cut];
        return Version.TryParse(text, out var v) ? Normalize(v) : null;
    }

    /// <summary>Compares major.minor.patch only, so 1.1.0 and the assembly's 1.1.0.0 count as equal.</summary>
    public static bool IsNewer(Version latest, Version current) => Normalize(latest) > Normalize(current);

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
