using System.Net.Http;
using System.Text.Json;

namespace GLAS.App.Services;

/// <summary>
/// Update detection through GitHub Releases: GET /repos/{owner}/{name}/
/// releases/latest and compare the tag with the running version. All
/// failures (offline, rate limit, repo without releases) collapse to
/// "no update info" instead of bothering the user.
/// </summary>
public static class UpdateChecker
{
    public sealed record ReleaseInfo(string Tag, string Name, string HtmlUrl, string Body);

    /// <summary>The latest published release, or null when it cannot be determined.</summary>
    public static async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"GLAS/{AppInfo.Version}");
            string json = await http.GetStringAsync(
                $"https://api.github.com/repos/{AppInfo.RepoOwner}/{AppInfo.RepoName}/releases/latest");
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (tag.Length == 0)
            {
                return null;
            }

            return new ReleaseInfo(
                Tag: tag,
                Name: root.GetProperty("name").GetString() ?? tag,
                HtmlUrl: root.GetProperty("html_url").GetString() ?? AppInfo.ReleasesUrl,
                Body: root.TryGetProperty("body", out JsonElement body) ? body.GetString() ?? "" : "");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when the release tag is strictly newer than the running version.</summary>
    public static bool IsNewer(string latestTag)
    {
        return Version.TryParse(latestTag.TrimStart('v', 'V'), out Version? latest)
            && Version.TryParse(AppInfo.Version, out Version? current)
            && latest > current;
    }
}
