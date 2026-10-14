using Microsoft.Extensions.Options;
using Tankstat.Api.Auth;
using Tankstat.Application.Auth;
using Tankstat.Application.Setup;

namespace Tankstat.Api.GraphQL;

/// <summary>The setup's last step: what the sign-in mode the next start runs needs of it.</summary>
/// <param name="Code">The one-time code from the server's log.</param>
/// <param name="Email">Standalone: the first administrator's e-mail address.</param>
/// <param name="Password">Standalone: the first administrator's password.</param>
/// <param name="AcknowledgeOpenAccess">Sign-in off: that everyone who reaches the instance sees and changes everything.</param>
public sealed record SetupFinishInput(string Code, string? Email = null, string? Password = null, bool AcknowledgeOpenAccess = false);

/// <param name="Restarting">The server restarts a moment after this answer, for the settings the wizard saved.</param>
/// <param name="Administrator">The first administrator it made (Standalone).</param>
/// <param name="SignedIn">The administrator is signed in on this device: Standalone was running already (a restart into it from another mode signs everyone out).</param>
public sealed record SetupFinishInfo(bool Restarting, UserInfo? Administrator, bool SignedIn);

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
    /// While the setup waits, once the wizard saved its settings: its last step in the sign-in mode the next start runs (Standalone: the first
    /// administrator, signed in on this device when Standalone runs already; sign-in off: the acknowledgement; OIDC, proxy: nothing to make,
    /// the administrators the settings name, or the claim after the restart), then the restart the saved settings wait for. The one-time code
    /// from the server's log; what the next start would refuse is refused before anything is completed.
    /// </summary>
    public async Task<SetupFinishInfo> SetupFinish(
        SetupFinishInput input, [Service] SetupService setup, [Service] IOptions<AuthOptions> auth, [Service] IHttpContextAccessor http, CancellationToken ct)
    {
        var finished = await setup.FinishAsync(input.Code, input.Email, input.Password, input.AcknowledgeOpenAccess, ct);
        var signIn = finished.Administrator is not null && auth.Value.Mode == AuthMode.Standalone;
        if (signIn) await SessionCookies.IssueAsync(http.HttpContext!, finished.Administrator!, ct);
        return new SetupFinishInfo(finished.Restarting, finished.Administrator is { } a ? UserInfo.From(a) : null, signIn);
    }

    /// <summary>
    /// OIDC and proxy mode, while the setup waits: the signed-in user becomes the first administrator (at once: every request reloads the
    /// user), which completes the setup. The one-time code from the server's log; signed out, <c>auth.unauthenticated</c>.
    /// </summary>
    public async Task<UserInfo> SetupClaimAdministrator(string code, [Service] SetupService setup, CancellationToken ct) =>
        UserInfo.From(await setup.ClaimAdministratorAsync(code, ct));
}
