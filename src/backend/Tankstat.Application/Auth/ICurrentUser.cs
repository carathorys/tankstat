namespace Tankstat.Application.Auth;

/// <summary>Port implemented by the host: the authenticated user of the current request, or null.</summary>
public interface ICurrentUser
{
    Task<Principal?> GetPrincipalAsync(CancellationToken ct);
}
