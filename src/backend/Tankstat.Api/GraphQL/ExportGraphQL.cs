using Tankstat.Application.Exports;

namespace Tankstat.Api.GraphQL;

/// <summary>A car of the person's own on the Export tab (#341): its counts, and the bytes its photos and picture add.</summary>
public sealed record ExportCarOfferInfo(
    Guid Id, string Name, string? LicensePlate, int Refuelings, int Expenses, int Schedules, int TyreSets, int Charts, int Photos, long PhotoBytes, long PictureBytes, long RowBytes);

public sealed record ExportProgressInfo(int Car, int Cars, int Done, int Total);

/// <summary>
/// The person's export: building (with its progress), ready (its size, key and how long it is kept; <c>downloadUrl</c> for the file;
/// <c>largerThanImport</c> when this server's import would refuse it), or how the last one ended (<c>failureKey</c>, <c>deletedAt</c>).
/// </summary>
public sealed record ExportInfo(
    Guid Id, ExportState State, IReadOnlyList<string> CarNames, bool Encrypted, DateTimeOffset StartedAt, DateTimeOffset? ReadyAt, DateTimeOffset? KeptUntil,
    DateTimeOffset? DeletedAt, long? Bytes, string? FailureKey, ExportProgressInfo? Progress, bool LargerThanImport, int ImportMaxMb, string? DownloadUrl)
{
    public static ExportInfo From(ExportView v) => new(
        v.Record.Id, v.Record.State, v.Record.CarNames, v.Record.Encrypted, v.Record.StartedAt, v.Record.ReadyAt, v.Record.KeptUntil, v.Record.DeletedAt, v.Record.Bytes,
        v.Record.FailureKey, v.Progress is { } p ? new ExportProgressInfo(p.Car, p.Cars, p.Done, p.Total) : null, v.LargerThanImport, v.ImportMaxMb,
        v.Record.State == ExportState.Ready ? $"/media/exports/{v.Record.Id}" : null);
}

/// <summary>A car to export and what of it goes; a kind left out (null) goes.</summary>
public sealed record ExportCarInput(
    Guid VehicleId, bool? Refuelings = null, bool? Expenses = null, bool? Schedules = null, bool? Tyres = null, bool? Charts = null, bool? Photos = null, bool? Picture = null)
{
    public ExportCar ToCar() => new(VehicleId, new ExportKinds(Refuelings ?? true, Expenses ?? true, Schedules ?? true, Tyres ?? true, Charts ?? true, Photos ?? true, Picture ?? true));
}

/// <param name="Passphrase">Optional: with one, typed twice by the backups' rules, the file is encrypted. Used for this request and kept nowhere.</param>
public sealed record StartExportInput(IReadOnlyList<ExportCarInput> Cars, string? Passphrase = null, string? PassphraseConfirmation = null);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class ExportQueries
{
    /// <summary>Whether the signed-in person may export their cars (<c>Exports:Enabled</c>; administrators always may).</summary>
    public Task<bool> GetCanExport([Service] ExportService exports, CancellationToken ct) => exports.MayExportAsync(ct);

    /// <summary>The person's own cars (every car with sign-in off), with what each holds and what its photos and picture add.</summary>
    public async Task<IReadOnlyList<ExportCarOfferInfo>> GetExportCars([Service] ExportService exports, CancellationToken ct) =>
        [.. (await exports.CarsAsync(ct)).Select(c => new ExportCarOfferInfo(c.Id, c.Name, c.LicensePlate, c.Refuelings, c.Expenses, c.Schedules, c.TyreSets, c.Charts,
            c.Photos, c.PhotoBytes, c.PictureBytes, c.RowBytes))];

    /// <summary>The person's export under way, ready, or how the last one ended; null when there is none.</summary>
    public async Task<ExportInfo?> GetMyExport([Service] ExportService exports, CancellationToken ct) =>
        await exports.MineAsync(ct) is { } v ? ExportInfo.From(v) : null;
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class ExportMutations
{
    /// <summary>Starts an export of the chosen cars in the background; it replaces the person's earlier one once it is written.</summary>
    public async Task<ExportInfo> StartExport(StartExportInput input, [Service] ExportService exports, CancellationToken ct) =>
        ExportInfo.From(await exports.StartAsync([.. input.Cars.Select(c => c.ToCar())], input.Passphrase, input.PassphraseConfirmation, ct));

    /// <summary>Stops the person's export under way: nothing of it is kept. False when none is.</summary>
    public Task<bool> CancelExport([Service] ExportService exports, CancellationToken ct) => exports.CancelAsync(ct);

    /// <summary>Deletes the person's finished export from the server now.</summary>
    public Task<bool> DeleteExport([Service] ExportService exports, CancellationToken ct) => exports.DeleteAsync(ct);
}
