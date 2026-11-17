using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Application.Exports;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Exports;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>Export my data's run (#341): the request's checks, one export per person, the kept file, its end, the sweep, who is told.</summary>
public sealed partial class BackupImportRunTests
{
    /// <summary>A writer that waits for its cancel, or fails, in place of the real one.</summary>
    private sealed class StuckWriter(bool fail) : IExportWriter
    {
        public async Task<ExportWritten> WriteAsync(ExportWriteRequest request, Action<ExportProgress> progress, CancellationToken ct)
        {
            progress(new ExportProgress(1, request.Cars.Count, 0, 3));
            if (fail) throw new DomainException("export.testFailure", "Zzcanary: the disk is full.");
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Not reached.");
        }
    }

    private TestDatabase ExportInstance(string name, IExportWriter? writer = null, bool enabled = true)
    {
        var database = new TestDatabase(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "Standalone", ["Storage:Path"] = Path.Combine(_root, name, "uploads"), ["Backups:Path"] = Path.Combine(_root, name, "backups"),
            ["DataProtection:KeysPath"] = Path.Combine(_root, name, "keys"), ["Exports:Folder"] = Path.Combine(_root, name, "exports"), ["Exports:Enabled"] = enabled ? "true" : "false",
            ["Imports:ScratchFolder"] = Path.Combine(_root, name, "scratch"),
        }, services =>
        {
            services.AddSingleton<ICurrentUser>(_user).AddSingleton<TimeProvider>(_clock);
            if (writer is not null) services.AddSingleton(writer);
        });
        _databases.Add(database);
        return database;
    }

    private async Task<Guid> CarWithFillsAsync(TestDatabase database, Principal who, string name) => await AsAsync(database, who, async sp =>
    {
        var car = (await sp.GetRequiredService<VehicleService>().AddAsync(name, null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
        await sp.GetRequiredService<RefuelingService>().LogAsync(car, new(Day, 40, 60, "EUR", 10_000, true, null), default);
        return car;
    });

    private Task<ExportView> StartExportAsync(TestDatabase database, Principal who, Guid car, string? passphrase = null) =>
        AsAsync(database, who, sp => sp.GetRequiredService<ExportService>().StartAsync([new ExportCar(car, ExportKinds.All)], passphrase, passphrase, default));

    private Task<ExportView?> MyExportAsync(TestDatabase database, Principal who) => AsAsync(database, who, sp => sp.GetRequiredService<ExportService>().MineAsync(default));

    [Fact]
    public async Task AnExport_IsBuilt_Kept_ReplacedByTheNext_DeletedAfterItsHours_AndThoseItConcernsAreTold()
    {
        var here = ExportInstance("export");
        var (anna, bob, ada) = (await PersonAsync(here, "Anna"), await PersonAsync(here, "Bob"), await PersonAsync(here, "Ada", admin: true));
        var golf = await CarWithFillsAsync(here, anna, "Zzcanary Golf");

        var first = await StartExportAsync(here, anna, golf, "a long enough passphrase");
        Assert.Equal(ExportState.Building, first.Record.State);
        await here.Get<ExportRunner>().WhenIdleAsync();
        var ready = (await MyExportAsync(here, anna))!.Record;
        Assert.Equal((ExportState.Ready, true, "Zzcanary Golf"), (ready.State, ready.Encrypted, Assert.Single(ready.CarNames)));
        Assert.Equal(_clock.GetUtcNow().AddHours(24).Date, ready.KeptUntil!.Value.Date);
        var (content, name) = await AsAsync(here, anna, sp => sp.GetRequiredService<ExportService>().DownloadAsync(ready.Id, default));
        await content.DisposeAsync();
        Assert.StartsWith("tankstat-export-", name);
        // Another person finds nothing, nor sees anyone's export.
        Assert.Equal("export.notFound", (await Assert.ThrowsAsync<NotFoundException>(() => AsAsync(here, bob, sp => sp.GetRequiredService<ExportService>().DownloadAsync(ready.Id, default)))).Key);
        Assert.Null(await MyExportAsync(here, bob));
        Assert.Contains(await ToldOfAsync(here, anna.Id, NotificationKind.ExportReady), n => n.Kind == NotificationKind.ExportReady);
        Assert.Contains(await ToldOfAsync(here, ada.Id, NotificationKind.CarsExported), n => n.Kind == NotificationKind.CarsExported && n.Args["names"] == "Zzcanary Golf");

        // A second export replaces the first once it is written.
        await StartExportAsync(here, anna, golf);
        await here.Get<ExportRunner>().WhenIdleAsync();
        var second = (await MyExportAsync(here, anna))!.Record;
        Assert.NotEqual(ready.Id, second.Id);
        Assert.Null(here.Get<IExportFiles>().OpenRead(ready.Id));
        Assert.Single(here.Get<IExportFiles>().Records(), r => r.OwnerId == anna.Id);

        // Past its hours the file goes; the record says when.
        _clock.Ahead = TimeSpan.FromHours(25);
        var expired = (await MyExportAsync(here, anna))!.Record;
        Assert.Equal(ExportState.Expired, expired.State);
        Assert.NotNull(expired.DeletedAt);
        Assert.Null(here.Get<IExportFiles>().OpenRead(second.Id));
        // The log names ids and counts: never the passphrase or a car.
        Assert.False(here.Log.Mentions("a long enough passphrase"));
        Assert.DoesNotContain(here.Log.Entries.Where(e => e.Category.StartsWith("Tankstat", StringComparison.Ordinal)), e => e.Text.Contains("Zzcanary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACancelOrAFailure_KeepsNothing_TheEarlierFileStays_AndARestartMidBuildEndsAsInterrupted()
    {
        var stuck = ExportInstance("stuck", new StuckWriter(fail: false));
        var anna = await PersonAsync(stuck, "Anna");
        var golf = await CarWithFillsAsync(stuck, anna, "Golf");
        var earlier = new ExportRecord(Guid.NewGuid(), anna.Id, ExportState.Ready, ["Golf"], false, _clock.GetUtcNow(), _clock.GetUtcNow(), _clock.GetUtcNow().AddHours(24), Bytes: 10);
        stuck.Get<IExportFiles>().Save(earlier);
        await StartExportAsync(stuck, anna, golf);
        // The run reports its first step on a thread of its own: asked until it has.
        ExportProgress? progress = null;
        for (var i = 0; i < 200 && (progress = (await MyExportAsync(stuck, anna))!.Progress) is null; i++) await Task.Delay(10);
        Assert.Equal((1, 3), progress is { } p ? (p.Car, p.Total) : default);
        Assert.True(await AsAsync(stuck, anna, sp => sp.GetRequiredService<ExportService>().CancelAsync(default)));
        await stuck.Get<ExportRunner>().WhenIdleAsync();
        Assert.Contains(stuck.Get<IExportFiles>().Records(), r => r.Id == earlier.Id && r.State == ExportState.Ready); // the earlier file stays
        Assert.Contains(stuck.Get<IExportFiles>().Records(), r => r.OwnerId == anna.Id && r.State == ExportState.Cancelled);
        var afterCancel = (await MyExportAsync(stuck, anna))!;
        Assert.Equal((ExportState.Cancelled, earlier.Id), (afterCancel.Record.State, afterCancel.Kept?.Record.Id)); // the page still offers the earlier file

        var failing = ExportInstance("failing", new StuckWriter(fail: true));
        var bob = await PersonAsync(failing, "Bob");
        var polo = await CarWithFillsAsync(failing, bob, "Polo");
        await StartExportAsync(failing, bob, polo);
        await failing.Get<ExportRunner>().WhenIdleAsync();
        var failed = (await MyExportAsync(failing, bob))!.Record;
        Assert.Equal((ExportState.Failed, "export.testFailure"), (failed.State, failed.FailureKey));
        Assert.Contains(await ToldOfAsync(failing, bob.Id, NotificationKind.ExportFailed), n => n.Kind == NotificationKind.ExportFailed);
        Assert.False(failing.Log.Mentions("Zzcanary"));

        // A record still building that no runner here builds: the server stopped meanwhile.
        var cut = new ExportRecord(Guid.NewGuid(), bob.Id, ExportState.Building, ["Polo"], false, failing.Get<IExportRunner>().Since.AddMinutes(-1));
        failing.Get<IExportFiles>().Save(cut);
        await MyExportAsync(failing, bob); // a look sweeps
        Assert.Equal(ExportState.Interrupted, failing.Get<IExportFiles>().Records().Single(r => r.Id == cut.Id).State);
    }

    [Fact]
    public async Task TheRequest_TakesOwnCarsOnly_AKindEach_APassphraseTypedTwice_AndTheSwitchHoldsUsersBack()
    {
        var here = ExportInstance("checks", enabled: false);
        var (anna, bob, ada) = (await PersonAsync(here, "Anna"), await PersonAsync(here, "Bob"), await PersonAsync(here, "Ada", admin: true));
        var golf = await CarWithFillsAsync(here, anna, "Golf");
        var adas = await CarWithFillsAsync(here, ada, "Fiat");
        Task<ExportView> Start(Principal who, IReadOnlyList<ExportCar> cars, string? pass = null, string? again = null) =>
            AsAsync(here, who, sp => sp.GetRequiredService<ExportService>().StartAsync(cars, pass, again, default));

        Assert.Equal(ExportService.DisabledKey, (await Assert.ThrowsAsync<ForbiddenException>(() => Start(anna, [new ExportCar(golf, ExportKinds.All)]))).Key);
        Assert.Equal(ExportService.DisabledKey, (await Assert.ThrowsAsync<ForbiddenException>(() => AsAsync(here, anna, sp => sp.GetRequiredService<ExportService>().DownloadAsync(Guid.NewGuid(), default)))).Key);
        Assert.False(await AsAsync(here, anna, sp => sp.GetRequiredService<ExportService>().MayExportAsync(default)));
        Assert.Equal(ExportService.NoCarsKey, (await Assert.ThrowsAsync<DomainException>(() => Start(ada, []))).Key);
        Assert.Equal("vehicle.notFound", (await Assert.ThrowsAsync<NotFoundException>(() => Start(ada, [new ExportCar(golf, ExportKinds.All)]))).Key); // not hers, admin or not
        Assert.Equal(ExportService.NoKindsKey, (await Assert.ThrowsAsync<DomainException>(() => Start(ada, [new ExportCar(adas, new ExportKinds(false, false, false, false, false, false, false))]))).Key);
        Assert.Equal("password.mismatch", (await Assert.ThrowsAsync<DomainException>(() => Start(ada, [new ExportCar(adas, ExportKinds.All)], "a long enough passphrase", "another"))).Key);
        Assert.Equal("backup.passphraseTooShort", (await Assert.ThrowsAsync<DomainException>(() => Start(ada, [new ExportCar(adas, ExportKinds.All)], "short", "short"))).Key);
        Assert.Equal(ExportState.Building, (await Start(ada, [new ExportCar(adas, ExportKinds.All)])).Record.State); // administrators keep it
        await here.Get<ExportRunner>().WhenIdleAsync();
        Assert.Equal(["Fiat"], (await AsAsync(here, ada, sp => sp.GetRequiredService<ExportService>().CarsAsync(default))).Select(c => c.Name)); // her own only
        Assert.Equal(1, (await AsAsync(here, ada, sp => sp.GetRequiredService<ExportService>().CarsAsync(default))).Single().Refuelings);
        _ = bob;
    }

    [Fact]
    public async Task WithSignInOff_TheInstanceHasOneExport_OfEveryCar_WithNoUserInIt()
    {
        var none = new TestDatabase(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "None", ["Storage:Path"] = Path.Combine(_root, "none-off", "uploads"), ["Backups:Path"] = Path.Combine(_root, "none-off", "backups"),
            ["DataProtection:KeysPath"] = Path.Combine(_root, "none-off", "keys"), ["Exports:Folder"] = Path.Combine(_root, "none-off", "exports"),
        }, services => services.AddSingleton<ICurrentUser>(_user).AddSingleton<TimeProvider>(_clock));
        _databases.Add(none);
        var golf = await CarWithFillsAsync(none, Principal.Anonymous, "Golf");
        var view = await StartExportAsync(none, Principal.Anonymous, golf);
        Assert.Equal(Guid.Empty, view.Record.OwnerId);
        await none.Get<ExportRunner>().WhenIdleAsync();
        var ready = (await MyExportAsync(none, Principal.Anonymous))!.Record;
        Assert.Equal(ExportState.Ready, ready.State);
        var (tables, _) = Read(none.Get<IExportFiles>().FileOf(ready.Id));
        Assert.Equal(golf, Assert.Single(tables["Vehicles"]).GetProperty("Id").GetGuid());
        Assert.Empty(tables["Users"]);
        Assert.Single(tables["Refuelings"]);
    }
}
