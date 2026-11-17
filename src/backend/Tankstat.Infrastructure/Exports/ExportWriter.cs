using System.Data;
using System.Data.Common;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Backups;
using Tankstat.Application.Exports;
using Tankstat.Application.Health;
using Tankstat.Application.Images;
using Tankstat.Domain.Backups;
using Tankstat.Domain.Images;
using Tankstat.Domain.Photos;
using Tankstat.Infrastructure.Backups;
using Tankstat.Infrastructure.Backups.Reader;
using Tankstat.Infrastructure.Persistence;
using Tankstat.Infrastructure.Persistence.Repositories;

namespace Tankstat.Infrastructure.Exports;

/// <summary>
/// The export's file (#341), in a backup's shape so the reader of cars (#336) loads it through the restore's own steps: an outer stored zip
/// with the inner archive (plain, or encrypted with age to the person's key) and the manifest, marked as an export. In the inner archive
/// every table of the model has its file: the cars' rows that go, chosen per table and kind (never what was in the trash: a tyre row by
/// the tyre book's own liveness), the exporter's own user row reduced as the reader reduces it, the migrations history whole, every other
/// table empty with a count of 0; then each photo and picture at the path the reader looks it up by, built from ids.
/// <para>
/// One read transaction gives the rows one snapshot, and only reads: SQLite's deferred one, which takes no lock to write (a backup's copy
/// of the whole file is never made for one person's cars), REPEATABLE READ on PostgreSQL and MySQL, SNAPSHOT on SQL Server where the
/// database allows it, else READ COMMITTED (a backup's SERIALIZABLE would hold every writer up). The files are copied after it.
/// </para>
/// </summary>
internal sealed class ExportWriter(
    IDbContextFactory<AppDbContext> dbFactory, IOptions<DatabaseOptions> database, IImageStore store, TimeProvider clock, ILogger<ExportWriter> logger) : IExportWriter
{
    private const int Chunk = 500;

    /// <summary>The tables whose rows may go (the chosen cars', by kind; the exporter's own user row; the migrations history).</summary>
    internal static readonly IReadOnlySet<string> Written = new HashSet<string>(StringComparer.Ordinal)
    {
        "Vehicles", "Refuelings", "Expenses", "Costs", "OdometerReadings", "RecurringExpenses", "RecurringCompletions",
        "TyreSets", "TyreInstallations", "TyreEntries", "Tyres", "TyrePlaces", "LogPhotos", "Images",
        "VehicleCharts", "VehicleDashboardLayouts", "UserDashboardLayouts", "Users", TableDump.MigrationsHistory,
    };

    /// <summary>
    /// The tables that never go (their files are there, empty): other people, grants and sharing, sessions and tokens, the sync ledger and
    /// tombstones, notifications, drafts and readings, settings, the instance's state, the runs. A new table is sorted into one of the two
    /// when it is added (<c>ExportWriterTests</c>).
    /// </summary>
    internal static readonly IReadOnlySet<string> Never = new HashSet<string>(StringComparer.Ordinal)
    {
        "AccessGrants", "AccessSettings", "GridSettings", "ImportRuns", "InstanceState", "Notifications", "OfflineSettings", "OfflineVehicleSettings",
        "PasswordResetTokens", "PhotoDrafts", "PhotoReadings", "ResourceGrants", "RunMarkers", "RunMarkerVehicles", "SyncChanges", "Tombstones",
        "UiSettings", "UserSessions", "VehicleOrders",
    };

    /// <summary>A table's rows that go, decided before anything is written: by a column and its values, and which of the rows it reads.</summary>
    private sealed record Selection(string Column, IReadOnlyCollection<Guid> Values, Func<object?[], bool>? Hook);

    public async Task<ExportWritten> WriteAsync(ExportWriteRequest request, Action<ExportProgress> progress, CancellationToken ct)
    {
        var partial = request.Output + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(request.Output)!);
        try
        {
            ExportWritten written;
            // Someone's cars, in plain unless they chose a passphrase: owner-only on Unix.
            var create = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) create.UnixCreateMode = Settings.SettingsFileStore.OwnerOnly;
            await using (var file = new FileStream(partial, create))
                written = await WriteFileAsync(file, request, progress, ct);
            File.Move(partial, request.Output, overwrite: true);
            return written;
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
    }

    private async Task<ExportWritten> WriteFileAsync(FileStream file, ExportWriteRequest request, Action<ExportProgress> progress, CancellationToken ct)
    {
        var createdAt = clock.GetUtcNow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var migration = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault();
        var tables = TableDump.Tables(db.Model);
        var sql = db.GetService<ISqlGenerationHelper>();
        var rows = new Dictionary<string, long>();
        long tableBytes = 0, pictureBytes = 0, photoBytes = 0;
        List<(StoredImage Image, string Path, bool Photo, int Car)> files;
        var archiveName = request.Encryption is null ? BackupArchive.Plain : BackupArchive.Encrypted;
        BackupManifest manifest;
        using (var outer = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            var hashing = new HashingStream(outer.CreateEntry(archiveName, CompressionLevel.NoCompression).Open());
            var data = request.Encryption is null ? hashing : AgeStream.Encrypt(hashing, AgeKeys.ParseRecipient(request.Encryption.Recipient));
            using (var inner = new ZipArchive(data, ZipArchiveMode.Create, leaveOpen: false))
            {
                await db.Database.OpenConnectionAsync(ct);
                try
                {
                    await using (var transaction = await BeginReadAsync(db, ct))
                    {
                        db.Database.UseTransaction(transaction);
                        var plan = await ExportPlan.MakeAsync(db, request, ct);
                        foreach (var table in tables)
                        {
                            await using var entry = inner.CreateEntry($"tables/{table.Name}.jsonl", CompressionLevel.Optimal).Open();
                            var (count, bytes) = await WriteTableAsync(db, sql, table, plan, request.ExporterId, entry, ct);
                            rows[table.Name] = count;
                            tableBytes += bytes;
                        }
                        files = await plan.FilesAsync(db, ct);
                        db.Database.UseTransaction(null);
                        await transaction.CommitAsync(ct); // it only read
                    }
                }
                finally
                {
                    await db.Database.CloseConnectionAsync();
                }
                var done = 0;
                foreach (var (image, path, photo, car) in files)
                {
                    progress(new ExportProgress(car + 1, request.Cars.Count, done, files.Count));
                    ct.ThrowIfCancellationRequested();
                    await using var content = await store.OpenReadAsync(image, ct);
                    if (content is null)
                    {
                        logger.LogWarning("Image {ImageId} of an export has no file; it is left out", image.Id);
                        continue;
                    }
                    await using (var entry = inner.CreateEntry(path, CompressionLevel.NoCompression).Open())
                        await content.CopyToAsync(entry, ct);
                    if (photo) photoBytes += image.SizeBytes;
                    else pictureBytes += image.SizeBytes;
                    done++;
                }
                progress(new ExportProgress(request.Cars.Count, request.Cars.Count, done, files.Count));
            }
            manifest = new BackupManifest(
                BackupManifest.CurrentFormat, BackupManifest.Export, HealthReporter.AppVersion, createdAt, database.Value.Provider.ToString(), migration, SafetyNet: false,
                new BackupContents(rows, Pictures: true, files.Any(f => f.Photo) ? BackupPhotos.Separate : BackupPhotos.LeftOut, Keys: false),
                new BackupSizes(tableBytes, pictureBytes, photoBytes, 0, hashing.Written),
                new BackupArchive(archiveName, hashing.Sha256Hex),
                request.Encryption);
            await using (var entry = outer.CreateEntry(BackupManifest.FileName, CompressionLevel.NoCompression).Open())
                await entry.WriteAsync(Encoding.UTF8.GetBytes(manifest.ToJson()), ct);
        }
        file.Flush(flushToDisk: true);
        return new ExportWritten(file.Length, request.Cars.Count, rows);
    }

    /// <summary>A transaction that only reads and gives one snapshot, never one that holds writers (see the class).</summary>
    private async Task<DbTransaction> BeginReadAsync(AppDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        switch (database.Value.Provider)
        {
            case DatabaseProvider.Sqlite:
                return ((SqliteConnection)connection).BeginTransaction(IsolationLevel.Serializable, deferred: true);
            case DatabaseProvider.SqlServer:
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT snapshot_isolation_state FROM sys.databases WHERE database_id = DB_ID()";
                    var snapshot = Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
                    return await connection.BeginTransactionAsync(snapshot ? IsolationLevel.Snapshot : IsolationLevel.ReadCommitted, ct);
                }
            default:
                return await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        }
    }

    private static async Task<(long Rows, long Bytes)> WriteTableAsync(
        AppDbContext db, ISqlGenerationHelper sql, DumpTable table, ExportPlan plan, Guid? exporter, Stream output, CancellationToken ct)
    {
        if (table.Name == TableDump.MigrationsHistory)
        {
            await using var history = Command(db, TableDump.Select(sql, table, null));
            return await TableDump.WriteAsync(history, table, output, null, ct);
        }
        if (table.Name == ScratchTables.Users) return exporter is { } me ? await WriteUserAsync(db, sql, table, me, output, ct) : (0, 0);
        if (plan.SelectionOf(table) is not { } selection) return (0, 0); // a table that never goes: its file is there, empty
        long rows = 0, bytes = 0;
        var column = table.Columns.First(c => c.Name == selection.Column);
        foreach (var chunk in selection.Values.Chunk(Chunk))
        {
            var names = chunk.Select((_, i) => $"@p{i}").ToList();
            await using var command = Command(db, TableDump.Select(sql, table, $"{sql.DelimitIdentifier(selection.Column)} IN ({string.Join(", ", names)})"));
            for (var i = 0; i < chunk.Length; i++) command.Parameters.Add(column.Mapping!.CreateParameter(command, names[i], chunk[i]));
            var (r, b) = await TableDump.WriteAsync(command, table, output, selection.Hook, ct);
            (rows, bytes) = (rows + r, bytes + b);
        }
        return (rows, bytes);
    }

    /// <summary>The exporter's own row, reduced exactly as the reader reduces every user (id, name and how they sign in; never a hash or an e-mail).</summary>
    private static async Task<(long Rows, long Bytes)> WriteUserAsync(AppDbContext db, ISqlGenerationHelper sql, DumpTable table, Guid me, Stream output, CancellationToken ct)
    {
        var id = table.Columns.First(c => c.Name == "Id");
        await using var command = Command(db, TableDump.Select(sql, table, $"{sql.DelimitIdentifier("Id")} = @me"));
        command.Parameters.Add(id.Mapping!.CreateParameter(command, "@me", me));
        using var whole = new MemoryStream();
        var (rows, _) = await TableDump.WriteAsync(command, table, whole, null, ct);
        var reduction = BackupUnpack.UserReduction.For(db.Model, keepEmails: false);
        long bytes = 0;
        foreach (var line in Encoding.UTF8.GetString(whole.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var reduced = reduction.Reduce(Encoding.UTF8.GetBytes(line));
            await output.WriteAsync(reduced, ct);
            output.WriteByte((byte)'\n');
            bytes += reduced.Length + 1;
        }
        return (rows, bytes);
    }

    private static DbCommand Command(AppDbContext db, string text)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = text;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    /// <summary>
    /// What goes, decided from the database in the export's transaction before a row is written: per table the rows by id, and what a row
    /// written must lose so it names nothing that stays behind (a picture not chosen, a tyre line's expense when the expenses stay, a
    /// dashboard's tile of a chart that does not go).
    /// </summary>
    private sealed class ExportPlan
    {
        private readonly Dictionary<string, Selection> _selections = new(StringComparer.Ordinal);
        private readonly List<(Guid ImageId, Guid Car, LogType? Type, Guid? Log)> _images = [];
        private IReadOnlyList<Guid> _cars = [];

        public Selection? SelectionOf(DumpTable table) => _selections.GetValueOrDefault(table.Name);

        public static async Task<ExportPlan> MakeAsync(AppDbContext db, ExportWriteRequest request, CancellationToken ct)
        {
            var plan = new ExportPlan();
            var kinds = request.Cars.ToDictionary(c => c.VehicleId, c => c.Kinds);
            var live = await db.Vehicles.IgnoreQueryFilters().AsNoTracking().Where(v => kinds.Keys.Contains(v.Id) && v.DeletedAt == null)
                .Select(v => new { v.Id, v.PictureImageId }).ToListAsync(ct);
            var cars = live.Select(v => v.Id).ToList();
            plan._cars = cars;
            List<Guid> With(Func<ExportKinds, bool> kind) => [.. cars.Where(c => kind(kinds[c]))];
            var me = request.ExporterId ?? Guid.Empty;

            // The logs: live ones of the kinds chosen, with the costs and readings they name.
            var fuelCars = With(k => k.Refuelings);
            var fuel = await db.Refuelings.IgnoreQueryFilters().AsNoTracking().Where(r => fuelCars.Contains(r.VehicleId) && r.DeletedAt == null)
                .Select(r => new { r.Id, r.CostId, r.OdometerReadingId }).ToListAsync(ct);
            var costCars = With(k => k.Expenses);
            var costs = await db.Expenses.IgnoreQueryFilters().AsNoTracking().Where(e => costCars.Contains(e.VehicleId) && e.DeletedAt == null)
                .Select(e => new { e.Id, e.VehicleId, e.CostId, e.OdometerReadingId }).ToListAsync(ct);
            var scheduleCars = With(k => k.Schedules);
            var schedules = await db.RecurringExpenses.IgnoreQueryFilters().AsNoTracking().Where(s => scheduleCars.Contains(s.VehicleId))
                .Select(s => s.Id).ToListAsync(ct);
            var expenseIds = costs.Select(e => e.Id).ToHashSet();
            var readings = new HashSet<Guid>(fuel.Select(r => r.OdometerReadingId).Concat(costs.Select(e => e.OdometerReadingId)).OfType<Guid>());
            var costRows = new HashSet<Guid>(fuel.Select(r => r.CostId).Concat(costs.Select(e => e.CostId)).OfType<Guid>());

            // The tyres, by the tyre book's own liveness (a line of a set in the trash is not itself trashed), all five tables or none.
            var (sets, installations, entries, tyres, places) = (new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
            foreach (var car in With(k => k.Tyres))
            {
                var book = await TyreRepository.LoadAsync(db, car, ct);
                var liveSets = book.Sets.Where(s => !s.IsDeleted).Select(s => s.Id).ToHashSet();
                sets.UnionWith(liveSets);
                var liveInstallations = book.Installations.Where(i => !i.IsDeleted && liveSets.Contains(i.SetId)).Select(i => i.Id).ToHashSet();
                installations.UnionWith(liveInstallations);
                var liveTyres = book.SingleTyres.Where(t => !t.IsDeleted && liveSets.Contains(t.SetId)).Select(t => t.Id).ToHashSet();
                tyres.UnionWith(liveTyres);
                places.UnionWith(book.TyrePlaces.Where(p => !p.IsDeleted && liveTyres.Contains(p.TyreId) && liveInstallations.Contains(p.InstallationId)).Select(p => p.Id));
                foreach (var line in book.Entries.Where(book.IsLive))
                {
                    entries.Add(line.Id);
                    if (line.OdometerReadingId is { } reading) readings.Add(reading);
                }
            }

            // The photos of the logs that go; the pictures of the cars whose picture goes.
            var photoCars = With(k => k.Photos);
            var fuelIds = fuel.Select(r => r.Id).ToHashSet();
            var photos = (await db.LogPhotos.AsNoTracking().Where(p => photoCars.Contains(p.VehicleId)).Select(p => new { p.Id, p.VehicleId, p.LogType, p.LogId, p.ImageId }).ToListAsync(ct))
                .Where(p => p.LogType == LogType.Refueling ? fuelIds.Contains(p.LogId) : expenseIds.Contains(p.LogId)).ToList();
            foreach (var photo in photos) plan._images.Add((photo.ImageId, photo.VehicleId, photo.LogType, photo.LogId));
            var pictures = live.Where(v => v.PictureImageId is not null && kinds[v.Id].Picture).ToDictionary(v => v.Id, v => v.PictureImageId!.Value);
            foreach (var (car, image) in pictures) plan._images.Add((image, car, null, null));

            // The saved widgets: the shared ones and the exporter's own (the owner's decision 4); the dashboards of the cars that take them.
            var chartCars = With(k => k.Charts);
            var charts = await db.VehicleCharts.AsNoTracking().Where(c => chartCars.Contains(c.VehicleId) && (c.IsShared || c.CreatedById == me)).Select(c => c.Id).ToListAsync(ct);
            var chartIds = charts.ToHashSet();

            var model = db.Model;
            int At(string table, string column) => TableDump.Tables(model).First(t => t.Name == table).Columns.ToList().FindIndex(c => c.Name == column);
            Func<object?[], bool> Ids(string table, IReadOnlySet<Guid> ids)
            {
                var id = At(table, "Id");
                return values => values[id] is Guid g && ids.Contains(g);
            }

            // Per table: the rows read by their car, then kept by id; a hook where a row names what stays behind.
            var pictureColumn = At("Vehicles", "PictureImageId");
            var vehicleId = At("Vehicles", "Id");
            plan._selections["Vehicles"] = new Selection("Id", cars, values =>
            {
                if (values[vehicleId] is Guid id && !pictures.ContainsKey(id)) values[pictureColumn] = null;
                return true;
            });
            plan._selections["Refuelings"] = new Selection("VehicleId", fuelCars, Ids("Refuelings", fuelIds));
            plan._selections["Expenses"] = new Selection("VehicleId", costCars, Ids("Expenses", expenseIds));
            plan._selections["RecurringExpenses"] = new Selection("VehicleId", scheduleCars, Ids("RecurringExpenses", schedules.ToHashSet()));
            var completionExpense = At("RecurringCompletions", "ExpenseId");
            plan._selections["RecurringCompletions"] = new Selection("RecurringExpenseId", schedules, values => values[completionExpense] is Guid e && expenseIds.Contains(e));
            plan._selections["Costs"] = new Selection("VehicleId", cars, Ids("Costs", costRows));
            plan._selections["OdometerReadings"] = new Selection("VehicleId", cars, Ids("OdometerReadings", readings));
            var tyreCars = With(k => k.Tyres);
            plan._selections["TyreSets"] = new Selection("VehicleId", tyreCars, Ids("TyreSets", sets));
            plan._selections["TyreInstallations"] = new Selection("VehicleId", tyreCars, Ids("TyreInstallations", installations));
            plan._selections["Tyres"] = new Selection("VehicleId", tyreCars, Ids("Tyres", tyres));
            plan._selections["TyrePlaces"] = new Selection("VehicleId", tyreCars, Ids("TyrePlaces", places));
            var entryKeep = Ids("TyreEntries", entries);
            var entryExpense = At("TyreEntries", "ExpenseId");
            plan._selections["TyreEntries"] = new Selection("VehicleId", tyreCars, values =>
            {
                if (!entryKeep(values)) return false;
                // The expenses stay behind: the line comes without its amount (#334's rule), never naming an expense the file does not have.
                if (values[entryExpense] is Guid e && !expenseIds.Contains(e)) values[entryExpense] = null;
                return true;
            });
            var photoIds = photos.Select(p => p.Id).ToHashSet();
            plan._selections["LogPhotos"] = new Selection("VehicleId", photoCars, Ids("LogPhotos", photoIds));
            plan._selections["Images"] = new Selection("Id", [.. plan._images.Select(i => i.ImageId).Distinct()], null);
            plan._selections["VehicleCharts"] = new Selection("VehicleId", chartCars, Ids("VehicleCharts", chartIds));
            plan._selections["VehicleDashboardLayouts"] = new Selection("VehicleId", chartCars, Tiles("VehicleDashboardLayouts", model, chartIds, null, At));
            plan._selections["UserDashboardLayouts"] = new Selection("VehicleId", chartCars, Tiles("UserDashboardLayouts", model, chartIds, me, At));
            return plan;
        }

        /// <summary>
        /// A dashboard's row with only the tiles (and hidden entries) whose widget goes: a saved widget's tile names it by id (<c>chart:…</c>),
        /// and one of a widget left behind would name someone's private chart. A person's own dashboard only when it is the exporter's.
        /// </summary>
        private static Func<object?[], bool> Tiles(string table, IModel model, IReadOnlySet<Guid> charts, Guid? owner, Func<string, string, int> at)
        {
            var (tiles, hidden) = (at(table, "Tiles"), at(table, "Hidden"));
            var user = owner is null ? -1 : at(table, "UserId");
            bool Goes(string? id) => id is null || !id.StartsWith(Tankstat.Domain.Charts.DashboardLayoutValues.ChartPrefix, StringComparison.Ordinal)
                || (Guid.TryParse(id[Tankstat.Domain.Charts.DashboardLayoutValues.ChartPrefix.Length..], out var chart) && charts.Contains(chart));
            return values =>
            {
                if (user >= 0 && !(values[user] is Guid u && u == owner)) return false;
                if (values[tiles] is string tileJson && JsonNode.Parse(tileJson) is JsonArray tileList)
                {
                    foreach (var tile in tileList.ToList())
                        if (!Goes(tile?["Id"]?.GetValue<string>() ?? tile?["id"]?.GetValue<string>())) tileList.Remove(tile);
                    values[tiles] = tileList.ToJsonString();
                }
                if (values[hidden] is string hiddenJson && JsonNode.Parse(hiddenJson) is JsonArray hiddenList)
                {
                    foreach (var entry in hiddenList.ToList())
                        if (!Goes(entry?.GetValue<string>())) hiddenList.Remove(entry);
                    values[hidden] = hiddenList.ToJsonString();
                }
                return true;
            };
        }

        /// <summary>The files to copy, each at the path the reader looks it up by (from ids, never the store's own layout), in the cars' order.</summary>
        public async Task<List<(StoredImage Image, string Path, bool Photo, int Car)>> FilesAsync(AppDbContext db, CancellationToken ct)
        {
            var ids = _images.Select(i => i.ImageId).Distinct().ToList();
            var stored = new Dictionary<Guid, StoredImage>();
            foreach (var chunk in ids.Chunk(Chunk))
                foreach (var image in await db.Images.AsNoTracking().Where(i => chunk.Contains(i.Id)).ToListAsync(ct)) stored[image.Id] = image;
            var files = new List<(StoredImage, string, bool, int)>();
            foreach (var (imageId, car, type, log) in _images.OrderBy(i => _cars.ToList().IndexOf(i.Car)))
            {
                if (!stored.TryGetValue(imageId, out var image)) continue;
                var name = imageId.ToString("N");
                files.Add(type is { } logType
                    ? (image, $"files/photos/{ImageFolders.LogPhotos(car, logType, log!.Value)}/{name}", true, _cars.ToList().IndexOf(car))
                    : (image, $"files/pictures/{ImageFolders.VehiclePicture(car)}/{name}", false, _cars.ToList().IndexOf(car)));
            }
            return files;
        }
    }
}
