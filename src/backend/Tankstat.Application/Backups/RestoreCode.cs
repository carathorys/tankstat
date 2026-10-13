using System.Security.Cryptography;

namespace Tankstat.Application.Backups;

/// <summary>
/// With sign-in off (<c>Auth:Mode=None</c>) there is no administrator, so the one operation that replaces everything, a restore asked for
/// from the app, is guarded by a code made at every start and written to the server's log: whoever reads the log may restore. One code per
/// run, compared in fixed time.
/// </summary>
public sealed class RestoreCode
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O, 1/I
    private const int Length = 8;

    public string Value { get; } = RandomNumberGenerator.GetString(Alphabet, Length);

    public bool Matches(string? code) =>
        code is not null && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(Value), System.Text.Encoding.ASCII.GetBytes(code.Trim().ToUpperInvariant()));
}
