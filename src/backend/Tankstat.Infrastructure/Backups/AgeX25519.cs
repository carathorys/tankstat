using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Math.EC.Rfc7748;
using Tankstat.Domain.Backups;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// age's X25519 recipient type: the file key wrapped with a key agreed between an ephemeral key pair and the recipient
/// (HKDF-SHA-256 over the shared secret, salted with both public keys, then ChaCha20-Poly1305 with a zero nonce). .NET has no X25519 on
/// Linux, so the curve comes from BouncyCastle; the ciphers are .NET's own.
/// </summary>
internal static class AgeX25519
{
    private static readonly byte[] Info = Encoding.ASCII.GetBytes(AgeFormat.X25519Info);

    /// <summary>The public key of an identity.</summary>
    public static byte[] PublicKey(ReadOnlySpan<byte> identity)
    {
        var publicKey = new byte[AgeKeys.KeyLength];
        X25519.ScalarMultBase(identity.ToArray(), 0, publicKey, 0);
        return publicKey;
    }

    /// <summary>The stanza that hands the file key to the recipient.</summary>
    public static AgeStanza Wrap(ReadOnlySpan<byte> recipient, ReadOnlySpan<byte> fileKey)
    {
        var ephemeral = RandomNumberGenerator.GetBytes(AgeKeys.KeyLength);
        var share = PublicKey(ephemeral);
        var shared = Agree(ephemeral, recipient) ?? throw new CryptographicException("The recipient is not a usable X25519 key.");
        var body = new byte[AgeFormat.FileKeyLength + AgeFormat.TagLength];
        using (var aead = new ChaCha20Poly1305(WrapKey(shared, share, recipient)))
            aead.Encrypt(new byte[12], fileKey, body.AsSpan(0, AgeFormat.FileKeyLength), body.AsSpan(AgeFormat.FileKeyLength));
        return new AgeStanza(AgeFormat.X25519, [AgeFormat.Base64(share)], body);
    }

    /// <summary>The file key of a stanza, when the identity is its recipient's; null when the stanza is not for this identity.</summary>
    public static byte[]? Unwrap(AgeStanza stanza, ReadOnlySpan<byte> identity)
    {
        if (stanza.Type != AgeFormat.X25519 || stanza.Args.Count != 1 || stanza.Body.Length != AgeFormat.FileKeyLength + AgeFormat.TagLength) return null;
        byte[] share;
        try
        {
            share = AgeFormat.FromBase64(stanza.Args[0]);
        }
        catch (FormatException)
        {
            return null;
        }
        if (share.Length != AgeKeys.KeyLength) return null;
        var shared = Agree(identity, share);
        if (shared is null) return null; // an all-zero shared secret: refused, as the format asks
        var fileKey = new byte[AgeFormat.FileKeyLength];
        try
        {
            using var aead = new ChaCha20Poly1305(WrapKey(shared, share, PublicKey(identity)));
            aead.Decrypt(new byte[12], stanza.Body.AsSpan(0, AgeFormat.FileKeyLength), stanza.Body.AsSpan(AgeFormat.FileKeyLength), fileKey);
            return fileKey;
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }
    }

    private static byte[]? Agree(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> publicKey)
    {
        var shared = new byte[AgeKeys.KeyLength];
        return X25519.CalculateAgreement(secret.ToArray(), 0, publicKey.ToArray(), 0, shared, 0) ? shared : null;
    }

    private static byte[] WrapKey(byte[] shared, ReadOnlySpan<byte> share, ReadOnlySpan<byte> recipient) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, AgeKeys.KeyLength, [.. share, .. recipient], Info);
}
