using System.Globalization;
using Tankstat.Application.Backups;

namespace Tankstat.Api.GraphQL;

/// <param name="File">A backup's file name in the backups folder.</param>
/// <param name="Passphrase">The passphrase of an encrypted backup, or <paramref name="Identity"/> (the <c>AGE-SECRET-KEY-1...</c> line) instead.</param>
/// <param name="Code">With sign-in off, or while the setup waits: the one-time code from the server's log.</param>
public sealed record RestoreBackupInput(string File, string? Passphrase, string? Identity, string? Code);

public sealed record RestorePendingInfo(string File, DateTimeOffset RestorePoint);

/// <summary>A backup in the folder as the administration page lists it. Nothing in it is personal: the manifest's counts and settings.</summary>
/// <param name="Migration">The name of the backup's last migration (without the provider's timestamp).</param>
/// <param name="SameMigration">It was made at the migration the database is at: restored as it is.</param>
/// <param name="OlderMigration">It was made at an older migration of this release: restored, then brought up to date. Neither: a newer release made it, which must restore it.</param>
/// <param name="Recipient">The recipient an encrypted backup is for; <paramref name="CurrentRecipient"/> says whether it is the one backups are encrypted for now.</param>
/// <param name="SettingsFile">Whether the settings file went in (none: written by a release before it did).</param>
/// <param name="Environment">What it recorded of its server's environment (none: written by a release before it did).</param>
/// <param name="Held">Held: retention never removes it, it counts towards no limit, and it is not deleted until released.</param>
/// <param name="HeldSince">Since when it is held.</param>
public sealed record RestorePointInfo(
    string File, long Bytes, DateTimeOffset CreatedAt, string AppVersion, string Provider, string? Migration, bool SameMigration, bool OlderMigration, bool SafetyNet, bool Uploaded,
    bool Encrypted, string? Recipient, bool CurrentRecipient, BackupPhotos Photos, bool Keys, int Tables, long Rows, BackupSettingsFileInfo? SettingsFile, BackupEnvironmentInfo? Environment,
    bool Held, DateTimeOffset? HeldSince)
{
    public static RestorePointInfo From(RestorePoint point, RestorePoints points, string? currentRecipient)
    {
        var m = point.Manifest;
        var same = MigrationNames.Same(m.Migration, points.DatabaseMigration);
        var older = !same && points.KnownMigrations.Any(known => MigrationNames.Same(known, m.Migration));
        return new RestorePointInfo(
            point.File, point.Bytes, m.CreatedAt, m.AppVersion, m.Provider, MigrationNames.Of(m.Migration), same, older, m.SafetyNet,
            point.Uploaded, m.Encryption is not null, m.Encryption?.Recipient, m.Encryption is { } e && e.Recipient == currentRecipient,
            m.Contents.Photos, m.Contents.Keys, m.Contents.Tables.Count, m.Contents.Tables.Where(t => t.Key != "__EFMigrationsHistory").Sum(t => t.Value),
            m.Settings is { } settings ? BackupSettingsFileInfo.From(settings, m.CarriesSecrets()) : null,
            m.Environment is { } environment ? BackupEnvironmentInfo.From(environment, points.Here.GetValueOrDefault(point.File)) : null,
            point.Held, point.HeldSince);
    }
}

/// <summary>
/// One backup in full, as far as its plain manifest says (its "open" view on the Backups page and the setup page): nothing is decrypted or
/// unpacked, so nothing personal is in it either, only counts, sizes, names of settings and the checksum.
/// </summary>
/// <param name="Tables">Each table's rows, by the database's table name (the migrations' own history left out).</param>
/// <param name="Sizes">Bytes per part before compression and encryption, and the inner archive as stored.</param>
/// <param name="Sha256">The SHA-256 of the inner archive as stored, which the restore checks first.</param>
/// <param name="Environment">What it recorded of its server's environment (what this server lacks of it is the list's, not this view's).</param>
public sealed record BackupDetailsInfo(
    string File, long Bytes, bool Held, DateTimeOffset? HeldSince, int Format, string WrittenBy, DateTimeOffset CreatedAt, string AppVersion, string Provider,
    string? Migration, bool SafetyNet, bool Uploaded, bool Encrypted, string? Recipient, bool CurrentRecipient, string? EncryptionMethod,
    bool Pictures, BackupPhotos Photos, bool Keys, IReadOnlyList<BackupTableInfo> Tables, BackupSizesInfo Sizes, string ArchiveEntry, string Sha256,
    BackupSettingsFileInfo? SettingsFile, BackupEnvironmentInfo? Environment)
{
    public static BackupDetailsInfo From(RestorePoint point, string? currentRecipient)
    {
        var m = point.Manifest;
        return new BackupDetailsInfo(
            point.File, point.Bytes, point.Held, point.HeldSince, m.Format, m.WrittenBy, m.CreatedAt, m.AppVersion, m.Provider, MigrationNames.Of(m.Migration),
            m.SafetyNet, point.Uploaded, m.Encryption is not null, m.Encryption?.Recipient, m.Encryption is { } e && e.Recipient == currentRecipient, m.Encryption?.Method,
            m.Contents.Pictures, m.Contents.Photos, m.Contents.Keys,
            [.. m.Contents.Tables.Where(t => t.Key != "__EFMigrationsHistory").OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => new BackupTableInfo(t.Key, t.Value))],
            new BackupSizesInfo(m.Sizes.Tables, m.Sizes.Pictures, m.Sizes.Photos, m.Sizes.Keys, m.Sizes.Archive), m.Archive.Name, m.Archive.Sha256,
            m.Settings is { } settings ? BackupSettingsFileInfo.From(settings, m.CarriesSecrets()) : null,
            m.Environment is { } environment ? BackupEnvironmentInfo.From(environment, null) : null);
    }
}

public sealed record BackupTableInfo(string Name, long Rows);

/// <summary>Bytes before compression and encryption: the tables, the pictures, the photos, the keys; and the inner archive as stored.</summary>
public sealed record BackupSizesInfo(long Tables, long Pictures, long Photos, long Keys, long Archive);

/// <param name="State">It went in, the server had none, or its start had left it out.</param>
/// <param name="Omitted">The settings of the file that stayed out (the machine's own; without its secrets, those), by name.</param>
/// <param name="Secrets">Whether its secrets went in (<c>Backups:Secrets</c> when it was written; an older backup's: when it is encrypted).</param>
/// <param name="SecretsLeftOut">Of <paramref name="Omitted"/>, the secrets: a restore keeps this server's own, and what this server has none of stops working.</param>
/// <param name="MachineLeftOut">Of <paramref name="Omitted"/>, the rest: the machine's own, which never travel (a restore keeps this server's).</param>
/// <param name="Bytes">The file's size as stored in the backup (none: 0).</param>
public sealed record BackupSettingsFileInfo(BackupSettingsState State, IReadOnlyList<string> Omitted, bool Secrets, IReadOnlyList<string> SecretsLeftOut, IReadOnlyList<string> MachineLeftOut, long Bytes)
{
    public static BackupSettingsFileInfo From(BackupSettingsFile settings, bool secrets)
    {
        var omitted = settings.Omitted ?? [];
        var leftOutSecrets = omitted.Where(k => SettingsInBackups.CategoryOf(k) == SettingCategory.Secret).ToList();
        return new BackupSettingsFileInfo(settings.State, omitted, secrets, leftOutSecrets, [.. omitted.Except(leftOutSecrets)], settings.Bytes);
    }
}

/// <param name="Mode">Whether the backup's settings file took the environment's values in too, or the names are all it holds.</param>
/// <param name="Settings">The instance's settings its server's environment set, by variable name.</param>
/// <param name="Server">The machine's own variables of its server (the database, the folders), which never travel.</param>
/// <param name="Missing">
/// Of <paramref name="Settings"/>, those a restore here would lack (neither this server's environment nor the backup's settings file holds
/// them), so it is refused until they are set; for an encrypted backup that only recorded them, as if its file held none, since it is read only
/// with the passphrase (<paramref name="MissingCertain"/> false then).
/// </param>
/// <param name="Overridden">
/// The settings of the backup's file that this server's environment overrides, by variable name: its values run, not the backup's (the
/// sign-in mode among them can leave the restored users without a way in). Empty where the file cannot be read without the passphrase.
/// </param>
public sealed record BackupEnvironmentInfo(
    BackupEnvironmentMode Mode, IReadOnlyList<string> Settings, IReadOnlyList<string> Server, IReadOnlyList<string> Missing, bool MissingCertain, IReadOnlyList<string> Overridden)
{
    public static BackupEnvironmentInfo From(BackupEnvironment environment, RestoreHere? here) =>
        new(environment.Mode, environment.Settings, environment.Server, here?.Missing ?? [], here?.MissingCertain ?? true, here?.Overridden ?? []);
}

/// <summary>Bytes of the database (null: the provider would not say), of the pictures, and of the photos when they have a folder of their own.</summary>
public sealed record StorageSizesInfo(long? Database, long Pictures, long? Photos);

public sealed record BackupRunInfo(bool Running, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? File, long? Bytes, string? Error);

/// <param name="Recipient">Backups are encrypted for this recipient (<c>Backups:Encryption:Recipient</c>); none: in plain, without the keys.</param>
/// <param name="PhotosApart">The photos of logs have a folder of their own (<c>Storage:PhotosPath</c>), so they are only in a backup with <paramref name="Photos"/>.</param>
/// <param name="Times">The times of day (<c>HH:mm</c>, the server's local time), earliest first, a backup is written on each of <paramref name="Days"/>; none: nothing is scheduled.</param>
/// <param name="Days">The days of the week of the schedule, from Monday; none: nothing is scheduled.</param>
/// <param name="TimeZone">The server's time zone (an IANA id such as <c>Europe/Budapest</c>, or <c>UTC</c>), which the times are in.</param>
/// <param name="NextRun">When the schedule writes the next backup.</param>
/// <param name="Keep">After a backup the app wrote, only the newest this many of its own stay.</param>
/// <param name="KeepDays">After a backup the app wrote, its own older than this many days go.</param>
/// <param name="Environment">What a backup does with the settings environment variables hold (<c>Backups:Environment</c>).</param>
/// <param name="Secrets">Whether a backup's settings file holds the secrets (<c>Backups:Secrets</c>, encrypted or not).</param>
public sealed record BackupSettingsInfo(
    string? Recipient, bool PhotosApart, bool Photos, int MaxUploadMb, IReadOnlyList<string> Times, IReadOnlyList<DayOfWeek> Days, string TimeZone, DateTimeOffset? NextRun, int? Keep, int? KeepDays,
    BackupEnvironmentMode Environment, bool Secrets);

/// <summary>The backups of the administration page. <c>folder</c>, <c>run</c> and <c>settings</c> cost nothing (the page polls <c>run</c> while a backup is written); the rest is worked out only when selected.</summary>
public sealed record BackupOverviewInfo(string Folder, BackupRunInfo Run, BackupSettingsInfo Settings)
{
    public static BackupOverviewInfo From(BackupStatus s)
    {
        var last = s.Run.Last;
        var schedule = s.Settings.Schedule;
        return new BackupOverviewInfo(
            s.Folder,
            new BackupRunInfo(s.Run.Running, last?.StartedAt, last?.FinishedAt, last?.File, last?.Bytes, last?.Error),
            new BackupSettingsInfo(
                s.Settings.CurrentRecipient, s.PhotosApart, s.Settings.Photos, s.Settings.MaxUploadMb,
                // a culture's time separator (fi-FI: 02.00) is not the setting's
                schedule?.Times.Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)).ToList() ?? [], schedule?.Days ?? [], s.TimeZone,
                s.NextRun, s.Settings.Keep, s.Settings.KeepDays, s.Settings.Environment, s.Settings.Secrets));
    }
}

[ExtendObjectType<BackupOverviewInfo>]
public sealed class BackupOverviewInfoExtensions
{
    /// <summary>The backups in the folder, newest first: every file whose manifest reads (one copied in by hand too).</summary>
    public async Task<IReadOnlyList<RestorePointInfo>> GetRestorePoints([Parent] BackupOverviewInfo overview, [Service] BackupAdminService backups, CancellationToken ct)
    {
        var points = await backups.RestorePointsAsync(ct);
        return [.. points.Points.Select(p => RestorePointInfo.From(p, points, overview.Settings.Recipient))];
    }

    /// <summary>The name of the migration the database is at; a backup made at an older one is brought up to date as it is restored, one made at a newer one (a newer release's) cannot be restored here.</summary>
    public async Task<string?> GetMigration([Service] BackupAdminService backups, CancellationToken ct) =>
        MigrationNames.Of(await backups.MigrationAsync(ct));

    public async Task<StorageSizesInfo> GetSizes([Service] BackupAdminService backups, CancellationToken ct)
    {
        var sizes = await backups.SizesAsync(ct);
        return new StorageSizesInfo(sizes.Database, sizes.Files.Pictures, sizes.Files.Photos);
    }
}

/// <summary>A key set shown once: the passphrase (<paramref name="Generated"/>: made up by the server), the identity, and the two settings to set.</summary>
public sealed record BackupKeySetInfo(string Passphrase, bool Generated, string Identity, string Recipient, string Salt);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class BackupQueries
{
    /// <summary>The restore points in the backups folder, how large the data is, the last backup and the settings. Administrators only.</summary>
    public async Task<BackupOverviewInfo> GetBackups([Service] BackupAdminService backups, CancellationToken ct) => BackupOverviewInfo.From(await backups.StatusAsync(ct));

    /// <summary>One backup of the folder in full, from its manifest (<c>backup.notFound</c> for a name that is no backup there). Administrators only.</summary>
    public async Task<BackupDetailsInfo> GetBackupDetails(string file, [Service] BackupAdminService backups, CancellationToken ct)
    {
        var (point, recipient) = await backups.DetailsAsync(file, ct);
        return BackupDetailsInfo.From(point, recipient);
    }
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class BackupMutations
{
    /// <summary>
    /// Asks for a restore of a backup from the backups folder: the data is replaced as a whole at the next start, which follows right after
    /// the answer (the server restarts in its process, as <c>restartServer</c> does). An administrator; with sign-in off or while the setup waits, the one-time code
    /// from the log. A wrong passphrase is <c>backup.wrongKey</c> at once, a backup a newer release made <c>backup.otherMigration</c>, one
    /// deleted meanwhile <c>backup.notFound</c>, a restart the next start would refuse <c>settings.restartWouldFail</c>; nothing is written then.
    /// </summary>
    public async Task<RestorePendingInfo> RestoreBackup(RestoreBackupInput input, [Service] RestoreService restores, CancellationToken ct)
    {
        var pending = await restores.RequestAsync(new RestoreRequest(input.File, input.Passphrase, input.Identity, input.Code), ct);
        return new RestorePendingInfo(pending.File, pending.RestorePoint);
    }

    /// <summary>Writes a backup now, in the background (the page follows it in <c>backups.run</c>); false when one is being written already.</summary>
    public Task<bool> RunBackup([Service] BackupAdminService backups, CancellationToken ct) => backups.RunNowAsync(ct);

    /// <summary>
    /// Deletes a backup from the backups folder (any the page lists, uploaded ones and safety nets too). <c>backup.notFound</c> for a name
    /// that is no backup there, <c>backup.held</c> for a held one, <c>backup.restorePending</c> while a restore waits for the next start. Administrators only.
    /// </summary>
    public async Task<bool> DeleteBackup(string file, [Service] BackupAdminService backups, CancellationToken ct)
    {
        await backups.DeleteAsync(file, ct);
        return true;
    }

    /// <summary>
    /// Holds a backup (<paramref name="hold"/>) or releases it: held, retention never removes it and it counts towards neither
    /// <c>Backups:Keep</c> nor <c>Backups:KeepDays</c>, and <c>deleteBackup</c> refuses it (<c>backup.held</c>). Answers whether it is held now. Administrators only.
    /// </summary>
    public async Task<bool> HoldBackup(string file, bool hold, [Service] BackupAdminService backups, CancellationToken ct) =>
        (await backups.HoldAsync(file, hold, ct)).Held;

    /// <summary>
    /// A key set for encrypted backups from the passphrase given (at least 12 characters), or from one the server makes up: shown once, stored
    /// nowhere. The two settings it names make every later backup encrypted for it. Administrators only.
    /// </summary>
    public async Task<BackupKeySetInfo> PrepareBackupEncryption(string? passphrase, [Service] BackupAdminService backups, CancellationToken ct)
    {
        var set = await backups.PrepareEncryptionAsync(passphrase, ct);
        return new BackupKeySetInfo(set.Passphrase, set.Generated, set.Identity, set.Recipient, set.Salt);
    }
}
