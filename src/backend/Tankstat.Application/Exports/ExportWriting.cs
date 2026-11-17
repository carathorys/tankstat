using Tankstat.Application.Backups;

namespace Tankstat.Application.Exports;

/// <summary>What of a car goes into an export (#341): every kind by default, each a box on the page.</summary>
public sealed record ExportKinds(
    bool Refuelings = true, bool Expenses = true, bool Schedules = true, bool Tyres = true, bool Charts = true, bool Photos = true, bool Picture = true)
{
    public static readonly ExportKinds All = new();

    /// <summary>At least one kind is ticked (<c>export.noKinds</c> otherwise).</summary>
    public bool Any => Refuelings || Expenses || Schedules || Tyres || Charts || Photos || Picture;
}

/// <summary>A car of an export with what of it goes.</summary>
public sealed record ExportCar(Guid VehicleId, ExportKinds Kinds);

/// <summary>
/// What the writer is asked for: whose export it is (null with sign-in off: no user row goes in), the cars (the exporter's own, checked
/// before), the key it is encrypted to (null: in plain) and the file to write.
/// </summary>
public sealed record ExportWriteRequest(Guid? ExporterId, IReadOnlyList<ExportCar> Cars, BackupEncryption? Encryption, string Output);

/// <summary>How far a write is: the car whose files are being copied, and the photos (and pictures) copied of all.</summary>
public sealed record ExportProgress(int Car, int Cars, int Done, int Total);

/// <summary>A written export: its size, how many cars, and every table's rows (the manifest's counts).</summary>
public sealed record ExportWritten(long Bytes, int Cars, IReadOnlyDictionary<string, long> Rows);

/// <summary>
/// Writes an export (#341): a backup-shaped file of chosen cars, marked as an export (<see cref="BackupManifest.Export"/>) so that no release
/// restores it as a backup, which the reader of cars (#336) takes. Only what the person may take with them goes in; the trash never.
/// </summary>
public interface IExportWriter
{
    Task<ExportWritten> WriteAsync(ExportWriteRequest request, Action<ExportProgress> progress, CancellationToken ct);
}
