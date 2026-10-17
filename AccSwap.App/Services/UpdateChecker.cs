using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace AccSwap.App.Services;

/// <summary>
/// GitHub Releases access: GET /repos/{owner}/{name}/releases/latest.
/// All failures (offline, rate limit, repo without releases) collapse to
/// "no update info" instead of bothering the user.
/// </summary>
public static class UpdateChecker
{
    public sealed record Asset(string Name, string DownloadUrl);

    public sealed record ReleaseInfo(
        string Tag,
        string Name,
        string HtmlUrl,
        string Body,
        IReadOnlyList<Asset> Assets);

    /// <summary>The latest published release, or null when it cannot be determined.</summary>
    public static async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"AccSwap/{AppInfo.Version}");
            string json = await http.GetStringAsync(
                $"https://api.github.com/repos/{AppInfo.RepoOwner}/{AppInfo.RepoName}/releases/latest");
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (tag.Length == 0)
            {
                return null;
            }

            var assets = new List<Asset>();
            if (root.TryGetProperty("assets", out JsonElement assetsElement))
            {
                foreach (JsonElement asset in assetsElement.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? "";
                    string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    if (name.Length > 0 && url.Length > 0)
                    {
                        assets.Add(new Asset(name, url));
                    }
                }
            }

            return new ReleaseInfo(
                Tag: tag,
                Name: root.GetProperty("name").GetString() ?? tag,
                HtmlUrl: root.GetProperty("html_url").GetString() ?? AppInfo.ReleasesUrl,
                Body: root.TryGetProperty("body", out JsonElement body) ? body.GetString() ?? "" : "",
                Assets: assets);
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

    /// <summary>The release's portable exe asset, if it carries one.</summary>
    public static Asset? FindExeAsset(ReleaseInfo release) =>
        release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>Downloads an asset to %TEMP%\AccSwap.Update, returning the file path.</summary>
    public static async Task<string> DownloadAssetAsync(Asset asset, CancellationToken ct = default)
    {
        string dir = Path.Combine(Path.GetTempPath(), "AccSwap.Update");
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, asset.Name);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"AccSwap/{AppInfo.Version}");
        await using Stream source = await http.GetStreamAsync(asset.DownloadUrl, ct);
        using Stream output = File.Create(target);
        await source.CopyToAsync(output, ct);
        return target;
    }
}
