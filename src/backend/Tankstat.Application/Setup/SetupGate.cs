using Tankstat.Application.Auth;
using Tankstat.Domain;

namespace Tankstat.Application.Setup;

/// <summary>
/// What the one-time code opens while the setup waits, the one check every operation open to it makes (the setup's steps, the settings it
/// saves, its restart, a restore): the setup waits as the database says (another server may have completed it) and the code matches.
/// </summary>
public sealed class SetupGate(SetupStatus status, ISetupStore store, OneTimeCode code)
{
    /// <summary>Whether the setup still waits, asked of the database while this process thinks so.</summary>
    public Task<bool> WaitsAsync(CancellationToken ct) => status.PendingAsync(store, ct);

    /// <summary>The setup waits and the code matches; a wrong code is refused (the GraphQL listener logs the refusal, never what was typed).</summary>
    public async Task RequireAsync(string? typedCode, CancellationToken ct)
    {
        if (!await WaitsAsync(ct)) throw Completed();
        if (!code.Matches(typedCode)) throw new ForbiddenException("setup.codeInvalid", "The one-time setup code from the server's log is needed.");
    }

    public static DomainException Completed() => new("setup.completed", "The setup is completed already.");
}
