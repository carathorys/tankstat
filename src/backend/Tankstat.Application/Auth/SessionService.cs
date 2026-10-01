using Microsoft.Extensions.Options;

namespace Tankstat.Application.Auth;

public sealed record SessionInfo(AuthMode Mode, Principal? User);

public sealed class SessionService(ICurrentUser current, IOptions<AuthOptions> auth)
{
    public async Task<SessionInfo> GetAsync(CancellationToken ct) => new(auth.Value.Mode, await current.GetPrincipalAsync(ct));
}
