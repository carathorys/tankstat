using Tankstat.Domain;

namespace Tankstat.Application.Auth;

/// <summary>The request needs a signed-in user.</summary>
public sealed class UnauthenticatedException() : KeyedException("auth.unauthenticated", "Authentication is required.");

/// <summary>The signed-in user may not do this.</summary>
public sealed class ForbiddenException(string key = "auth.forbidden", string? message = null)
    : KeyedException(key, message ?? "You are not allowed to do this.");

/// <summary>Deliberately vague so it does not reveal whether an account exists.</summary>
public sealed class InvalidCredentialsException()
    : KeyedException("auth.invalidCredentials", "Invalid e-mail or password, or the account is temporarily locked.");
