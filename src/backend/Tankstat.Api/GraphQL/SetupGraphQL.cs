using Tankstat.Api.Auth;
using Tankstat.Application.Setup;

namespace Tankstat.Api.GraphQL;

/// <param name="Code">The one-time code from the server's log.</param>
public sealed record SetupAdministratorInput(string Code, string Email, string Password);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class SetupQueries
{
    /// <summary>Whether the instance waits for its setup (public, nothing secret: the code is only in the server's log).</summary>
    public Task<SetupInfo> GetSetup([Service] SetupService setup, CancellationToken ct) => setup.InfoAsync(ct);

    /// <summary>
    /// The backups folder's restore points while the setup waits, to restore one into the new instance with <c>restoreBackup</c> (which
    /// takes the same code then). The one-time code from the server's log: <c>setup.codeInvalid</c> without it, <c>setup.completed</c> once
    /// the setup is done (administrators list them under <c>backups</c>).
    /// </summary>
    public async Task<IReadOnlyList<RestorePointInfo>> GetSetupRestorePoints(string code, [Service] SetupService setup, CancellationToken ct)
    {
        var (points, recipient) = await setup.RestorePointsAsync(code, ct);
        return [.. points.Points.Select(p => RestorePointInfo.From(p, points, recipient))];
    }
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class SetupMutations
{
    /// <summary>
    /// Standalone, while the setup waits: makes the first administrator and signs them in on this device (both cookies), which completes the
    /// setup. The one-time code from the server's log; the bootstrap administrator's rules (<c>user.emailInvalid</c>, the password policy).
    /// </summary>
    public async Task<UserInfo> SetupCreateAdministrator(
        SetupAdministratorInput input, [Service] SetupService setup, [Service] IHttpContextAccessor http, CancellationToken ct)
    {
        var administrator = await setup.CreateAdministratorAsync(input.Code, input.Email, input.Password, ct);
        await SessionCookies.IssueAsync(http.HttpContext!, administrator, ct);
        return UserInfo.From(administrator);
    }

    /// <summary>
    /// OIDC and proxy mode, while the setup waits: the signed-in user becomes the first administrator (at once: every request reloads the
    /// user), which completes the setup. The one-time code from the server's log; signed out, <c>auth.unauthenticated</c>.
    /// </summary>
    public async Task<UserInfo> SetupClaimAdministrator(string code, [Service] SetupService setup, CancellationToken ct) =>
        UserInfo.From(await setup.ClaimAdministratorAsync(code, ct));

    /// <summary>With sign-in off, while the setup waits: acknowledges that everyone who reaches the instance sees and changes everything, which completes the setup.</summary>
    public Task<SetupInfo> SetupAcknowledgeOpenAccess(string code, [Service] SetupService setup, CancellationToken ct) => setup.AcknowledgeOpenAccessAsync(code, ct);
}
