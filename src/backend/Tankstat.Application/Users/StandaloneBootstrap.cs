using Tankstat.Domain;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Users;

/// <summary>
/// Whether a start in Standalone can create the first administrator from <c>Auth:Standalone:AdminEmail</c> and <c>AdminPassword</c>, as
/// <see cref="AuthService.BootstrapAdminAsync"/> will try to when no local administrator exists (and fails the start when it cannot): both
/// given, the password within <see cref="PasswordPolicy"/>, the address a valid one. Shared by the system settings, which ask the users there
/// are now, and the restore, which asks the users it is about to load.
/// </summary>
public static class StandaloneBootstrap
{
    /// <summary>
    /// Why the administrator could not be created (<c>missing</c>, <c>password</c>, <c>email</c>), or null with the address in its normalized
    /// form, which no local account may hold yet (else <c>taken</c>, which only the caller can tell).
    /// </summary>
    public static string? ProblemOf(string? email, string? password, out string address)
    {
        address = "";
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return "missing";
        try
        {
            PasswordPolicy.Validate(password);
        }
        catch (DomainException)
        {
            return "password";
        }
        try
        {
            address = User.NormalizeEmail(email);
        }
        catch (DomainException)
        {
            return "email";
        }
        return null;
    }
}
