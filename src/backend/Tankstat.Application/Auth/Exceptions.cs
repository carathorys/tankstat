namespace Tankstat.Application.Auth;

/// <summary>The request needs a signed-in user.</summary>
public sealed class UnauthenticatedException() : Exception("Authentication is required.");

/// <summary>The signed-in user may not do this.</summary>
public sealed class ForbiddenException(string? message = null) : Exception(message ?? "You are not allowed to do this.");

/// <summary>Deliberately vague so it does not reveal whether an account exists.</summary>
public sealed class InvalidCredentialsException() : Exception("Invalid e-mail or password, or the account is temporarily locked.");
