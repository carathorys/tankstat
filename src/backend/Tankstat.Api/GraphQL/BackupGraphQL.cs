using System.Globalization;
using Tankstat.Application.Backups;

namespace Tankstat.Api.GraphQL;

/// <param name="File">A backup's file name in the backups folder.</param>
/// <param name="Passphrase">The passphrase of an encrypted backup, or <paramref name="Identity"/> (the <c>AGE-SECRET-KEY-1...</c> line) instead.</param>
/// <param name="Code">With sign-in off: the one-time restore code from the server's log.</param>
public sealed record RestoreBackupInput(string File, string? Passphrase, string? Identity, string? Code);

public sealed record RestorePendingInfo(string File, DateTimeOffset RestorePoint);

/// <summary>A backup in the folder as the administration page lists it. Nothing in it is personal: the manifest's counts and settings.</summary>
/// <param name="Migration">The name of the backup's last migration (without the provider's timestamp).</param>
/// <param name="SameMigration">Whether it can be restored here: it was made at the migration the database is at.</param>
/// <param name="Recipient">The recipient an encrypted backup is for; <paramref name="CurrentRecipient"/> says whether it is the one backups are encrypted for now.</param>
public sealed record RestorePointInfo(
    string File, long Bytes, DateTimeOffset CreatedAt, string AppVersion, string Provider, string? Migration, bool SameMigration, bool SafetyNet, bool Uploaded,
    bool Encrypted, string? Recipient, bool CurrentRecipient, BackupPhotos Photos, bool Keys, int Tables, long Rows)
{
    public static RestorePointInfo From(RestorePoint point, string? databaseMigration, string? currentRecipient)
    {
        var m = point.Manifest;
        return new RestorePointInfo(
            point.File, point.Bytes, m.CreatedAt, m.AppVersion, m.Provider, MigrationNames.Of(m.Migration), MigrationNames.Same(m.Migration, databaseMigration), m.SafetyNet,
            point.Uploaded, m.Encryption is not null, m.Encryption?.Recipient, m.Encryption is { } e && e.Recipient == currentRecipient,
            m.Contents.Photos, m.Contents.Keys, m.Contents.Tables.Count, m.Contents.Tables.Where(t => t.Key != "__EFMigrationsHistory").Sum(t => t.Value));
    }
}

/// <summary>Bytes of the database (null: the provider would not say), of the pictures, and of the photos when they have a folder of their own.</summary>
public sealed record StorageSizesInfo(long? Database, long Pictures, long? Photos);

public sealed record BackupRunInfo(bool Running, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? File, long? Bytes, string? Error);

/// <param name="Recipient">Backups are encrypted for this recipient (<c>Backups:Encryption:Recipient</c>); none: in plain, without the keys.</param>
/// <param name="PhotosApart">The photos of logs have a folder of their own (<c>Storage:PhotosPath</c>), so they are only in a backup with <paramref name="Photos"/>.</param>
/// <param name="Every">The time of day (<c>HH:mm</c>, the server's local time) a backup is written every day; none: nothing is scheduled.</param>
/// <param name="NextRun">When the schedule writes the next backup.</param>
/// <param name="Keep">After a backup the app wrote, only the newest this many of its own stay.</param>
/// <param name="KeepDays">After a backup the app wrote, its own older than this many days go.</param>
public sealed record BackupSettingsInfo(string? Recipient, bool PhotosApart, bool Photos, int MaxUploadMb, string? Every, DateTimeOffset? NextRun, int? Keep, int? KeepDays);

/// <summary>The backups of the administration page. <c>folder</c>, <c>run</c> and <c>settings</c> cost nothing (the page polls <c>run</c> while a backup is written); the rest is worked out only when selected.</summary>
public sealed record BackupOverviewInfo(string Folder, BackupRunInfo Run, BackupSettingsInfo Settings)
{
    public static BackupOverviewInfo From(BackupStatus s)
    {
        var last = s.Run.Last;
        return new BackupOverviewInfo(
            s.Folder,
            new BackupRunInfo(s.Run.Running, last?.StartedAt, last?.FinishedAt, last?.File, last?.Bytes, last?.Error),
            new BackupSettingsInfo(
                s.Settings.Encrypted ? s.Settings.Encryption.Recipient!.Trim() : null, s.PhotosApart, s.Settings.Photos, s.Settings.MaxUploadMb,
                s.Settings.DailyAt?.ToString("HH:mm", CultureInfo.InvariantCulture), s.NextRun, s.Settings.Keep, s.Settings.KeepDays)); // a culture's time separator (fi-FI: 02.00) is not the setting's
    }
}

[ExtendObjectType<BackupOverviewInfo>]
public sealed class BackupOverviewInfoExtensions
{
    /// <summary>The backups in the folder, newest first: every file whose manifest reads (one copied in by hand too).</summary>
    public async Task<IReadOnlyList<RestorePointInfo>> GetRestorePoints([Parent] BackupOverviewInfo overview, [Service] BackupAdminService backups, CancellationToken ct)
    {
        var points = await backups.RestorePointsAsync(ct);
        return [.. points.Points.Select(p => RestorePointInfo.From(p, points.DatabaseMigration, overview.Settings.Recipient))];
    }

    /// <summary>The name of the migration the database is at; a backup made at another one cannot be restored here.</summary>
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
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class BackupMutations
{
    /// <summary>
    /// Asks for a restore of a backup from the backups folder: the data is replaced as a whole at the next start, which follows right after
    /// the answer (the server restarts in its process, as <c>restartServer</c> does). An administrator; with sign-in off, the one-time code
    /// from the log. A wrong passphrase is <c>backup.wrongKey</c> at once, a backup of another migration <c>backup.otherMigration</c>, a restart
    /// the next start would refuse <c>settings.restartWouldFail</c>; nothing is written then.
    /// </summary>
    public async Task<RestorePendingInfo> RestoreBackup(RestoreBackupInput input, [Service] RestoreService restores, CancellationToken ct)
    {
        var pending = await restores.RequestAsync(new RestoreRequest(input.File, input.Passphrase, input.Identity, input.Code), ct);
        return new RestorePendingInfo(pending.File, pending.RestorePoint);
    }

    /// <summary>Writes a backup now, in the background (the page follows it in <c>backups.run</c>); false when one is being written already.</summary>
    public Task<bool> RunBackup([Service] BackupAdminService backups, CancellationToken ct) => backups.RunNowAsync(ct);

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
