using GLAS.Core.Steam;
using Xunit;

namespace GLAS.Core.Tests;

public class LoginUsersFileTests
{
    [Fact]
    public void MostRecentUser_ReturnsActiveAccount()
    {
        LoginUsersFile file = LoginUsersFile.Parse(Samples.LoginUsersVdf());
        SteamUserEntry? current = file.MostRecentUser();

        Assert.NotNull(current);
        Assert.Equal(Samples.SteamIdA, current.SteamId);
        Assert.Equal("userA", current.AccountName);
    }

    [Fact]
    public void SetActive_MarksTargetAndClearsOthers()
    {
        LoginUsersFile file = LoginUsersFile.Parse(Samples.LoginUsersVdf());
        file.SetActive(Samples.SteamIdB);

        IReadOnlyList<SteamUserEntry> users = file.Users();
        Assert.False(users.First(u => u.SteamId == Samples.SteamIdA).IsMostRecent);
        Assert.True(users.First(u => u.SteamId == Samples.SteamIdB).IsMostRecent);
        Assert.True(users.First(u => u.SteamId == Samples.SteamIdB).RemembersPassword);

        // Still parses after modification.
        LoginUsersFile reparsed = LoginUsersFile.Parse(file.ToVdfText());
        Assert.Equal(Samples.SteamIdB, reparsed.MostRecentUser()!.SteamId);
    }

    [Fact]
    public void ClearActive_RemovesAllFlags()
    {
        LoginUsersFile file = LoginUsersFile.Parse(Samples.LoginUsersVdf());
        file.ClearActive();

        Assert.All(file.Users(), u => Assert.False(u.IsMostRecent));
        Assert.All(file.Users(), u => Assert.False(u.RemembersPassword));
    }

    [Fact]
    public void Parse_EmptyUsersNode_IsValid()
    {
        LoginUsersFile file = LoginUsersFile.Parse("\"users\"\n{\n}\n");
        Assert.Empty(file.Users());
        Assert.Null(file.MostRecentUser());
    }

    [Fact]
    public void ActiveUser_FallsBackToAutoLogin_WithoutMostRecent()
    {
        LoginUsersFile file = LoginUsersFile.Parse(Samples.SingleUserLoginUsersVdf());

        Assert.Null(file.MostRecentUser());
        Assert.Equal(Samples.SteamIdA, file.ActiveUser()!.SteamId);
        Assert.True(file.ActiveUser()!.IsAutoLogin);
    }

    [Fact]
    public void ActiveUser_SingleEntryWithoutFlags_StillDetected()
    {
        string vdf = string.Join('\n',
            "\"users\"",
            "{",
            $"\"{Samples.SteamIdB}\"",
            "{",
            "\"AccountName\"\t\"userB\"",
            "}",
            "}");
        LoginUsersFile file = LoginUsersFile.Parse(vdf);

        Assert.Equal(Samples.SteamIdB, file.ActiveUser()!.SteamId);
    }
}
