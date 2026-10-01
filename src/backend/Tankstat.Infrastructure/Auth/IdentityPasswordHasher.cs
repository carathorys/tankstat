using Microsoft.AspNetCore.Identity;
using Tankstat.Application.Auth;

namespace Tankstat.Infrastructure.Auth;

/// <summary>Adapter over the framework's PBKDF2 hasher (versioned format, constant-time comparison).</summary>
internal sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _inner = new();
    private static readonly object Subject = new();

    public string Hash(string password) => _inner.HashPassword(Subject, password);

    public bool Verify(string hash, string password) =>
        _inner.VerifyHashedPassword(Subject, hash, password) is not PasswordVerificationResult.Failed;
}
