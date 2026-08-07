using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerHelper.Services;

public readonly record struct UpdateInfo(Version LatestVersion, string ReleaseUrl);

/// <summary>
/// Checks GitHub's Releases API for a newer tagged version than the one currently running.
/// Best-effort by design: no network, GitHub down, rate-limited, or a malformed response
/// should ever surface as an app error - it just means no update info this time.
/// </summary>
public sealed class UpdateCheckService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/dara-oladapo/power-helper/releases/latest";

    private static readonly HttpClient HttpClient = BuildClient();

    public async Task<UpdateInfo?> CheckForUpdateAsync(Version currentVersion)
    {
        try
        {
            var json = await HttpClient.GetStringAsync(LatestReleaseUrl);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release?.TagName is null)
            {
                return null;
            }

            var versionText = release.TagName.StartsWith('v') ? release.TagName[1..] : release.TagName;
            if (!Version.TryParse(versionText, out var latestVersion))
            {
                return null;
            }

            return latestVersion > currentVersion
                ? new UpdateInfo(latestVersion, release.HtmlUrl ?? "https://github.com/dara-oladapo/power-helper/releases")
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static HttpClient BuildClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub's API rejects requests with no User-Agent header.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerHelper-UpdateCheck");
        return client;
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
