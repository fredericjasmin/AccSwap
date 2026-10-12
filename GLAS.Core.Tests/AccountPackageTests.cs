using System.Security.Cryptography;
using System.Text;
using GLAS.Core.ExportImport;
using GLAS.Core.Snapshots;
using Xunit;

namespace GLAS.Core.Tests;

public class AccountPackageTests : IDisposable
{
    private readonly TempDir _work = new();

    [Fact]
    public async Task ExportImport_RoundTripsAccounts()
    {
        var source = new SnapshotStore(_work.Combine("store1"));
        var target = new SnapshotStore(_work.Combine("store2"));

        await source.SaveAccountBytesAsync(
            Meta("acc1", "userA"),
            new Dictionary<string, byte[]> { ["session.ini"] = Encoding.UTF8.GetBytes("tokenAAA") });
        await source.SaveAccountBytesAsync(
            Meta("acc2", "userB"),
            new Dictionary<string, byte[]> { ["session.ini"] = Encoding.UTF8.GetBytes("tokenBBB") });

        string package = _work.Combine("backup.glas");
        await AccountPackage.ExportAsync(source, ["epic"], package, password: null);

        AccountPackage.ImportResult imported = await AccountPackage.ImportAsync(target, package, password: null);

        Assert.Equal(2, imported.Added);
        Assert.Equal(0, imported.Skipped);
        IReadOnlyList<AccountMeta> accounts = await target.ListAccountsAsync("epic");
        Assert.Equal(2, accounts.Count);
        Assert.Equal(
            "tokenAAA",
            Encoding.UTF8.GetString(await target.ReadAccountFileAsync("epic", "acc1", "session.ini")));
    }

    [Fact]
    public async Task ExportImport_WithPassword_RoundTrips()
    {
        var source = new SnapshotStore(_work.Combine("store1"));
        var target = new SnapshotStore(_work.Combine("store2"));
        await source.SaveAccountBytesAsync(
            Meta("acc1", "userA"),
            new Dictionary<string, byte[]> { ["session.ini"] = Encoding.UTF8.GetBytes("tokenAAA") });

        string package = _work.Combine("backup.glas");
        await AccountPackage.ExportAsync(source, ["epic"], package, password: "secreta");

        // Package content is not plain text.
        byte[] raw = await File.ReadAllBytesAsync(package);
        Assert.DoesNotContain("tokenAAA"u8, raw);

        AccountPackage.ImportResult imported = await AccountPackage.ImportAsync(target, package, password: "secreta");
        Assert.Equal(1, imported.Added);
        Assert.Equal(
            "tokenAAA",
            Encoding.UTF8.GetString(await target.ReadAccountFileAsync("epic", "acc1", "session.ini")));
    }

    [Fact]
    public async Task Import_WrongPassword_Throws()
    {
        var source = new SnapshotStore(_work.Combine("store1"));
        await source.SaveAccountBytesAsync(
            Meta("acc1", "userA"),
            new Dictionary<string, byte[]> { ["session.ini"] = Encoding.UTF8.GetBytes("tokenAAA") });

        string package = _work.Combine("backup.glas");
        await AccountPackage.ExportAsync(source, ["epic"], package, password: "correcta");

        var target = new SnapshotStore(_work.Combine("store2"));
        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => AccountPackage.ImportAsync(target, package, password: "incorrecta"));
    }

    [Fact]
    public async Task Import_NeverTouchesExistingAccounts_OnlyAddsNewOnes()
    {
        // Source PC has two accounts.
        var source = new SnapshotStore(_work.Combine("store1"));
        await source.SaveAccountBytesAsync(
            Meta("acc1", "userA"),
            new Dictionary<string, byte[]> { ["session.ini"] = Encoding.UTF8.GetBytes("tokenAAA") });
        await source.SaveAccountBytesAsync(
            Meta("acc2", "userB"),
            new Dictionary<string, byte[]> { ["session.ini"] = Encoding.UTF8.GetBytes("tokenBBB") });

        string package = _work.Combine("backup.glas");
        await AccountPackage.ExportAsync(source, ["epic"], package, password: null);

        // Target PC already has a local account and an older copy of acc1.
        var target = new SnapshotStore(_work.Combine("store2"));
        await target.SaveAccountBytesAsync(
            Meta("acc-local", "userLocal"),
            new Dictionary<string, byte[]> { ["session.ini"] = Encoding.UTF8.GetBytes("tokenLOCAL") });
        await target.SaveAccountBytesAsync(
            Meta("acc1", "userA-local"),
            new Dictionary<string, byte[]> { ["session.ini"] = Encoding.UTF8.GetBytes("tokenOLD") });

        AccountPackage.ImportResult result = await AccountPackage.ImportAsync(target, package, password: null);

        // The local accounts survive untouched; only acc2 is new.
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Skipped);
        IReadOnlyList<AccountMeta> accounts = await target.ListAccountsAsync("epic");
        Assert.Equal(3, accounts.Count);
        Assert.Equal(
            "tokenOLD",
            Encoding.UTF8.GetString(await target.ReadAccountFileAsync("epic", "acc1", "session.ini")));
        Assert.Equal(
            "tokenLOCAL",
            Encoding.UTF8.GetString(await target.ReadAccountFileAsync("epic", "acc-local", "session.ini")));
    }

    [Fact]
    public async Task Import_SamePackageTwice_DoesNotDuplicateOrOverwrite()
    {
        var source = new SnapshotStore(_work.Combine("store1"));
        await source.SaveAccountBytesAsync(
            Meta("acc1", "userA"),
            new Dictionary<string, byte[]> { ["session.ini"] = Encoding.UTF8.GetBytes("tokenAAA") });

        string package = _work.Combine("backup.glas");
        await AccountPackage.ExportAsync(source, ["epic"], package, password: null);

        var target = new SnapshotStore(_work.Combine("store2"));
        await AccountPackage.ImportAsync(target, package, password: null);

        // Replace the local copy with different content, then re-import:
        // the local version must win, not the package one.
        await target.SaveAccountBytesAsync(
            Meta("acc1", "userA-local"),
            new Dictionary<string, byte[]> { ["session.ini"] = Encoding.UTF8.GetBytes("tokenLOCAL") });
        AccountPackage.ImportResult result = await AccountPackage.ImportAsync(target, package, password: null);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Single(await target.ListAccountsAsync("epic"));
        Assert.Equal(
            "tokenLOCAL",
            Encoding.UTF8.GetString(await target.ReadAccountFileAsync("epic", "acc1", "session.ini")));
    }

    private static AccountMeta Meta(string id, string username) => new()
    {
        Id = id,
        PlatformId = "epic",
        Username = username,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    public void Dispose() => _work.Dispose();
}
