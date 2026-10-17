namespace AccSwap.Core.Platforms;

/// <summary>
/// Common contract for every supported game launcher platform.
/// Implementations must not hardcode real installation paths: paths come
/// from injectable path providers so tests can use simulated folders.
/// </summary>
public interface IPlatform
{
    /// <summary>Stable platform identifier, e.g. "steam", "epic".</summary>
    string Id { get; }

    /// <summary>Human-readable platform name.</summary>
    string Name { get; }

    /// <summary>True if the launcher installation was detected.</summary>
    bool IsInstalled { get; }

    /// <summary>Launcher path AccSwap is using (or null when not installed). Exposed so the UI can show/override it.</summary>
    string? DetectedPath { get; }

    /// <summary>Raised with short human-readable progress during long operations.</summary>
    event Action<string>? StatusChanged;

    Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken ct = default);

    /// <summary>Saves the current launcher session as a new (or updated) account snapshot.</summary>
    Task<Account> CaptureCurrentSessionAsync(string? displayName = null, CancellationToken ct = default);

    /// <summary>Closes the launcher, restores the target account snapshot and relaunches.</summary>
    Task SwitchAsync(string accountId, CancellationToken ct = default);

    /// <summary>Ends the current session and opens the launcher login so a new account can be used.</summary>
    Task AddNewLoginAsync(CancellationToken ct = default);

    /// <summary>Ends the current session without relaunching.</summary>
    Task LogoutAsync(CancellationToken ct = default);

    Task DeleteAccountAsync(string accountId, CancellationToken ct = default);

    Task RenameAccountAsync(string accountId, string newName, CancellationToken ct = default);
}
