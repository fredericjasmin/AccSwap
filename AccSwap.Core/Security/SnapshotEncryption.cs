using System.Security.Cryptography;
namespace AccSwap.Core.Security;

/// <summary>
/// Encrypts snapshot payload files at rest with a user password
/// (PBKDF2-SHA256 key derivation, AES-GCM authenticated encryption).
/// </summary>
public interface IFileProtector
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] maybeProtected);
}

public static class SnapshotEncryption
{
    private const string Magic = "ACCENC1";

    public static bool IsEncrypted(byte[] data)
    {
        if (data.Length < Magic.Length)
        {
            return false;
        }

        for (int i = 0; i < Magic.Length; i++)
        {
            if (data[i] != (byte)Magic[i])
            {
                return false;
            }
        }

        return true;
    }

    public static byte[] Encrypt(byte[] plain, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] key = DeriveKey(password, salt);

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        byte[] cipher = new byte[plain.Length + 16];
        aes.Encrypt(nonce, plain, cipher.AsSpan(0, plain.Length), cipher.AsSpan(plain.Length));

        byte[] output = new byte[Magic.Length + salt.Length + nonce.Length + cipher.Length];
        int offset = 0;
        Copy(System.Text.Encoding.ASCII.GetBytes(Magic), output, ref offset);
        Copy(salt, output, ref offset);
        Copy(nonce, output, ref offset);
        Copy(cipher, output, ref offset);
        return output;
    }

    public static byte[] Decrypt(byte[] encrypted, string password)
    {
        int magicLength = Magic.Length;
        if (encrypted.Length < magicLength + 16 + 12 + 16)
        {
            throw new InvalidDataException("Encrypted snapshot is truncated.");
        }

        byte[] salt = new byte[16];
        byte[] nonce = new byte[12];
        Array.Copy(encrypted, magicLength, salt, 0, 16);
        Array.Copy(encrypted, magicLength + 16, nonce, 0, 12);
        byte[] cipher = new byte[encrypted.Length - magicLength - 16 - 12];
        Array.Copy(encrypted, magicLength + 16 + 12, cipher, 0, cipher.Length);

        byte[] key = DeriveKey(password, salt);
        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        byte[] plain = new byte[cipher.Length - 16];
        aes.Decrypt(nonce, cipher.AsSpan(0, plain.Length), cipher.AsSpan(plain.Length), plain);
        return plain;
    }

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations: 100_000, HashAlgorithmName.SHA256, 32);

    private static void Copy(byte[] source, byte[] destination, ref int offset)
    {
        Array.Copy(source, 0, destination, offset, source.Length);
        offset += source.Length;
    }
}

public sealed class PasswordFileProtector(string password) : IFileProtector
{
    public byte[] Protect(byte[] plain) => SnapshotEncryption.Encrypt(plain, password);

    public byte[] Unprotect(byte[] maybeProtected) =>
        SnapshotEncryption.IsEncrypted(maybeProtected)
            ? SnapshotEncryption.Decrypt(maybeProtected, password)
            : maybeProtected;
}
