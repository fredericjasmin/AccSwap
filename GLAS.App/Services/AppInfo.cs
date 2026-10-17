namespace GLAS.App.Services;

/// <summary>
/// Single source of truth for the app version and repository. The version
/// itself lives in GLAS.App.csproj (the Version property) so it lands on
/// the compiled exe metadata automatically; bump it there only.
/// </summary>
public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";

    public const string RepoOwner = "fredericjasmin";

    public const string RepoName = "Game-Launcher-Account-Switcher";

    /// <summary>The name a self-update renames the published exe to.</summary>
    public const string StableExeName = "GLAS - Game Launcher Account Switcher.exe";

    public const string ReleasesUrl = "https://github.com/" + RepoOwner + "/" + RepoName + "/releases";
}
