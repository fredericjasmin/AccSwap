using GLAS.Core.Steam.Vdf;
using Xunit;

namespace GLAS.Core.Tests;

public class VdfDocumentTests
{
    [Fact]
    public void RoundTrip_PreservesValues()
    {
        VdfNode root = VdfDocument.Parse(Samples.LoginUsersVdf());
        string text = VdfDocument.Serialize(root);
        VdfNode reparsed = VdfDocument.Parse(text);

        VdfNode users = reparsed.GetNode("users")!;
        Assert.NotNull(users);
        Assert.Equal(2, users.Entries.Count);
        Assert.Equal("userA", (users.FirstOrNull(Samples.SteamIdA)!.Value as VdfNode)!.GetString("AccountName"));
    }

    [Fact]
    public void RoundTrip_PreservesUnknownFields()
    {
        string vdf = """
            "users"
            {
            	"7656"
            	{
            		"AccountName"	"a"
            		"FutureField"	"keep me"
            	}
            }
            """;

        VdfNode root = VdfDocument.Parse(vdf);
        string text = VdfDocument.Serialize(root);
        Assert.Contains("FutureField", text);
        Assert.Contains("keep me", text);
    }

    [Fact]
    public void Parse_HandlesCommentsAndEscapes()
    {
        string vdf = """
            // top comment
            "users" /* inline */
            {
            	"7656"
            	{
            		"AccountName"	"escaped \"quote\" and \\ slash"
            	}
            }
            """;

        VdfNode root = VdfDocument.Parse(vdf);
        Assert.Equal("escaped \"quote\" and \\ slash", root.GetNode("users")!.GetNode("7656")!.GetString("AccountName"));
    }

    [Fact]
    public void Parse_Malformed_Throws()
    {
        Assert.Throws<VdfFormatException>(() => VdfDocument.Parse("\"users\" { \"7656\" \"name\" "));
        Assert.Throws<VdfFormatException>(() => VdfDocument.Parse("}"));
        Assert.Throws<VdfFormatException>(() => VdfDocument.Parse("\"key\" \"unterminated"));
    }

    [Fact]
    public void Set_ReplacesExistingKey()
    {
        VdfNode root = VdfDocument.Parse(Samples.LoginUsersVdf());
        VdfNode userA = root.GetNode("users")!.GetNode(Samples.SteamIdA)!;
        Assert.Equal("1", userA.GetString("MostRecent"));

        userA.Set("MostRecent", "0");
        Assert.Equal("0", userA.GetString("MostRecent"));
        Assert.Single(userA.Entries, e => e.Key == "MostRecent");
    }
}
