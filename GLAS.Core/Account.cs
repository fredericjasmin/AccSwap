namespace GLAS.Core;

public sealed class Account
{
    public required string PlatformId { get; init; }

    public required string Id { get; init; }

    /// <summary>Launcher username (e.g. Steam AccountName) or user-provided name.</summary>
    public required string Username { get; init; }

    public string? DisplayName { get; init; }

    public DateTimeOffset? CreatedAtUtc { get; init; }

    /// <summary>Platform-specific identifiers (steamId, session hash...).</summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    public bool IsActive { get; set; }

    public string DisplayLabel => DisplayName ?? Username;
}
