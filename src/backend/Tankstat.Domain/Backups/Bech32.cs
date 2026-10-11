using System.Text;

namespace Tankstat.Domain.Backups;

/// <summary>
/// Bech32 (BIP-173) as age spells its keys: a human-readable part, the separator <c>1</c>, the data in five-bit groups and a six-character
/// checksum. age writes recipients in lower case (<c>age1...</c>) and identities in upper case (<c>AGE-SECRET-KEY-1...</c>); a string is
/// read in either case, never mixed.
/// </summary>
public static class Bech32
{
    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    private const int ChecksumLength = 6;

    public static string Encode(string hrp, ReadOnlySpan<byte> data)
    {
        if (hrp.Length == 0) throw new ArgumentException("The human-readable part must not be empty.", nameof(hrp));
        var lower = hrp.ToLowerInvariant();
        var five = ToFiveBits(data);
        var checksum = Checksum(lower, five);
        var text = new StringBuilder(lower.Length + 1 + five.Length + ChecksumLength).Append(lower).Append('1');
        foreach (var v in five) text.Append(Charset[v]);
        foreach (var v in checksum) text.Append(Charset[v]);
        return text.ToString();
    }

    /// <summary>The human-readable part as written and the data; a <see cref="FormatException"/> for anything that is not Bech32.</summary>
    public static (string Hrp, byte[] Data) Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Any(c => c is < '!' or > '~')) throw new FormatException("Not a Bech32 string.");
        if (text.Any(char.IsUpper) && text.Any(char.IsLower)) throw new FormatException("A Bech32 string is all lower or all upper case.");
        var lower = text.ToLowerInvariant();
        var separator = lower.LastIndexOf('1');
        if (separator < 1 || separator + 1 + ChecksumLength > lower.Length) throw new FormatException("Not a Bech32 string.");
        var hrp = lower[..separator];
        var values = new byte[lower.Length - separator - 1];
        for (var i = 0; i < values.Length; i++)
        {
            var index = Charset.IndexOf(lower[separator + 1 + i]);
            if (index < 0) throw new FormatException("Not a Bech32 string.");
            values[i] = (byte)index;
        }
        if (Polymod(HrpExpand(hrp).Concat(values)) != 1) throw new FormatException("The Bech32 checksum does not match.");
        return (text[..separator], FromFiveBits(values.AsSpan(0, values.Length - ChecksumLength)));
    }

    private static byte[] ToFiveBits(ReadOnlySpan<byte> data)
    {
        var result = new List<byte>(data.Length * 8 / 5 + 1);
        int acc = 0, bits = 0;
        foreach (var b in data)
        {
            acc = (acc << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                result.Add((byte)((acc >> bits) & 31));
            }
        }
        if (bits > 0) result.Add((byte)((acc << (5 - bits)) & 31));
        return [.. result];
    }

    private static byte[] FromFiveBits(ReadOnlySpan<byte> values)
    {
        var result = new List<byte>(values.Length * 5 / 8);
        int acc = 0, bits = 0;
        foreach (var v in values)
        {
            acc = (acc << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                result.Add((byte)((acc >> bits) & 255));
            }
        }
        // What is left must be the zero padding of the last group, shorter than a whole one.
        if (bits >= 5 || ((acc << (8 - bits)) & 255) != 0) throw new FormatException("The Bech32 data is not padded as it should be.");
        return [.. result];
    }

    private static IEnumerable<byte> HrpExpand(string hrp) =>
        hrp.Select(c => (byte)(c >> 5)).Append((byte)0).Concat(hrp.Select(c => (byte)(c & 31)));

    private static byte[] Checksum(string hrp, byte[] data)
    {
        var mod = Polymod(HrpExpand(hrp).Concat(data).Concat(new byte[ChecksumLength])) ^ 1;
        var checksum = new byte[ChecksumLength];
        for (var i = 0; i < ChecksumLength; i++) checksum[i] = (byte)((mod >> (5 * (5 - i))) & 31);
        return checksum;
    }

    private static uint Polymod(IEnumerable<byte> values)
    {
        ReadOnlySpan<uint> generator = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];
        uint chk = 1;
        foreach (var v in values)
        {
            var top = chk >> 25;
            chk = ((chk & 0x1ffffff) << 5) ^ v;
            for (var i = 0; i < 5; i++)
                if (((top >> i) & 1) != 0) chk ^= generator[i];
        }
        return chk;
    }
}
