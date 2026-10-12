using System.Security.Cryptography;
using GLAS.Core.RegistryAccess;
using System.Text;
using GLAS.Core.Files;
using GLAS.Core.Security;
using GLAS.Core.Snapshots;
using Xunit;

namespace GLAS.Core.Tests;

public class SnapshotStoreTests : IDisposable
{
    private readonly TempDir _work = new();
    private readonly TempDir _backup = new();
    private readonly InMemoryRegistryService _registry = new();

    [Fact]
    public async Task SaveAndList_RoundTripsMetaAndFiles()
    {
        string storeRoot = _work.Combine("store");
        var store = new SnapshotStore(storeRoot);

        string sourceFile = _work.Combine("session.ini");
        await File.WriteAllTextAsync(sourceFile, "session content");
        var meta = new AccountMeta
        {
            Id = "acc1",
            PlatformId = "epic",
            Username = "main",
            DisplayName = "Cuenta main",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await store.SaveAccountAsync(meta, new Dictionary<string, string> { ["session.ini"] = sourceFile });

        IReadOnlyList<AccountMeta> accounts = await store.ListAccountsAsync("epic");
        Assert.Single(accounts);
        Assert.Equal("main", accounts[0].Username);
        Assert.Equal("Cuenta main", accounts[0].DisplayName);

        byte[] content = await store.ReadAccountFileAsync("epic", "acc1", "session.ini");
        Assert.Equal("session content", Encoding.UTF8.GetString(content));
    }

    [Fact]
    public async Task RenameDelete_ModifyStore()
    {
        var store = new SnapshotStore(_work.Combine("store"));
        await store.SaveAccountBytesAsync(Meta("acc1"), new Dictionary<string, byte[]>());

        await store.RenameAsync("epic", "acc1", "nuevo nombre");
        Assert.Equal("nuevo nombre", (await store.ListAccountsAsync("epic")).Single().DisplayName);

        store.DeleteAccount("epic", "acc1");
        Assert.Empty(await store.ListAccountsAsync("epic"));
    }

    [Fact]
    public async Task ProtectedStore_RoundTripsThroughEncryption()
    {
        var store = new SnapshotStore(_work.Combine("store"), new PasswordFileProtector("secreto"));
        await store.SaveAccountBytesAsync(
            Meta("acc1"),
            new Dictionary<string, byte[]> { ["session.ini"] = Encoding.UTF8.GetBytes("plain payload") });

        // At rest it is encrypted.
        string storedPath = Path.Combine(_work.Combine("store"), "epic", "acc1", "session.ini");
        byte[] raw = await File.ReadAllBytesAsync(storedPath);
        Assert.True(SnapshotEncryption.IsEncrypted(raw));
        Assert.DoesNotContain("plain payload", Encoding.UTF8.GetString(raw));

        // Through the store it reads back as plain.
        byte[] content = await store.ReadAccountFileAsync("epic", "acc1", "session.ini");
        Assert.Equal("plain payload", Encoding.UTF8.GetString(content));
    }

    [Fact]
    public async Task SwitchTransaction_CommitThenComplete_LeavesChanges()
    {
        string target = _work.Combine("live.ini");
        await File.WriteAllTextAsync(target, "original");

        await using (var tx = new SwitchTransaction(_backup.Path, _registry))
        {
            tx.StageFileWrite(target, Encoding.UTF8.GetBytes("nuevo"));
            await tx.CommitAsync();
            tx.Complete();
        }

        Assert.Equal("nuevo", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task SwitchTransaction_FailureAfterCommit_RestoresOriginalFile()
    {
        string target = _work.Combine("live.ini");
        await File.WriteAllTextAsync(target, "original");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var tx = new SwitchTransaction(_backup.Path, _registry);
            tx.StageFileWrite(target, Encoding.UTF8.GetBytes("nuevo"));
            await tx.CommitAsync();
            throw new InvalidOperationException("boom after commit");
        });

        Assert.Equal("original", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task SwitchTransaction_Failure_RestoresRegistryValue_AndRemovesNewValues()
    {
        string key = @"Software\GlasTest";
        _registry.SetStringValue(RegistryHive.CurrentUser, key, "AutoLoginUser", "old");

        string fileTarget = _work.Combine("created.ini");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var tx = new SwitchTransaction(_backup.Path, _registry);
            tx.StageFileWrite(fileTarget, Encoding.UTF8.GetBytes("x"));
            tx.StageRegistryValue(RegistryHive.CurrentUser, key, "AutoLoginUser", "new");
            tx.StageRegistryValue(RegistryHive.CurrentUser, key, "RememberPassword", "1");
            await tx.CommitAsync();
            throw new InvalidOperationException("boom");
        });

        Assert.Equal("old", _registry.GetStringValue(RegistryHive.CurrentUser, key, "AutoLoginUser"));
        Assert.Null(_registry.GetStringValue(RegistryHive.CurrentUser, key, "RememberPassword"));
        Assert.False(File.Exists(fileTarget));
    }

    [Fact]
    public async Task SwitchTransaction_StageDelete_RestoresOnFailure()
    {
        string target = _work.Combine("live.ini");
        await File.WriteAllTextAsync(target, "sesión actual");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var tx = new SwitchTransaction(_backup.Path, _registry);
            tx.StageFileDelete(target);
            await tx.CommitAsync();
            throw new InvalidOperationException("boom");
        });

        Assert.Equal("sesión actual", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public void AtomicFile_Overwrite_LeavesCompleteFile()
    {
        string path = _work.Combine("file.txt");
        AtomicFile.WriteAllText(path, "uno");
        AtomicFile.WriteAllText(path, "dos");
        Assert.Equal("dos", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".glas-tmp"));
    }

    private static AccountMeta Meta(string id) => new()
    {
        Id = id,
        PlatformId = "epic",
        Username = "user" + id,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    public void Dispose() => _work.Dispose();
}

public class SnapshotEncryptionTests
{
    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        byte[] plain = Encoding.UTF8.GetBytes("contenido secreto de la sesión");
        byte[] encrypted = SnapshotEncryption.Encrypt(plain, "contraseña");

        Assert.True(SnapshotEncryption.IsEncrypted(encrypted));
        Assert.NotEqual(plain, encrypted);
        byte[] decrypted = SnapshotEncryption.Decrypt(encrypted, "contraseña");
        Assert.Equal("contenido secreto de la sesión", Encoding.UTF8.GetString(decrypted));
    }

    [Fact]
    public void Decrypt_WrongPassword_Throws()
    {
        byte[] encrypted = SnapshotEncryption.Encrypt("secreto"u8.ToArray(), "correcta");
        Assert.ThrowsAny<CryptographicException>(() => SnapshotEncryption.Decrypt(encrypted, "incorrecta"));
    }

    [Fact]
    public void IsEncrypted_PlainData_ReturnsFalse()
    {
        Assert.False(SnapshotEncryption.IsEncrypted("[Portal]\nData=123\n"u8.ToArray()));
    }
}
