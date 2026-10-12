using GLAS.Core.Steam.Vdf;

namespace GLAS.Core.Steam;

/// <summary>
/// Typed view over Steam's config/loginusers.vdf:
///
///   "users"
///   {
///     "7656119..."
///     {
///       "AccountName"    "name"
///       "PersonaName"    "display name"
///       "RememberPassword" "1"
///       "MostRecent"     "1"
///       "Timestamp"      "..."
///     }
///   }
/// </summary>
public sealed class LoginUsersFile
{
    private const string UsersKey = "users";

    private readonly VdfNode _root;

    private LoginUsersFile(VdfNode root) => _root = root;

    public static LoginUsersFile Parse(string text)
    {
        VdfNode root = VdfDocument.Parse(text);
        if (root.GetNode(UsersKey) is null)
        {
            // An empty/missing "users" node is valid (logged out of everything).
            root.Add(UsersKey, new VdfNode());
        }

        return new LoginUsersFile(root);
    }

    public static string Serialize(LoginUsersFile file) => VdfDocument.Serialize(file._root);

    public IReadOnlyList<SteamUserEntry> Users()
    {
        var result = new List<SteamUserEntry>();
        foreach (VdfEntry entry in _root.GetNode(UsersKey)!.Entries)
        {
            if (entry.Value is VdfNode userNode)
            {
                result.Add(new SteamUserEntry(
                    SteamId: entry.Key,
                    AccountName: userNode.GetString("AccountName"),
                    PersonaName: userNode.GetString("PersonaName"),
                    IsMostRecent: userNode.GetString("MostRecent") == "1",
                    RemembersPassword: userNode.GetString("RememberPassword") == "1",
                    IsAutoLogin: userNode.GetString("AutoLogin") == "1"));
            }
        }

        return result;
    }

    public SteamUserEntry? MostRecentUser() =>
        Users().FirstOrDefault(u => u.IsMostRecent);

    /// <summary>
    /// The account Steam will sign in automatically. Steam only writes
    /// MostRecent when there is more than one remembered account, so fall
    /// back to AutoLogin, then RememberPassword, then a single-entry file.
    /// </summary>
    public SteamUserEntry? ActiveUser()
    {
        IReadOnlyList<SteamUserEntry> users = Users();
        return users.FirstOrDefault(u => u.IsMostRecent)
            ?? users.FirstOrDefault(u => u.IsAutoLogin)
            ?? users.FirstOrDefault(u => u.RemembersPassword)
            ?? (users.Count == 1 ? users[0] : null);
    }

    /// <summary>
    /// Marks the target SteamID as the auto-login user (MostRecent=1,
    /// RememberPassword=1) and clears those flags on everyone else.
    /// </summary>
    public void SetActive(string steamId)
    {
        foreach (VdfEntry entry in _root.GetNode(UsersKey)!.Entries)
        {
            if (entry.Value is not VdfNode userNode)
            {
                continue;
            }

            bool isTarget = string.Equals(entry.Key, steamId, StringComparison.Ordinal);
            userNode.Set("MostRecent", isTarget ? "1" : "0");
            if (isTarget)
            {
                userNode.Set("RememberPassword", "1");
            }
        }
    }

    /// <summary>Clears auto-login flags on every user (used by logout / add-new-account).</summary>
    public void ClearActive()
    {
        foreach (VdfEntry entry in _root.GetNode(UsersKey)!.Entries)
        {
            if (entry.Value is VdfNode userNode)
            {
                userNode.Set("MostRecent", "0");
                userNode.Set("RememberPassword", "0");
            }
        }
    }

    public string ToVdfText() => VdfDocument.Serialize(_root);
}

public sealed record SteamUserEntry(
    string SteamId,
    string? AccountName,
    string? PersonaName,
    bool IsMostRecent,
    bool RemembersPassword,
    bool IsAutoLogin = false);
