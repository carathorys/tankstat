using Tankstat.Api.Auth;
using Tankstat.Application.Users;

namespace Tankstat.Api.GraphQL;

/// <summary>A device the user is signed in on (Standalone and OIDC): what it is, when it signed in and was last used, and whether it is this one.</summary>
/// <param name="Client">A coarse name worked out from the browser ("Firefox on Linux"); null when it could not be told.</param>
/// <param name="ExpiresAt">When it is signed out unless it is used again before.</param>
public sealed record UserSessionInfo(Guid Id, string? Client, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt, DateTimeOffset ExpiresAt, bool Current);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class DeviceSessionQueries
{
    /// <summary>The devices the signed-in user is signed in on, most recently used first.</summary>
    public async Task<IReadOnlyList<UserSessionInfo>> GetMySessions([Service] UserSessionService sessions, [Service] IHttpContextAccessor http, CancellationToken ct)
    {
        var current = SessionCookies.SessionId(http.HttpContext!.User);
        return (await sessions.ListMineAsync(ct)).Select(s => new UserSessionInfo(s.Id, s.Client, s.CreatedAt, s.LastUsedAt, s.ExpiresAt, s.Id == current)).ToList();
    }
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class DeviceSessionMutations
{
    /// <summary>Signs one of your devices out; this one too (its cookies are removed). False when it was not yours or already signed out.</summary>
    public async Task<bool> RevokeSession(Guid id, [Service] UserSessionService sessions, [Service] IHttpContextAccessor http, CancellationToken ct)
    {
        var context = http.HttpContext!;
        var revoked = await sessions.RevokeMineAsync(id, ct);
        if (revoked && SessionCookies.SessionId(context.User) == id) await SessionCookies.ClearAsync(context);
        return revoked;
    }

    /// <summary>Signs every device but this one out; returns how many.</summary>
    public Task<int> RevokeOtherSessions([Service] UserSessionService sessions, [Service] IHttpContextAccessor http, CancellationToken ct) =>
        sessions.RevokeMyOthersAsync(SessionCookies.SessionId(http.HttpContext!.User), ct);
}
