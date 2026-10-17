namespace AccSwap.App.Services;

/// <summary>Sidebar glyph and brand accent per platform id.</summary>
public static class PlatformVisuals
{
    public static (string Glyph, string Accent) For(string platformId) => platformId switch
    {
        "steam" => ("S", "#66C0F4"),
        "epic" => ("E", "#F5F5F5"),
        "riot" => ("R", "#D32937"),
        "ubisoft" => ("U", "#0072FF"),
        "tarkov" => ("T", "#9AA0A6"),
        "battlenet" => ("B", "#00AEFF"),
        "ea" => ("A", "#FF454F"),
        "gog" => ("G", "#B49CF0"),
        "discord" => ("D", "#5865F2"),
        "roblox" => ("X", "#E2231A"),
        _ => ("?", "#9AA0A6"),
    };
}
