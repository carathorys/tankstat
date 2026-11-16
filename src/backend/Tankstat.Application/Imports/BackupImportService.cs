using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Domain;

namespace Tankstat.Application.Imports;

/// <summary>
/// The first step of importing cars from a backup or an export (#336): who may upload one, how large, and the person's open file. Every
/// user may, unless the instance turned it off (<c>Imports:BackupEnabled</c>; administrators always may). The file itself is the
/// sessions' business (<see cref="IBackupImportSessions"/>).
/// </summary>
public sealed class BackupImportService(
    AccessService access, IBackupImportSessions sessions, IOptionsSnapshot<BackupImportOptions> options, ILogger<BackupImportService> logger)
{
    public const string DisabledKey = "import.backupDisabled";

    /// <summary>
    /// Who is uploading, checked before a byte of the body is read: signed in, allowed by the switch, and not announcing more than the
    /// limit (<paramref name="announcedBytes"/>: the request's Content-Length, when it has one; the body is counted anyway).
    /// </summary>
    public async Task<Principal> RequireUploaderAsync(long? announcedBytes, CancellationToken ct)
    {
        var me = await RequireUserAsync(ct);
        if (announcedBytes > options.Value.MaxUploadBytes) throw TooLarge(options.Value.MaxUploadMb);
        return me;
    }

    public async Task<BackupImportStatus> UploadAsync(Principal uploader, Stream content, CancellationToken ct)
    {
        try
        {
            var status = await sessions.UploadAsync(uploader.Id, content, ct);
            logger.LogInformation("User {UserId} uploaded a file to import cars from (session {Session})", uploader.Id, status.Token);
            return status;
        }
        catch (KeyedException e)
        {
            logger.LogInformation("An upload of user {UserId} to import cars from was refused: {Key}", uploader.Id, e.Key);
            throw;
        }
    }

    /// <summary>The person's open file; <c>import.expired</c> when there is none any more.</summary>
    public async Task<BackupImportStatus> StatusAsync(string token, CancellationToken ct) =>
        sessions.Find((await RequireUserAsync(ct)).Id, token) ?? throw Expired();

    /// <summary>Tries a passphrase on the person's uploaded file (never an identity: the owner's decision 3).</summary>
    public async Task<BackupImportStatus> SubmitPassphraseAsync(string token, string? passphrase, CancellationToken ct) =>
        sessions.SubmitKey((await RequireUserAsync(ct)).Id, token, passphrase, null);

    /// <summary>Gives the file up: it is deleted from the server now.</summary>
    public async Task EndAsync(string token, CancellationToken ct) => sessions.End((await RequireUserAsync(ct)).Id, token);

    /// <summary>Whether the signed-in person may import from a backup at all (the page asks, to offer the source or not).</summary>
    public async Task<bool> MayImportAsync(CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        return options.Value.BackupEnabled || IsAdministrator(me);
    }

    private async Task<Principal> RequireUserAsync(CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        if (!options.Value.BackupEnabled && !IsAdministrator(me))
            throw new ForbiddenException(DisabledKey, "Importing from a backup is turned off on this server.");
        return me;
    }

    /// <summary>A real administrator: with sign-in off everyone acts as the anonymous one, and nobody is.</summary>
    private static bool IsAdministrator(Principal p) => p.IsAdmin && !p.IsAnonymous;

    public static DomainException TooLarge(int maxMb) => new("import.tooLarge", $"The file can be at most {maxMb} MB.", new { MaxMb = maxMb });

    public static DomainException Expired() => new("import.expired", "This import has expired. Choose the file again.");
}
