using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;

namespace Tankstat.Application.Backups;

/// <summary>What is asked for: a backup by its file name in the backups folder, the passphrase or the identity of an encrypted one, and with sign-in off the one-time code from the log.</summary>
public sealed record RestoreRequest(string File, string? Passphrase, string? Identity, string? Code);

/// <summary>The restore that now waits for the restart: the file and the time it takes the data back to.</summary>
public sealed record RestorePending(string File, DateTimeOffset RestorePoint);

/// <summary>
/// Asks for a restore: an administrator (with sign-in off, whoever holds the one-time code from the server's log, since there is no
/// administrator then) names a backup of the folder and, for an encrypted one, its passphrase or identity. The key is checked against the
/// manifest before anything is written, so a wrong passphrase is refused at once, never after a restart that then cannot decrypt; then the
/// marker is written (with the identity, which the restart needs) and the caller restarts the app. Neither the passphrase nor the identity
/// nor the code is ever logged.
/// </summary>
public sealed class RestoreService(IBackupStore store, AccessService access, IOptions<AuthOptions> auth, RestoreCode code, TimeProvider clock, ILogger<RestoreService> logger)
{
    public async Task<RestorePending> RequestAsync(RestoreRequest request, CancellationToken ct)
    {
        Guid actor;
        if (auth.Value.Mode == AuthMode.None)
        {
            if (!code.Matches(request.Code)) throw new ForbiddenException("backup.codeRequired", "The one-time restore code from the server's log is needed.");
            actor = Principal.Anonymous.Id;
        }
        else actor = (await access.RequireAdminAsync(ct)).Id;

        var manifest = store.ReadManifest(request.File);
        var identity = manifest.Encryption is null ? null : store.IdentityFor(manifest, request.Passphrase, request.Identity);
        store.RequestRestore(new RestoreMarker(request.File, actor, clock.GetUtcNow(), identity));
        logger.LogInformation("A restore of {File} (written {CreatedAt}) was asked for by user {UserId}; it is applied at the next start", request.File, manifest.CreatedAt, actor);
        return new RestorePending(request.File, manifest.CreatedAt);
    }
}
