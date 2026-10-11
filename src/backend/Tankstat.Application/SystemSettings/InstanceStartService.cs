using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Domain.Instance;

namespace Tankstat.Application.SystemSettings;

/// <summary>
/// Runs at every start, after the migrations: compares the authentication mode with the one the app last started in and, when it changed,
/// signs every device out (a session issued in Standalone must not keep its holder signed in once the instance moved to OIDC, or back),
/// then records this start. The first start, and a start in the same mode, sign nobody out.
/// </summary>
public sealed class InstanceStartService(
    IInstanceStateRepository instance, IUserSessionRepository sessions, IOptions<AuthOptions> auth, TimeProvider clock, ILogger<InstanceStartService> logger)
{
    public async Task RecordStartAsync(CancellationToken ct)
    {
        var state = await instance.GetAsync(ct) ?? InstanceState.New();
        var mode = auth.Value.Mode.ToString();
        var now = clock.GetUtcNow();
        if (state.LastStartMode is { } previous && !previous.Equals(mode, StringComparison.OrdinalIgnoreCase))
        {
            var count = await sessions.RevokeAllAsync(now, ct);
            if (count > 0) logger.LogInformation("The authentication mode changed from {Before} to {After}: {Count} signed-in devices were signed out", previous, mode, count);
            else logger.LogDebug("The authentication mode changed from {Before} to {After}; no device was signed in", previous, mode);
        }
        state.RecordStart(mode, now);
        await instance.SaveAsync(state, ct);
    }
}
