using System.Text;

namespace Tankstat.Domain.Backups;

/// <summary>One recipient stanza of an age header: <c>-&gt; type args...</c> and a body in base64 lines of 64 characters, the last one shorter.</summary>
public sealed record AgeStanza(string Type, IReadOnlyList<string> Args, byte[] Body);

/// <summary>
/// The age v1 file format (https://age-encryption.org/v1), the pure parts: the header (version line, stanzas, the MAC line) as text, and
/// the nonce of every payload chunk. Keys and ciphers are the infrastructure's business; this is the layout they fill.
/// </summary>
public static class AgeFormat
{
    public const string Version = "age-encryption.org/v1";
    public const string X25519 = "X25519";
    /// <summary>age's own passphrase mode, which Tankstat never writes and does not read (a backup is encrypted for a recipient).</summary>
    public const string Scrypt = "scrypt";

    public const string X25519Info = "age-encryption.org/v1/X25519";
    public const string HeaderInfo = "header";
    public const string PayloadInfo = "payload";

    public const int FileKeyLength = 16;
    public const int PayloadNonceLength = 16;
    public const int ChunkSize = 64 * 1024;
    public const int TagLength = 16;
    public const int MacLength = 32;

    private const string StanzaStart = "-> ";
    private const string MacStart = "---";
    private const int BodyLineLength = 64;

    /// <summary>What the header MAC covers: everything up to and including <c>---</c>.</summary>
    public static byte[] MacInput(IEnumerable<AgeStanza> stanzas)
    {
        var text = new StringBuilder(Version).Append('\n');
        foreach (var stanza in stanzas)
        {
            text.Append(StanzaStart).Append(stanza.Type);
            foreach (var arg in stanza.Args) text.Append(' ').Append(arg);
            text.Append('\n');
            var body = Base64(stanza.Body);
            for (var i = 0; ; i += BodyLineLength)
            {
                var line = body.Substring(i, Math.Min(BodyLineLength, body.Length - i));
                text.Append(line).Append('\n');
                if (line.Length < BodyLineLength) break;
            }
        }
        return Encoding.ASCII.GetBytes(text.Append(MacStart).ToString());
    }

    /// <summary>The whole header: the MAC input, the MAC and the line break before the payload.</summary>
    public static byte[] Header(IEnumerable<AgeStanza> stanzas, ReadOnlySpan<byte> mac)
    {
        if (mac.Length != MacLength) throw new ArgumentException($"The header MAC is {MacLength} bytes.", nameof(mac));
        return [.. MacInput(stanzas), .. Encoding.ASCII.GetBytes(" " + Base64(mac) + "\n")];
    }

    /// <summary>
    /// Reads a header from the start of a stream, leaving it at the first payload byte; a <see cref="FormatException"/> when it is not one.
    /// The stream is read byte by byte, so nothing of the payload is taken (wrap an unbuffered stream first).
    /// </summary>
    public static (IReadOnlyList<AgeStanza> Stanzas, byte[] Mac) ReadHeader(Stream stream)
    {
        if (ReadLine(stream) != Version) throw new FormatException("Not an age file.");
        var stanzas = new List<AgeStanza>();
        var line = ReadLine(stream);
        while (line.StartsWith(StanzaStart, StringComparison.Ordinal))
        {
            var parts = line[StanzaStart.Length..].Split(' ');
            if (parts.Length == 0 || parts.Any(p => p.Length == 0)) throw new FormatException("Not an age file.");
            var body = new StringBuilder();
            string bodyLine;
            do
            {
                bodyLine = ReadLine(stream);
                if (bodyLine.Length > BodyLineLength) throw new FormatException("Not an age file.");
                body.Append(bodyLine);
            } while (bodyLine.Length == BodyLineLength);
            stanzas.Add(new AgeStanza(parts[0], parts[1..], FromBase64(body.ToString())));
            line = ReadLine(stream);
        }
        if (stanzas.Count == 0 || !line.StartsWith(MacStart + " ", StringComparison.Ordinal)) throw new FormatException("Not an age file.");
        var mac = FromBase64(line[(MacStart.Length + 1)..]);
        if (mac.Length != MacLength) throw new FormatException("Not an age file.");
        return (stanzas, mac);
    }

    /// <summary>The nonce of a payload chunk: an 11-byte big-endian counter and a last byte that says whether it is the final chunk.</summary>
    public static byte[] ChunkNonce(long counter, bool last)
    {
        var nonce = new byte[12];
        for (var i = 0; i < 8; i++) nonce[10 - i] = (byte)(counter >> (8 * i));
        nonce[11] = last ? (byte)1 : (byte)0;
        return nonce;
    }

    /// <summary>Standard base64 without padding, as every base64 in an age header.</summary>
    public static string Base64(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    /// <summary>The strict reverse of <see cref="Base64"/>: padding, whitespace or a non-canonical last group are refused.</summary>
    public static byte[] FromBase64(string text)
    {
        if (text.Length % 4 == 1 || text.Any(c => c is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/')))
            throw new FormatException("Not an age file.");
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(text + new string('=', (4 - text.Length % 4) % 4));
        }
        catch (FormatException e)
        {
            throw new FormatException("Not an age file.", e);
        }
        if (Base64(bytes) != text) throw new FormatException("Not an age file.");
        return bytes;
    }

    private static string ReadLine(Stream stream)
    {
        var bytes = new List<byte>(96);
        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0) throw new FormatException("Not an age file.");
            if (b == '\n') return Encoding.ASCII.GetString(bytes.ToArray());
            if (b is < 32 or > 126) throw new FormatException("Not an age file.");
            bytes.Add((byte)b);
        }
    }
}
