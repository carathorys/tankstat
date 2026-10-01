using System.Net.Mail;

namespace Tankstat.Domain.Users;

public sealed class User
{
    private User() { } // EF Core

    public Guid Id { get; private set; }
    public UserProvider Provider { get; private set; }

    /// <summary>Stable identifier from the provider: the normalized e-mail for Local users, the "sub"/header value otherwise.</summary>
    public string Subject { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public bool IsAdmin { get; private set; }
    public bool IsDisabled { get; private set; }

    // Local (standalone) credentials.
    public string? PasswordHash { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockoutEnd { get; private set; }

    /// <summary>Embedded in session cookies; bumping it signs the user out everywhere.</summary>
    public int SessionVersion { get; private set; }

    public static string NormalizeEmail(string? email)
    {
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0 || normalized.Length > 254 || !MailAddress.TryCreate(normalized, out var parsed) || parsed.Address != normalized)
            throw new DomainException("A valid e-mail address is required.");
        return normalized;
    }

    /// <summary>For lookups: returns "" instead of throwing when the input is not a valid address.</summary>
    public static string NormalizeEmailOrEmpty(string? email)
    {
        try { return NormalizeEmail(email); }
        catch (DomainException) { return ""; }
    }

    public static User CreateLocal(string email, string? displayName, bool isAdmin)
    {
        var normalized = NormalizeEmail(email);
        return new User
        {
            Id = Guid.NewGuid(),
            Provider = UserProvider.Local,
            Subject = normalized,
            Email = normalized,
            DisplayName = Name(displayName, normalized),
            IsAdmin = isAdmin,
        };
    }

    public static User CreateExternal(UserProvider provider, string subject, string? email, string? displayName, bool isAdmin)
    {
        if (provider == UserProvider.Local) throw new DomainException("Use CreateLocal for local users.");
        if (string.IsNullOrWhiteSpace(subject)) throw new DomainException("An external user needs a subject.");
        var mail = (email ?? "").Trim().ToLowerInvariant();
        return new User
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            Subject = subject.Trim(),
            Email = mail,
            DisplayName = Name(displayName, mail.Length > 0 ? mail : subject.Trim()),
            IsAdmin = isAdmin,
        };
    }

    private static string Name(string? displayName, string fallback) =>
        string.IsNullOrWhiteSpace(displayName) ? fallback : displayName.Trim();

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;

    public void RegisterFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockout)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= maxAttempts)
        {
            LockoutEnd = now + lockout;
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockoutEnd = null;
    }

    /// <summary>Sets a new password and invalidates every existing session.</summary>
    public void SetPasswordHash(string hash)
    {
        if (Provider != UserProvider.Local) throw new DomainException("Only local users have a password.");
        PasswordHash = hash;
        FailedLoginCount = 0;
        LockoutEnd = null;
        SessionVersion++;
    }

    public void UpdateProfile(string? email, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(email)) Email = email.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(displayName)) DisplayName = displayName.Trim();
    }

    public void SetAdmin(bool isAdmin) => IsAdmin = isAdmin;

    public void SetDisabled(bool disabled)
    {
        IsDisabled = disabled;
        if (disabled) SessionVersion++;
    }
}
