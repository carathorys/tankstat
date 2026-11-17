using System.Security.Cryptography;
using Tankstat.Application.Auth;
using Tankstat.Application.Images;
using Tankstat.Application.Photos;
using Tankstat.Application.Runs;
using Tankstat.Application.Stats;
using Tankstat.Domain;
using Tankstat.Domain.Charts;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Imports;

/// <summary>
/// What else of a car is chosen (#339, the owner's choice: boxes, no Replace all): its log photos, its picture, its charts, its dashboard.
/// A picture or dashboard left null is "as the car is": the backup's comes when the car has none of its own (a new car never has).
/// </summary>
public sealed record ImportMediaChoice(bool Photos = true, bool? Picture = null, bool Charts = true, bool? Dashboard = null, bool? OwnDashboard = null);

/// <summary>
/// What the media of a backup's car are to the car they go into (#339). Photos: of the logs that come or are matched, new or here already
/// (the same file). The picture: whether the backup has one, and the car. Widgets: the shared ones and the importer's own, new or here
/// (the same title and recipe). Dashboards: the vehicle's and the importer's own, and whether the car has its own.
/// </summary>
public sealed record ImportMediaReview(
    int PhotosNew, int PhotosHere, bool PhotosLeftOut, bool Picture, bool TargetPicture, int ChartsNew, int ChartsHere, int OwnChartsNew,
    bool Dashboard, bool TargetDashboard, bool OwnDashboard, bool TargetOwnDashboard = false);

/// <summary>
/// A partial restore's media (#340, the owner's decision 5): the same photo, picture or widget is the one with the backup's id, every row
/// keeps its id and its creator, and a person's private widgets and own dashboard come back when <see cref="MaySeeAsync"/> says they have an
/// account and can see the car afterwards (asked once per person).
/// </summary>
public sealed class RestoreMedia(Func<Guid, CancellationToken, Task<bool>> maySee)
{
    private readonly Dictionary<Guid, bool> _known = [];

    public async Task<bool> MaySeeAsync(Guid userId, CancellationToken ct)
    {
        if (!_known.TryGetValue(userId, out var may)) _known[userId] = may = await maySee(userId, ct);
        return may;
    }

    /// <summary>How many private widgets and own dashboards were left out for people without an account or the sight of the car.</summary>
    public int LeftOut { get; set; }
}

/// <summary>What came of a car's media: photos added and found here, the picture, widgets added and here, the dashboards and their tiles left out.</summary>
public sealed record ImportMediaReport(int Photos, int PhotosHere, bool Picture, int Charts, int ChartsHere, bool Dashboard, bool OwnDashboard, int TilesLeftOut);

/// <summary>
/// The media of a backup's car (#339), through the services: a log's photo with <see cref="LogPhotoService"/> (its rules: ten a log, the
/// formats, 2 MB; no reading queued), the picture with <see cref="ImageService"/> (inside a run the old picture's file goes only after the
/// commit), the widgets with <see cref="ChartService"/> (the importer the creator, new ids; the limit of 30), the dashboards with
/// <see cref="DashboardService"/>, each chart tile pointed at the widget's id here and one whose widget did not come left out and counted.
/// <b>The same photo</b> is the same file (its SHA-256) among the matched entry's photos, worked out when compared; nothing is stored.
/// </summary>
public sealed class BackupMediaWork(
    LogPhotoService photos, ImageService images, ChartService charts, DashboardService dashboards, ILogPhotoRepository photoRows, IImageRepository imageRows,
    IImageStore store, IVehicleChartRepository chartRows, IDashboardLayoutRepository layouts, IImportTargetReader targets)
{
    public const string PhotoKind = "photo", PictureKind = "picture", ChartKind = "chart", DashboardKind = "dashboard";

    /// <summary>The widgets of the backup's that come: the shared ones and the importer's own (the owner's answer on #339: their own come back to them).</summary>
    private static IEnumerable<VehicleChart> Coming(BackupMedia media, Guid me) => media.Charts.Where(c => c.IsShared || c.CreatedById == me);

    private static bool Same(VehicleChart a, VehicleChart b) =>
        a.Title.Trim() == b.Title.Trim() && Equals(a.Config, b.Config) && Equals(a.Figure, b.Figure) && a.IsShared == b.IsShared;

    private async Task<HashSet<string>> HashesOfAsync(LogType type, Guid logId, Dictionary<(LogType, Guid), HashSet<string>> cache, CancellationToken ct)
    {
        if (cache.TryGetValue((type, logId), out var known)) return known;
        var hashes = new HashSet<string>();
        foreach (var photo in await photoRows.ListForLogAsync(type, logId, ct))
            if (await imageRows.FindAsync(photo.ImageId, ct) is { } image && await store.OpenReadAsync(image, ct) is { } stream)
                await using (stream) hashes.Add(Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)));
        return cache[(type, logId)] = hashes;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>
    /// How many of the car's photos of a kind a Replace all deletes for good (#339, the owner's answer: shown in the confirmation): every one
    /// the backup does not bring back, by its file, the backup's photos of that kind only when the photos come.
    /// </summary>
    public async Task<int> PhotosLostAsync(IBackupCars file, Guid carId, Vehicle target, LogType type, bool photosComing, CancellationToken ct)
    {
        var mine = await targets.PhotosAsync(target, type, ct);
        if (mine.Count == 0) return 0;
        var backup = new HashSet<string>();
        if (photosComing && await file.ReadMediaAsync(carId, ct) is { } media)
            foreach (var photo in media.Photos.Where(p => p.LogType == type))
                if (await file.ReadImageAsync(carId, photo, photo.ImageId, ImageFormat.MaxBytes, ct) is { } bytes) backup.Add(Hash(bytes));
        var lost = 0;
        foreach (var id in mine)
        {
            if (await imageRows.FindAsync(id, ct) is not { } image || await store.OpenReadAsync(image, ct) is not { } stream) { lost++; continue; }
            await using (stream) if (!backup.Contains(Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)))) lost++;
        }
        return lost;
    }

    /// <summary>The widgets that come of a restore: the shared ones, and each person's own when they may have them (<see cref="RestoreMedia"/>).</summary>
    private static async Task<List<VehicleChart>> ComingAsync(BackupMedia media, Principal me, RestoreMedia? restore, CancellationToken ct)
    {
        if (restore is null) return [.. Coming(media, me.Id)];
        var coming = new List<VehicleChart>();
        foreach (var chart in media.Charts)
            if (chart.IsShared || await restore.MaySeeAsync(chart.CreatedById, ct)) coming.Add(chart);
        return coming;
    }

    /// <summary>The dashboards that come: the vehicle's, and the importer's own, or for a restore each person's own they may have.</summary>
    private static async Task<List<BackupDashboard>> DashboardsAsync(BackupMedia media, Principal me, RestoreMedia? restore, CancellationToken ct)
    {
        var coming = new List<BackupDashboard>();
        foreach (var layout in media.Dashboards)
            if (layout.UserId is not { } user || (restore is null ? user == me.Id : await restore.MaySeeAsync(user, ct))) coming.Add(layout);
        return coming;
    }

    /// <summary>A widget of the backup's that is the car's already: by its id for a restore, else by its title and recipe among those the importer sees.</summary>
    private async Task<VehicleChart?> HereAsync(VehicleChart chart, Vehicle vehicle, IReadOnlyList<VehicleChart> visible, RestoreMedia? restore, CancellationToken ct) =>
        restore is null ? visible.FirstOrDefault(v => Same(v, chart))
            : await chartRows.FindAsync(chart.Id, ct) is { } byId && byId.VehicleId == vehicle.Id ? byId : null;

    /// <param name="logOf">Where a backup's log will be in the car: null when it does not come; <c>Guid.Empty</c> when it comes as a new log.</param>
    /// <param name="restore">A partial restore's (#340): everything by id, and the people's own widgets and dashboards.</param>
    public async Task<ImportMediaReview> ReviewAsync(IBackupCars file, Guid carId, Vehicle? target, Principal me, Func<LogType, Guid, Guid?> logOf, CancellationToken ct,
        RestoreMedia? restore = null)
    {
        var media = await file.ReadMediaAsync(carId, ct) ?? new BackupMedia(null, [], false, [], []);
        var cache = new Dictionary<(LogType, Guid), HashSet<string>>();
        var (fresh, here) = (0, 0);
        foreach (var photo in media.Photos)
        {
            if (logOf(photo.LogType, photo.LogId) is not { } log) continue;
            if (log == Guid.Empty) fresh++;
            else if (restore is not null)
            {
                if (await photoRows.FindAsync(photo.Id, ct) is not null) here++;
                else fresh++;
            }
            else if (await file.ReadImageAsync(carId, photo, photo.ImageId, ImageFormat.MaxBytes, ct) is { } bytes
                && (await HashesOfAsync(photo.LogType, log, cache, ct)).Contains(Hash(bytes))) here++;
            else fresh++;
        }
        var visible = target is null ? [] : await chartRows.ListVisibleAsync(target.Id, me.Id, ct);
        var coming = await ComingAsync(media, me, restore, ct);
        var (chartsHere, ownNew) = (0, 0);
        foreach (var chart in coming)
        {
            if (target is not null && await HereAsync(chart, target, visible, restore, ct) is not null) chartsHere++;
            else if (!chart.IsShared) ownNew++;
        }
        var own = (await DashboardsAsync(media, me, restore, ct)).Where(d => d.UserId is not null).ToList();
        var ownHere = false;
        if (target is not null)
            foreach (var layout in own)
                ownHere |= await layouts.FindPersonalAsync(layout.UserId!.Value, target.Id, ct) is not null;
        // A restore brings no picture the car has already under the backup's id (the run takes none then, so it is no step either).
        var picture = media.PictureImageId is not null && (restore is null || target?.PictureImageId != media.PictureImageId);
        return new ImportMediaReview(fresh, here, media.PhotosLeftOut, picture, target?.PictureImageId is not null,
            coming.Count - chartsHere, chartsHere, ownNew,
            media.Dashboards.Any(d => d.UserId is null), target is not null && await layouts.FindSharedAsync(target.Id, ct) is not null,
            own.Count > 0, ownHere);
    }

    /// <param name="logOf">Where a backup's log is in the car now; null when it did not come.</param>
    /// <param name="refused">A photo, the picture, a widget or a dashboard the rules refuse, as a refused entry under the car's policy.</param>
    /// <param name="restore">A partial restore's (#340): everything by id, keeping ids and creators, and the people's own widgets and dashboards.</param>
    public async Task<ImportMediaReport> RunAsync(
        IRun run, IBackupCars file, Guid carId, Vehicle vehicle, Principal me, ImportMediaChoice choice, Func<LogType, Guid, Guid?> logOf,
        Action<ImportRefusedEntry> refused, Action step, CancellationToken ct, RestoreMedia? restore = null)
    {
        var media = await file.ReadMediaAsync(carId, ct) ?? new BackupMedia(null, [], false, [], []);
        var (added, here, picture, chartsAdded, chartsHere, dashboard, own, left) = (0, 0, false, 0, 0, false, false, 0);
        async Task<bool> SavedAsync(Func<Task> save, string kind, string? title)
        {
            await run.ThrowIfStoppedAsync();
            try
            {
                await run.InSavepointAsync(save);
                return true;
            }
            catch (KeyedException e)
            {
                refused(new ImportRefusedEntry(kind, null, title, e.Key, e.Args.ToDictionary(a => a.Key, a => Convert.ToString(a.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "")));
                return false;
            }
            finally
            {
                step();
            }
        }

        if (choice.Photos)
        {
            var cache = new Dictionary<(LogType, Guid), HashSet<string>>();
            // In the backup's order, one at a time: a log shows its photos by when they were added.
            foreach (var photo in media.Photos)
            {
                if (logOf(photo.LogType, photo.LogId) is not { } log) continue;
                var bytes = await file.ReadImageAsync(carId, photo, photo.ImageId, ImageFormat.MaxBytes, ct);
                if (bytes is null)
                {
                    step();
                    refused(new ImportRefusedEntry(PhotoKind, null, null, "import.photoMissing", new Dictionary<string, string>()));
                    continue;
                }
                if (restore is not null)
                {
                    // The same photo is the one with its id; it comes under that id, its image under the backup's with the backup's bytes.
                    if (await photoRows.FindAsync(photo.Id, ct) is not null)
                    {
                        here++;
                        step();
                    }
                    else if (await SavedAsync(() => photos.AddAsync(photo.LogType, log, bytes, ct, (photo.Id, photo.ImageId)), PhotoKind, null)) added++;
                    continue;
                }
                var hashes = await HashesOfAsync(photo.LogType, log, cache, ct);
                if (hashes.Contains(Hash(bytes)))
                {
                    here++;
                    step();
                    continue;
                }
                if (await SavedAsync(() => photos.AddAsync(photo.LogType, log, bytes, ct), PhotoKind, null))
                {
                    added++;
                    hashes.Add(Hash(bytes));
                }
            }
        }

        // Left as the car is: the backup's picture and dashboard come when the car has none of its own (decided here, not by the page).
        var takePicture = (choice.Picture ?? vehicle.PictureImageId is null) && (restore is null || vehicle.PictureImageId != media.PictureImageId);
        var takeDashboard = choice.Dashboard ?? await layouts.FindSharedAsync(vehicle.Id, ct) is null;
        if (takePicture && media.PictureImageId is { } pictureId)
        {
            if (await file.ReadImageAsync(carId, null, pictureId, ImageFormat.MaxBytes, ct) is { } bytes)
                picture = await SavedAsync(() => images.SetVehiclePictureAsync(vehicle.Id, bytes, ct, restore is null ? null : pictureId), PictureKind, null);
            else
            {
                step();
                refused(new ImportRefusedEntry(PictureKind, null, null, "import.photoMissing", new Dictionary<string, string>()));
            }
        }

        // The widgets here are matched whatever is ticked: a dashboard taken without the charts still points its tiles at those here.
        var chartIds = new Dictionary<Guid, Guid>();
        var visible = (await chartRows.ListVisibleAsync(vehicle.Id, me.Id, ct)).ToList();
        var comingCharts = await ComingAsync(media, me, restore, ct);
        if (restore is not null) restore.LeftOut += media.Charts.Count - comingCharts.Count;
        foreach (var chart in comingCharts)
            if (await HereAsync(chart, vehicle, visible, restore, ct) is { } same) chartIds[chart.Id] = same.Id;
        if (choice.Charts)
        {
            foreach (var chart in comingCharts)
            {
                if (chartIds.ContainsKey(chart.Id))
                {
                    chartsHere++;
                    step();
                    continue;
                }
                VehicleChart? made = null;
                if (await SavedAsync(async () => made = await charts.CreateAsync(vehicle.Id, new ChartInput(chart.Title, chart.Config, chart.IsShared, chart.Figure), ct,
                        restore is null ? null : (chart.Id, chart.CreatedById)), ChartKind, chart.Title))
                {
                    chartIds[chart.Id] = made!.Id;
                    visible.Add(made);
                    chartsAdded++;
                }
            }
        }

        var comingLayouts = await DashboardsAsync(media, me, restore, ct);
        if (restore is not null) restore.LeftOut += media.Dashboards.Count - comingLayouts.Count;
        foreach (var layout in comingLayouts)
            {
                // The importer's own (a box of its own, the owner's choice), or for a restore each person's: as the car is, when they have none here.
                var take = layout.UserId is not { } user ? takeDashboard
                    : choice.OwnDashboard ?? await layouts.FindPersonalAsync(user, vehicle.Id, ct) is null;
                if (!take) continue;
                // Every chart tile pointed at the widget here; one whose widget did not come is left out, and counted.
                string? Point(string entry) => DashboardLayoutValues.ChartIdOf(entry) is not { } id ? entry
                    : chartIds.TryGetValue(id, out var here) ? $"{DashboardLayoutValues.ChartPrefix}{here:D}" : null;
                var tiles = layout.Values.Tiles.Select(t => Point(t.Id) is { } id ? t with { Id = id } : null).ToList();
                var hidden = layout.Values.Hidden.Select(Point).ToList();
                var values = new DashboardLayoutValues([.. tiles.OfType<DashboardTile>()], [.. hidden.OfType<string>()]);
                var personal = layout.UserId is not null;
                var whose = layout.UserId ?? me.Id;
                if (await SavedAsync(() => dashboards.SaveAsync(vehicle.Id, personal, values, ct, restore is null || !personal ? null : whose), DashboardKind, null))
                {
                    var saved = personal ? (await layouts.FindPersonalAsync(whose, vehicle.Id, ct))?.Values : (await layouts.FindSharedAsync(vehicle.Id, ct))?.Values;
                    left += tiles.Count(t => t is null) + values.Tiles.Count - (saved?.Tiles.Count ?? 0);
                    if (personal) own = true;
                    else dashboard = true;
                }
            }

        return new ImportMediaReport(added, here, picture, chartsAdded, chartsHere, dashboard, own, left);
    }
}
