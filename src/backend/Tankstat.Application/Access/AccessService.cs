using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Domain.Access;

namespace Tankstat.Application.Access;

/// <summary>
/// The access layer above the raw entities: every use case asks it who is acting and what they may touch,
/// instead of looking at owners itself. Works for any <see cref="IOwned"/> entity.
/// </summary>
public sealed class AccessService(
    ICurrentUser current, IOptions<AuthOptions> auth, IAccessGrantRepository grants, IAccessSettingsRepository settings)
{
    public async Task<Principal> RequirePrincipalAsync(CancellationToken ct)
    {
        if (await current.GetPrincipalAsync(ct) is { } principal) return principal;
        if (auth.Value.Mode == AuthMode.None) return Principal.Anonymous;
        throw new UnauthenticatedException();
    }

    /// <summary>Administrators only; never available when authentication is off (there is no real administrator then).</summary>
    public async Task<Principal> RequireAdminAsync(CancellationToken ct)
    {
        var principal = await RequirePrincipalAsync(ct);
        if (principal.IsAnonymous || !principal.IsAdmin) throw new ForbiddenException("auth.adminRequired", "Administrator rights are required.");
        return principal;
    }

    public async Task<AccessLevel> LevelAsync(Guid ownerId, CancellationToken ct)
    {
        var principal = await RequirePrincipalAsync(ct);
        if (principal.IsAdmin || principal.Id == ownerId) return AccessLevel.Edit;

        var defaults = (await settings.GetAsync(ct)).DefaultLevelForOthers;
        var granted = await grants.ListForGranteeAsync(principal.Id, ct);
        return AccessPolicy.Resolve(principal.Id, principal.IsAdmin, ownerId, defaults, granted);
    }

    public async Task<bool> CanAsync(Guid ownerId, AccessLevel level, CancellationToken ct) =>
        await LevelAsync(ownerId, ct) >= level;

    /// <summary>The owners whose data the current user may access at (at least) <paramref name="level"/>.</summary>
    public async Task<OwnerScope> ScopeAsync(AccessLevel level, CancellationToken ct)
    {
        var principal = await RequirePrincipalAsync(ct);
        if (principal.IsAdmin) return OwnerScope.All;

        var defaults = (await settings.GetAsync(ct)).DefaultLevelForOthers;
        if (defaults >= level) return OwnerScope.All;

        var owners = (await grants.ListForGranteeAsync(principal.Id, ct))
            .Where(g => g.Level >= level).Select(g => g.OwnerId).Append(principal.Id);
        return OwnerScope.Of(owners);
    }
}
