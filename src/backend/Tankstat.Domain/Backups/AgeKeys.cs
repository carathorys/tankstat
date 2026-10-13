namespace Tankstat.Domain.Backups;

/// <summary>
/// How age writes an X25519 key pair: the recipient (public key) as <c>age1...</c>, the identity (secret) as <c>AGE-SECRET-KEY-1...</c>,
/// both Bech32 over the raw 32 bytes. A backup is encrypted for a recipient; only the identity opens it.
/// </summary>
public static class AgeKeys
{
    public const int KeyLength = 32;
    private const string RecipientHrp = "age";
    private const string IdentityHrp = "AGE-SECRET-KEY-";

    public static string FormatRecipient(ReadOnlySpan<byte> publicKey) => Bech32.Encode(RecipientHrp, Checked(publicKey));

    public static string FormatIdentity(ReadOnlySpan<byte> secret) => Bech32.Encode(IdentityHrp, Checked(secret)).ToUpperInvariant();

    /// <summary>The 32 bytes of a recipient; a <see cref="FormatException"/> for anything else.</summary>
    public static byte[] ParseRecipient(string text) => Parse(text, RecipientHrp, "recipient");

    /// <summary>The 32 bytes of an identity; a <see cref="FormatException"/> for anything else.</summary>
    public static byte[] ParseIdentity(string text) => Parse(text, IdentityHrp, "identity");

    private static byte[] Parse(string text, string hrp, string what)
    {
        (string Hrp, byte[] Data) decoded;
        try
        {
            decoded = Bech32.Decode(text.Trim());
        }
        catch (FormatException e)
        {
            throw new FormatException($"Not an age {what}.", e);
        }
        if (!string.Equals(decoded.Hrp, hrp, StringComparison.OrdinalIgnoreCase) || decoded.Data.Length != KeyLength) throw new FormatException($"Not an age {what}.");
        return decoded.Data;
    }

    private static ReadOnlySpan<byte> Checked(ReadOnlySpan<byte> key) =>
        key.Length == KeyLength ? key : throw new ArgumentException($"An X25519 key is {KeyLength} bytes.", nameof(key));
}
