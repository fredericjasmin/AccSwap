namespace AccSwap.App.Services;

/// <summary>
/// Single source of truth for the app version and repository. The version
/// itself lives in AccSwap.App.csproj (the Version property) so it lands on
/// the compiled exe metadata automatically; bump it there only.
/// </summary>
public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";

    public const string RepoOwner = "fredericjasmin";

    public const string RepoName = "AccSwap";

    /// <summary>The name a self-update renames the published exe to.</summary>
    public const string StableExeName = "AccSwap.exe";

    public const string ReleasesUrl = "https://github.com/" + RepoOwner + "/" + RepoName + "/releases";
}
