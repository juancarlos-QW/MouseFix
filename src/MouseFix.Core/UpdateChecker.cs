using System.Net.Http.Headers;
using System.Text.Json;

namespace MouseFix.Core;

/// <param name="Url">The release page (fallback when installing in the app isn't possible).</param>
/// <param name="Assets">Downloadable files of the release, by file name.</param>
public sealed record UpdateInfo(Version Version, string Url, IReadOnlyDictionary<string, string>? Assets = null)
{
    /// <summary>The installer and its checksum file, if the release has them.</summary>
    public (string File, string Checksum)? Installer => Pair(n => n.StartsWith("MouseFix-Setup-", StringComparison.OrdinalIgnoreCase));

    /// <summary>The portable exe and its checksum file, if the release has them.</summary>
    public (string File, string Checksum)? Portable => Pair(n => n.Equals("MouseFix.exe", StringComparison.OrdinalIgnoreCase));

    private (string, string)? Pair(Func<string, bool> isFile)
    {
        if (Assets is null)
            return null;
        var file = Assets.Keys.FirstOrDefault(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && isFile(n));
        if (file is null || !Assets.TryGetValue(file + ".sha256", out var checksum))
            return null;
        return (Assets[file], checksum);
    }
}

/// <summary>Asks GitHub for the latest release. Checking and downloading updates are the only network access MouseFix makes.</summary>
public static class UpdateChecker
{
    private const string DefaultLatestReleaseApi = "https://api.github.com/repos/juancarlos-QW/MouseFix/releases/latest";

    /// <summary>The GitHub API address. MOUSEFIX_UPDATE_API can point it at a local test server.</summary>
    public static string LatestReleaseApi =>
        Environment.GetEnvironmentVariable("MOUSEFIX_UPDATE_API") is { Length: > 0 } custom ? custom : DefaultLatestReleaseApi;

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
            if (version is null)
                return null;

            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in list.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name) && asset.TryGetProperty("browser_download_url", out var download) &&
                        name.GetString() is { } n && download.GetString() is { } d)
                        assets[n] = d;
                }
            }
            return new UpdateInfo(version, url.GetString() ?? "", assets);
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
