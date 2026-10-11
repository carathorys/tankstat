using System.Text;
using Tankstat.Domain.Backups;

namespace Tankstat.Domain.UnitTests;

/// <summary>The text forms of age: Bech32 keys as age-keygen writes them, and the header as age writes it (the fixture is age 1.3.2's).</summary>
public class AgeFormatTests
{
    // age-keygen's pair for tests/backend/contracts/age (the identity is a test key, used for nothing else).
    private const string Recipient = "age1a42zkrtucpm5gkzw4ur7759dutj838g6xaj6hafkkyp5002g4p4qzu53nv";
    private const string Identity = "AGE-SECRET-KEY-1RR0SC0HNJ9QQW4A56Z4XYHLYYQP6K89KR2SFUGJGZT2G7926RSKSZAXXRC";

    private const string FixtureHeader = "age-encryption.org/v1\n-> X25519 VDX70gM/skstctd3hsjA/rncRIA+W5jO463OC8jTqlA\nVXwRLKbVzwkCS4qtq/lY9yxVBJ0SN/GXZnNoier2NKE\n--- FuOq0zC1ttd3FkEiFjH6x2rYVh2r0boqxmVRCQNM6p8\n";

    [Theory]
    [InlineData("A12UEL5L")] // BIP-173 valid strings
    [InlineData("a12uel5l")]
    [InlineData("abcdef1qpzry9x8gf2tvdw0s3jn54khce6mua7lmqqqxw")]
    [InlineData("split1checkupstagehandshakeupstreamerranterredcaperred2y9e3w")]
    public void Bech32_ValidStrings_DecodeAndEncodeBackTheSame(string text)
    {
        var (hrp, data) = Bech32.Decode(text);

        Assert.Equal(text.ToLowerInvariant(), Bech32.Encode(hrp, data));
    }

    [Theory]
    [InlineData("A1G7SGD8")] // a wrong checksum
    [InlineData("split1checkupstagehandshakeupstreamerranterredcaperred2y9e2w")]
    [InlineData("1pzry9x8gf2tvdw0s3jn54khce6mua7l")] // no human-readable part
    [InlineData("pzry9x8gf2tvdw0s3jn54khce6mua7l")] // no separator
    [InlineData("x1b4n0q5v")] // a character outside the charset
    [InlineData("A12UEl5L")] // mixed case
    [InlineData("A1\x7fG7SGD8")]
    public void Bech32_InvalidStrings_AreRefused(string text) => Assert.Throws<FormatException>(() => Bech32.Decode(text));

    [Fact]
    public void AgeKeys_AreReadAndWrittenAsAgeKeygenWritesThem()
    {
        var publicKey = AgeKeys.ParseRecipient(Recipient);
        var secret = AgeKeys.ParseIdentity(Identity);

        Assert.Equal(32, publicKey.Length);
        Assert.Equal(32, secret.Length);
        Assert.Equal(Recipient, AgeKeys.FormatRecipient(publicKey));
        Assert.Equal(Identity, AgeKeys.FormatIdentity(secret));
        Assert.Equal(secret, AgeKeys.ParseIdentity("  " + Identity.ToLowerInvariant() + "\n")); // either case, surrounding space ignored
        Assert.Throws<FormatException>(() => AgeKeys.ParseRecipient(Identity)); // the wrong kind of key
        Assert.Throws<FormatException>(() => AgeKeys.ParseIdentity(Recipient));
        Assert.Throws<FormatException>(() => AgeKeys.ParseRecipient("age1" + Recipient[5..])); // a damaged one
        Assert.Throws<ArgumentException>(() => AgeKeys.FormatRecipient(new byte[31]));
    }

    [Fact]
    public void Header_IsReadFromTheFixtureAndWrittenBackByteForByte()
    {
        var bytes = Encoding.ASCII.GetBytes(FixtureHeader + "payload follows");
        using var stream = new MemoryStream(bytes);

        var (stanzas, mac) = AgeFormat.ReadHeader(stream);

        var stanza = Assert.Single(stanzas);
        Assert.Equal(AgeFormat.X25519, stanza.Type);
        Assert.Equal(["VDX70gM/skstctd3hsjA/rncRIA+W5jO463OC8jTqlA"], stanza.Args);
        Assert.Equal(32, stanza.Body.Length);
        Assert.Equal(32, mac.Length);
        Assert.Equal(FixtureHeader.Length, stream.Position); // the payload is untouched
        Assert.Equal(FixtureHeader, Encoding.ASCII.GetString(AgeFormat.Header(stanzas, mac)));
        Assert.Equal(FixtureHeader[..FixtureHeader.LastIndexOf(" ", StringComparison.Ordinal)], Encoding.ASCII.GetString(AgeFormat.MacInput(stanzas)));
    }

    [Fact]
    public void Header_WithALongBody_WrapsAt64Characters_AndEndsWithAShorterLine()
    {
        var stanza = new AgeStanza("X25519", ["abc"], Enumerable.Range(0, 96).Select(i => (byte)i).ToArray()); // 128 base64 characters: two full lines, then an empty one

        var text = Encoding.ASCII.GetString(AgeFormat.MacInput([stanza]));
        var lines = text.Split('\n');

        Assert.Equal(["age-encryption.org/v1", "-> X25519 abc", lines[2], lines[3], "", "---"], lines);
        Assert.Equal(64, lines[2].Length);
        Assert.Equal(64, lines[3].Length);
        using var stream = new MemoryStream(AgeFormat.Header([stanza], new byte[32]));
        var (back, _) = AgeFormat.ReadHeader(stream);
        Assert.Equal(stanza.Body, Assert.Single(back).Body);
    }

    [Theory]
    [InlineData("not age at all\n")]
    [InlineData("age-encryption.org/v1\n--- AAAA\n")] // no stanza
    [InlineData("age-encryption.org/v1\n-> X25519 abc\nAAAA\n")] // cut short before the MAC
    [InlineData("age-encryption.org/v1\n-> X25519 abc\nAAA=\n--- FuOq0zC1ttd3FkEiFjH6x2rYVh2r0boqxmVRCQNM6p8\n")] // padded base64
    [InlineData("age-encryption.org/v1\n-> X25519 abc\nAAF\n--- FuOq0zC1ttd3FkEiFjH6x2rYVh2r0boqxmVRCQNM6p8\n")] // non-canonical base64 (padding bits set)
    [InlineData("age-encryption.org/v1\n-> X25519 abc\nAAAA\n--- short\n")] // a MAC of the wrong length
    public void Header_ThatIsNotOne_IsRefused(string text) =>
        Assert.Throws<FormatException>(() => AgeFormat.ReadHeader(new MemoryStream(Encoding.ASCII.GetBytes(text))));

    [Fact]
    public void ChunkNonce_CountsBigEndianInElevenBytes_AndMarksTheLastChunk()
    {
        Assert.Equal(new byte[12], AgeFormat.ChunkNonce(0, false));
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 1], AgeFormat.ChunkNonce(258, true));
        Assert.Equal([0, 0, 0, 0x7f, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0], AgeFormat.ChunkNonce(long.MaxValue, false));
    }

    [Fact]
    public void Base64_IsUnpaddedStandardBase64_AndStrictOnTheWayBack()
    {
        Assert.Equal("AAE", AgeFormat.Base64([0, 1]));
        Assert.Equal([0, 1], AgeFormat.FromBase64("AAE"));
        Assert.Equal([], AgeFormat.FromBase64(""));
        Assert.Throws<FormatException>(() => AgeFormat.FromBase64("AAE="));
        Assert.Throws<FormatException>(() => AgeFormat.FromBase64("AAF")); // the padding bits are not zero
        Assert.Throws<FormatException>(() => AgeFormat.FromBase64("A"));
        Assert.Throws<FormatException>(() => AgeFormat.FromBase64("AA E"));
    }
}
