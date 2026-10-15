namespace Tankstat.Domain.Users;

public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    public static void Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            throw new DomainException("password.tooShort", $"Password must be at least {MinLength} characters long.", new { Min = MinLength });
        if (password.Length > MaxLength)
            throw new DomainException("password.tooLong", $"Password must be at most {MaxLength} characters long.", new { Max = MaxLength });
    }

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
