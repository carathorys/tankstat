using Tankstat.Application.Backups;

namespace Tankstat.Api.GraphQL;

/// <param name="File">A backup's file name in the backups folder.</param>
/// <param name="Passphrase">The passphrase of an encrypted backup, or <paramref name="Identity"/> (the <c>AGE-SECRET-KEY-1...</c> line) instead.</param>
/// <param name="Code">With sign-in off: the one-time restore code from the server's log.</param>
public sealed record RestoreBackupInput(string File, string? Passphrase, string? Identity, string? Code);

public sealed record RestorePendingInfo(string File, DateTimeOffset RestorePoint);

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class BackupMutations
{
    /// <summary>
    /// Asks for a restore of a backup from the backups folder: the data is replaced as a whole at the next start, which follows right after
    /// the answer (the app stops itself; the container's restart policy brings it back). An administrator; with sign-in off, the one-time
    /// code from the log. A wrong passphrase is <c>backup.wrongKey</c> at once, nothing is written then.
    /// </summary>
    public async Task<RestorePendingInfo> RestoreBackup(
        RestoreBackupInput input, [Service] RestoreService restores, [Service] IHostApplicationLifetime lifetime, [Service] IHttpContextAccessor http, CancellationToken ct)
    {
        var pending = await restores.RequestAsync(new RestoreRequest(input.File, input.Passphrase, input.Identity, input.Code), ct);
        // The answer goes out first: the app stops once the response is written (stopping inside the resolver could cut it short).
        if (http.HttpContext is { } context) context.Response.OnCompleted(() => { lifetime.StopApplication(); return Task.CompletedTask; });
        else lifetime.StopApplication();
        return new RestorePendingInfo(pending.File, pending.RestorePoint);
    }
}
