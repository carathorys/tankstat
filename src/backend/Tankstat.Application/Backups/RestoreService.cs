using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Setup;
using Tankstat.Application.SystemSettings;
using Tankstat.Domain;

namespace Tankstat.Application.Backups;

/// <summary>What is asked for: a backup by its file name in the backups folder, the passphrase or the identity of an encrypted one, and with sign-in off or while the setup waits the one-time code from the log.</summary>
public sealed record RestoreRequest(string File, string? Passphrase, string? Identity, string? Code);

/// <summary>
/// What a backup of the folder would mean here (<see cref="RestoreService.HereAsync"/>): the recorded settings this server lacks (the restore is
/// refused for them; <see cref="MissingCertain"/> false where they could not be checked against an encrypted backup's file), and the settings
/// of its file that this server's environment overrides, by variable name.
/// </summary>
public sealed record RestoreHere(IReadOnlyList<string> Missing, bool MissingCertain, IReadOnlyList<string> Overridden);

/// <summary>The restore that now waits for the restart: the file and the time it takes the data back to.</summary>
public sealed record RestorePending(string File, DateTimeOffset RestorePoint);

/// <summary>
/// Asks for a restore: an administrator (with sign-in off, or while the setup waits, whoever holds the one-time code from the server's log,
/// since there is no administrator then) names a backup of the folder and, for an encrypted one, its passphrase or identity. The key is checked against the
/// manifest before anything is written, so a wrong passphrase is refused at once, never after a restart that then cannot decrypt; so is a
/// backup made by a newer release, at a migration this release does not know (the start would refuse it; an older one is brought up to
/// date), one deleted since its manifest was read, and a restart the next start would refuse, since a start that fails ends the process:
/// the settings file the backup carries, as the restore would leave it under this server's environment (<see cref="ISettingsBackup"/>,
/// <c>backup.settingsRefused</c>), or the settings file as it is now when the backup carries none. Then the marker is
/// written (with the identity, which the restart needs) and the server restarts in this process, as an administrator's restart does
/// (<see cref="IServerRestart"/>). Neither the passphrase nor the identity nor the code is ever logged.
/// </summary>
public sealed class RestoreService(
    IBackupStore store, AccessService access, IOptions<AuthOptions> auth, OneTimeCode code, SetupGate setup, ISystemConfiguration configuration,
    ISettingsBackup settings, IServerRestart restart, TimeProvider clock, ILogger<RestoreService> logger)
{
    public async Task<RestorePending> RequestAsync(RestoreRequest request, CancellationToken ct)
    {
        Guid actor;
        // While the setup waits as the database says: one another server completed is closed to the code here too, and an administrator
        // made there restores as administrators do.
        if (auth.Value.Mode == AuthMode.None || await setup.WaitsAsync(ct))
        {
            if (!code.Matches(request.Code)) throw new ForbiddenException("backup.codeRequired", "The one-time code from the server's log is needed.");
            actor = Principal.Anonymous.Id;
        }
        else actor = (await access.RequireAdminAsync(ct)).Id;

        var manifest = store.ReadManifest(request.File);
        // What the start would refuse anyway is refused now, not after a restart that comes back with the data as it was.
        await store.RequireRestorableMigrationAsync(manifest, ct);
        var identity = manifest.Encryption is null ? null : store.IdentityFor(manifest, request.Passphrase, request.Identity);
        // Only whether the file lets the server start again: who may sign in afterwards is the backup's business, its users replace these.
        await RequireStartableAsync(store, settings, configuration, request.File, manifest, identity, ct);
        store.RequestRestore(new RestoreMarker(request.File, actor, clock.GetUtcNow(), identity));
        logger.LogInformation("A restore of {File} (written {CreatedAt}) was asked for by user {UserId}; the server restarts to apply it", request.File, manifest.CreatedAt, actor);
        restart.Restart();
        return new RestorePending(request.File, manifest.CreatedAt);
    }

    /// <summary>
    /// Whether the server starts again after a restore of <paramref name="file"/>, with what it would run: the settings the backup recorded of
    /// its server's environment must each have a value here, from this environment or from the file the restore leaves
    /// (<c>backup.environmentMissing</c>, naming the variables: set them first); and the server must start with the settings file the backup
    /// carries, as the restore would leave it (<c>backup.settingsRefused</c>, naming the settings at fault), else with the settings file as
    /// it is now (<c>settings.restartWouldFail</c>). Shared with the <c>restore</c> command, which asks it before it writes the marker too.
    /// </summary>
    public static async Task RequireStartableAsync(
        IBackupStore store, ISettingsBackup settings, ISystemConfiguration configuration, string file, BackupManifest manifest, string? identity, CancellationToken ct)
    {
        var restored = store.ReadSettings(file, manifest, identity) is { } fromBackup ? await settings.RestoredAsync(fromBackup, manifest.CarriesSecrets(), ct) : null;
        await RequireRecordedAsync(settings, manifest, restored, ct);
        if (restored is not null)
        {
            if (restored.Problems.Count > 0)
                throw new DomainException("backup.settingsRefused",
                    $"The server would not start with the settings file this backup brings ({string.Join(" ", restored.Problems)}), so it is not restored.",
                    new { restored.Problems });
            return;
        }
        using var next = configuration.PrepareNextStart();
        next.RequireStartable();
    }

    /// <summary>
    /// For a list of the folder's backups, before anyone restores: what <paramref name="point"/> would lack here (the settings it recorded of its
    /// server's environment that neither this environment nor its file holds, which the restore is refused for) and what this server's
    /// environment overrides of its file, as far as can be told without a key. A plain backup's settings entry is read; an encrypted one's cannot
    /// be: one that merged the environment into it holds what it recorded (the secrets only if it carries them), one that only recorded counts as holding none, and then the missing ones are not
    /// certain (<see cref="RestoreHere.MissingCertain"/>: the restore judges the file once the passphrase is given), and nothing is known to be
    /// overridden. A backup whose file cannot be read lacks nothing here: its restore says why.
    /// </summary>
    public static async Task<RestoreHere> HereAsync(IBackupStore store, ISettingsBackup settings, RestorePoint point, CancellationToken ct)
    {
        var m = point.Manifest;
        try
        {
            IReadOnlyDictionary<string, string?>? file = null; // none: the restore leaves this server's own file
            var readable = true;
            if (m.Settings is { State: BackupSettingsState.Included })
            {
                if (m.Encryption is null) file = store.ReadSettings(point.File, m, identity: null);
                else readable = false;
            }
            var overridden = file is null ? [] : settings.Overridden(file);
            if (m.Environment is not { Settings.Count: > 0 } recorded) return new RestoreHere([], true, overridden);
            // Merged into a file that cannot be read without the passphrase: it holds every recorded setting, the secrets only when it was
            // asked to carry them.
            if (!readable && recorded.Mode == BackupEnvironmentMode.Merge)
            {
                var merged = recorded.Settings.Select(SettingsInBackups.KeyOf)
                    .Where(k => m.CarriesSecrets() || SettingsInBackups.CategoryOf(k) != SettingCategory.Secret)
                    .ToDictionary(k => k, string? (_) => "", StringComparer.OrdinalIgnoreCase);
                return new RestoreHere(await settings.MissingAsync(recorded, merged, ct), true, overridden);
            }
            var missing = await settings.MissingAsync(recorded, readable ? file : new Dictionary<string, string?>(), ct);
            return new RestoreHere(missing, readable, overridden);
        }
        catch (DomainException)
        {
            return new RestoreHere([], true, []);
        }
    }

    /// <summary>
    /// The refusal of a restore that would leave settings its backup recorded without a value (D4 of #140): <c>backup.environmentMissing</c>,
    /// the variables named. Shared with the start, which asks it again (its environment may not be the one the restore was asked under).
    /// </summary>
    public static async Task RequireRecordedAsync(ISettingsBackup settings, BackupManifest manifest, RestoredSettings? restored, CancellationToken ct)
    {
        if (manifest.Environment is not { } recorded) return;
        var missing = await settings.MissingAsync(recorded, restored?.Values, ct);
        if (missing.Count > 0)
            throw new DomainException("backup.environmentMissing",
                $"The backup was made on a server whose environment set {string.Join(", ", missing)}; this server sets none of them, nor does the settings file the backup brings. Set them, then restore.",
                new { Variables = string.Join(", ", missing) });
    }
}
