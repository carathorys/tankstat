using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Tankstat.Application.Backups;
using Tankstat.Domain.Backups;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// The key pair behind a passphrase: Argon2id of the passphrase and a random salt is the X25519 identity, its public key the recipient
/// backups are encrypted for. The server keeps the recipient and the salt (<c>Backups:Encryption</c>) and never the identity, so nothing
/// on the server can read its own backups; <c>tankstat decrypt</c> derives the identity again from the passphrase and the manifest's
/// salt and parameters. Argon2id comes from BouncyCastle (.NET has none).
/// </summary>
public static class BackupKeys
{
    public const int SaltLength = 16;

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltLength);

    /// <summary>The identity (32 bytes) of a passphrase; the same passphrase, salt and parameters always give the same one.</summary>
    public static byte[] Identity(string passphrase, ReadOnlySpan<byte> salt, Argon2idParameters parameters)
    {
        if (passphrase.Length == 0) throw new ArgumentException("The passphrase must not be empty.", nameof(passphrase));
        if (salt.Length < 8) throw new ArgumentException("The salt is too short.", nameof(salt));
        var generator = new Argon2BytesGenerator();
        generator.Init(new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithVersion(Argon2Parameters.Version13)
            .WithSalt(salt.ToArray())
            .WithMemoryAsKB(parameters.MemoryKiB)
            .WithIterations(parameters.Passes)
            .WithParallelism(parameters.Lanes)
            .Build());
        var identity = new byte[AgeKeys.KeyLength];
        generator.GenerateBytes(Encoding.UTF8.GetBytes(passphrase), identity);
        return identity;
    }

    /// <summary>The recipient (public key) of an identity.</summary>
    public static byte[] Recipient(ReadOnlySpan<byte> identity) => AgeX25519.PublicKey(identity);

    /// <summary>Whether an identity is the one a file was encrypted for: checked before anything is written or restarted.</summary>
    public static bool Opens(ReadOnlySpan<byte> identity, string recipient) =>
        CryptographicOperations.FixedTimeEquals(Recipient(identity), AgeKeys.ParseRecipient(recipient));
}
