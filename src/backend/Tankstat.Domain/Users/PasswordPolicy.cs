namespace Tankstat.Domain.Users;

/// <summary>
/// What a password must be: at least the minimum the administrators set (<c>Auth:Standalone:PasswordMinLength</c>, never below
/// <see cref="MinLength"/>), at most <see cref="MaxLength"/> (the hasher's cost).
/// </summary>
public static class PasswordPolicy
{
    /// <summary>The floor: the least any setting may require (today's rule before the setting existed).</summary>
    public const int MinLength = 10;
    public const int MaxLength = 128;

    /// <summary>Refuses a password shorter than <paramref name="minLength"/> (<c>password.tooShort</c>) or longer than the ceiling.</summary>
    public static void Validate(string? password, int minLength)
    {
        if (string.IsNullOrEmpty(password) || password.Length < minLength)
            throw new DomainException("password.tooShort", $"Password must be at least {minLength} characters long.", new { Min = minLength });
        if (password.Length > MaxLength)
            throw new DomainException("password.tooLong", $"Password must be at most {MaxLength} characters long.", new { Max = MaxLength });
    }

    /// <summary>Whether a password meets the requirements: what <see cref="Validate"/> would let through.</summary>
    public static bool Meets(string? password, int minLength) =>
        !string.IsNullOrEmpty(password) && password.Length >= minLength && password.Length <= MaxLength;

    /// <summary>
    /// A new secret (a password, a passphrase) must come twice, the same both times: <c>password.mismatch</c> for a missing or different
    /// repetition (an older client that sends none gets this too). Compared exactly: no trimming, no case folding.
    /// </summary>
    public static void RequireRepeated(string? value, string? repeated)
    {
        if (repeated is null || !string.Equals(value ?? "", repeated, StringComparison.Ordinal))
            throw new DomainException("password.mismatch", "The two entries are not the same.");
    }
}
