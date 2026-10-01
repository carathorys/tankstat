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
}
