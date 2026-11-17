using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Imports;
using Tankstat.Domain;
using Tankstat.Domain.Backups;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Exports;

/// <summary>Where an export stands.</summary>
public enum ExportState { Building, Ready, Failed, Cancelled, Interrupted, Expired }

/// <summary>
/// The record of a person's export (#341), kept beside its file as <c>&lt;id&gt;.json</c>: whose it is (<see cref="Guid.Empty"/> with sign-in
/// off), its state, the cars' names, its size and key, when it was started, made and is (or was) deleted, and why it failed. It outlives
/// the file until the person's next export, so the page can say the file was deleted, or that the server restarted while it was built.
/// </summary>
public sealed record ExportRecord(
    Guid Id, Guid OwnerId, ExportState State, IReadOnlyList<string> CarNames, bool Encrypted, DateTimeOffset StartedAt,
    DateTimeOffset? ReadyAt = null, DateTimeOffset? KeptUntil = null, DateTimeOffset? DeletedAt = null, long? Bytes = null, string? FailureKey = null);

/// <summary>The kept exports and their records, in <c>Exports:Folder</c>.</summary>
public interface IExportFiles
{
    IReadOnlyList<ExportRecord> Records();
    void Save(ExportRecord record);
    string FileOf(Guid id);
    /// <summary>The finished file, when it is there.</summary>
    Stream? OpenRead(Guid id);
    void DeleteFile(Guid id);
    void DeleteRecord(Guid id);
    /// <summary>Removes files a stopped run left half written.</summary>
    void RemovePartials();
}

/// <summary>A job the runner builds: the record it ends, the request it writes, the exporter's name for the administrators' notification.</summary>
public sealed record ExportJob(ExportRecord Record, ExportWriteRequest Request, string? ExporterName);

/// <summary>The exports being built on this server, in the background.</summary>
public interface IExportRunner
{
    void Start(ExportJob job);
    /// <summary>Asks the person's export under way to stop; false when none is.</summary>
    bool Cancel(Guid ownerId);
    /// <summary>How far an export under way is; null when none of that id is.</summary>
    ExportProgress? ProgressOf(Guid id);
    bool IsRunning(Guid id);
}

/// <summary>A car of the person's own as the Export tab offers it: its counts, and what its photos and picture add.</summary>
public sealed record ExportCarOffer(
    Guid Id, string Name, string? LicensePlate, int Refuelings, int Expenses, int Schedules, int TyreSets, int Charts, int Photos, long PhotoBytes, long PictureBytes, long RowBytes);

/// <summary>The cars a person may export, by name (<see cref="Guid.Empty"/>: every car, with sign-in off).</summary>
public interface IExportFacts
{
    Task<IReadOnlyList<ExportCarOffer>> OwnCarsAsync(Guid? ownerId, CancellationToken ct);
}

/// <summary>
/// A person's export as the page sees it: its record, how far it is while it is built, and whether this server's import would take it (the
/// owner's decision 8: said on the ready file).
/// </summary>
public sealed record ExportView(ExportRecord Record, ExportProgress? Progress, bool LargerThanImport, int ImportMaxMb);

/// <summary>
/// Export my data (#341): a person's own cars as a file, chosen car by car and kind by kind, optionally encrypted with a passphrase, built in
/// the background and kept for <c>Exports:KeepHours</c>. One export per person: a new one replaces it, the earlier file deleted only once
/// the new one is written. Every user may, unless <c>Exports:Enabled</c> is off (administrators always may; with sign-in off, nobody then).
/// </summary>
public sealed class ExportService(
    AccessService access, IExportFacts facts, IExportFiles files, IExportRunner runner, IBackupKeyMaker keys, IOptionsSnapshot<ExportOptions> options,
    IOptionsSnapshot<BackupImportOptions> imports, IOptionsSnapshot<BackupOptions> backups, TimeProvider clock, ILogger<ExportService> logger)
{
    public const string DisabledKey = "export.disabled", NoCarsKey = "export.noCars", NoKindsKey = "export.noKinds";

    /// <summary>Whether the signed-in person may export at all.</summary>
    public async Task<bool> MayExportAsync(CancellationToken ct) => May(await access.RequirePrincipalAsync(ct));

    private bool May(Principal me) => options.Value.Enabled || (me.IsAdmin && !me.IsAnonymous);

    private async Task<Principal> RequireExporterAsync(CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        if (!May(me)) throw new ForbiddenException(DisabledKey, "Exporting is switched off on this server.");
        return me;
    }

    /// <summary>The person's own cars (with sign-in off every car), what each holds and what its photos and picture add.</summary>
    public async Task<IReadOnlyList<ExportCarOffer>> CarsAsync(CancellationToken ct)
    {
        var me = await RequireExporterAsync(ct);
        return await facts.OwnCarsAsync(me.IsAnonymous ? null : me.Id, ct);
    }

    /// <summary>
    /// Starts an export of the chosen cars: own cars only, a kind each; a passphrase typed twice by the backups' rules (both empty: in
    /// plain). The key is derived here and only the recipient goes on: the passphrase and the identity are kept nowhere.
    /// </summary>
    public async Task<ExportView> StartAsync(IReadOnlyList<ExportCar> cars, string? passphrase, string? repeated, CancellationToken ct)
    {
        var me = await RequireExporterAsync(ct);
        if (cars.Count == 0) throw new DomainException(NoCarsKey, "Tick at least one car.");
        var own = (await facts.OwnCarsAsync(me.IsAnonymous ? null : me.Id, ct)).ToDictionary(c => c.Id);
        foreach (var car in cars)
        {
            if (!own.TryGetValue(car.VehicleId, out var offer)) throw new NotFoundException("vehicle.notFound", $"Vehicle {car.VehicleId} does not exist.", new { Id = car.VehicleId });
            if (!car.Kinds.Any) throw new DomainException(NoKindsKey, "Tick at least one kind for this car.", new { Car = offer.Name });
        }
        if (cars.GroupBy(c => c.VehicleId).Any(g => g.Count() > 1)) throw new DomainException("export.carTwice", "A car can be chosen once.");
        BackupEncryption? encryption = null;
        if (!string.IsNullOrEmpty(passphrase) || !string.IsNullOrEmpty(repeated))
        {
            PasswordPolicy.RequireRepeated(passphrase, repeated);
            BackupPassphrase.Validate(passphrase!, backups.Value.PassphraseMinLength);
            var set = keys.FromPassphrase(passphrase!);
            encryption = new BackupEncryption(BackupEncryption.AgeX25519, set.Recipient, set.Salt, Argon2idParameters.Default);
        }

        var owner = me.IsAnonymous ? Guid.Empty : me.Id;
        Sweep();
        runner.Cancel(owner); // one export per person: the one under way gives way
        // What is left of the person's earlier export besides a finished file: only the newest record is kept, so the page says one thing.
        foreach (var old in files.Records().Where(r => r.OwnerId == owner && r.State != ExportState.Ready)) files.DeleteRecord(old.Id);
        var id = Guid.NewGuid();
        var record = new ExportRecord(id, owner, ExportState.Building, [.. cars.Select(c => own[c.VehicleId].Name)], encryption is not null, clock.GetUtcNow());
        files.Save(record);
        runner.Start(new ExportJob(record, new ExportWriteRequest(me.IsAnonymous ? null : me.Id, cars, encryption, files.FileOf(id)), me.IsAnonymous ? null : me.DisplayName));
        logger.LogInformation("User {UserId} started export {ExportId}: {Cars} cars, {Encryption}", owner, id, cars.Count, encryption is null ? "in plain" : "encrypted");
        return View(record);
    }

    /// <summary>The person's export: under way, ready, or how the last one ended; null when there is none.</summary>
    public async Task<ExportView?> MineAsync(CancellationToken ct)
    {
        var me = await RequireExporterAsync(ct);
        Sweep();
        var owner = me.IsAnonymous ? Guid.Empty : me.Id;
        return Latest(owner) is { } record ? View(record) : null;
    }

    /// <summary>Asks the person's export under way to stop: nothing of it is kept, the earlier file stays.</summary>
    public async Task<bool> CancelAsync(CancellationToken ct)
    {
        var me = await RequireExporterAsync(ct);
        return runner.Cancel(me.IsAnonymous ? Guid.Empty : me.Id);
    }

    /// <summary>Deletes the person's finished file now (and its record): the page offers a new export.</summary>
    public async Task<bool> DeleteAsync(CancellationToken ct)
    {
        var me = await RequireExporterAsync(ct);
        var owner = me.IsAnonymous ? Guid.Empty : me.Id;
        var deleted = false;
        foreach (var record in files.Records().Where(r => r.OwnerId == owner && !runner.IsRunning(r.Id)))
        {
            files.DeleteFile(record.Id);
            files.DeleteRecord(record.Id);
            deleted = true;
        }
        if (deleted) logger.LogInformation("User {UserId} deleted their export", owner);
        return deleted;
    }

    /// <summary>The finished file of an export for its download: only for the person who made it (anyone else's looks non-existent).</summary>
    public async Task<(Stream Content, string Name)> DownloadAsync(Guid id, CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        Sweep();
        var owner = me.IsAnonymous ? Guid.Empty : me.Id;
        var record = files.Records().FirstOrDefault(r => r.Id == id && r.OwnerId == owner && r.State == ExportState.Ready);
        var content = record is null ? null : files.OpenRead(id);
        if (content is null) throw new NotFoundException("export.notFound", "There is no such export.");
        return (content, $"tankstat-export-{record!.StartedAt.UtcDateTime:yyyy-MM-dd}.zip");
    }

    private ExportRecord? Latest(Guid owner) =>
        files.Records().Where(r => r.OwnerId == owner).OrderByDescending(r => r.StartedAt).ThenBy(r => r.Id).FirstOrDefault();

    private ExportView View(ExportRecord record) =>
        new(record, runner.ProgressOf(record.Id), record.Bytes > imports.Value.MaxUploadBytes, imports.Value.MaxUploadMb);

    /// <summary>
    /// Brings the folder up to date, as anyone starts or looks: a file past its time is deleted (its record says when), an export no runner
    /// builds any more was cut short by a restart, and what a stopped run left half written goes.
    /// </summary>
    public void Sweep()
    {
        var now = clock.GetUtcNow();
        foreach (var record in files.Records())
        {
            if (record.State == ExportState.Ready && record.KeptUntil <= now)
            {
                files.DeleteFile(record.Id);
                files.Save(record with { State = ExportState.Expired, DeletedAt = now });
                logger.LogInformation("Export {ExportId} expired and was deleted", record.Id);
            }
            else if (record.State == ExportState.Building && !runner.IsRunning(record.Id))
                files.Save(record with { State = ExportState.Interrupted });
        }
        files.RemovePartials();
    }
}
