using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Access;
using Tankstat.Application.Expenses;
using Tankstat.Application.Photos;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

public class LogPhotoRepositoryTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 9, 1);
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(Vehicle Vehicle, Expense Expense)> AddExpense(TestDatabase db, Vehicle? vehicle = null)
    {
        var v = vehicle ?? TestData.Vehicle(Owner, "Car");
        if (vehicle is null) await db.Get<IVehicleRepository>().AddAsync(v, default);
        var expense = Expense.Create(Owner, Owner, v.Id, Day, "Tyres", null, Cost.Create(Owner, v.Id, Day, 100, "EUR"), OdometerReading.Create(Owner, v.Id, Day, 1000));
        await db.Get<IExpenseRepository>().AddAsync(expense, default);
        return (v, expense);
    }

    private static LogPhoto Photo(Vehicle v, LogType type, Guid logId, DateTimeOffset? at = null) =>
        LogPhoto.Create(v.OwnerId, v.Id, type, logId, Guid.NewGuid(), Owner, at ?? Now);

    private static async Task<AppDbContext> Context(TestDatabase db) => await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

    [Fact]
    public async Task Photos_RoundTrip_OldestFirst_PerKindOfLog()
    {
        await using var db = new TestDatabase();
        var (v, expense) = await AddExpense(db);
        var repo = db.Get<ILogPhotoRepository>();
        var later = Photo(v, LogType.Expense, expense.Id, Now.AddMinutes(5));
        var earlier = Photo(v, LogType.Expense, expense.Id, Now);
        var otherKind = Photo(v, LogType.Refueling, expense.Id); // same log id, other kind
        foreach (var p in new[] { later, earlier, otherKind }) await repo.AddAsync(p, default);

        var listed = await repo.ListForLogAsync(LogType.Expense, expense.Id, default);

        Assert.Equal([earlier.ImageId, later.ImageId], listed.Select(p => p.ImageId));
        Assert.Equal(2, await repo.CountForLogAsync(LogType.Expense, expense.Id, default));
        var found = (await repo.FindByImageAsync(later.ImageId, default))!;
        Assert.Equal((later.Id, Owner, v.Id, Now.AddMinutes(5)), (found.Id, found.OwnerId, found.VehicleId, found.CreatedAt));
        Assert.Null(await repo.FindByImageAsync(Guid.NewGuid(), default));

        await repo.RemoveAsync(earlier, default);
        Assert.Equal([later.ImageId], (await repo.ListForLogAsync(LogType.Expense, expense.Id, default)).Select(p => p.ImageId));
    }

    [Fact]
    public async Task ListForLogs_ReturnsThePhotosOfTheGivenLogsOfThatKindOnly()
    {
        await using var db = new TestDatabase();
        var (v, one) = await AddExpense(db);
        var (_, two) = await AddExpense(db, v);
        var (_, three) = await AddExpense(db, v);
        var repo = db.Get<ILogPhotoRepository>();
        var p1 = Photo(v, LogType.Expense, one.Id);
        var p2 = Photo(v, LogType.Expense, two.Id);
        await repo.AddAsync(p1, default);
        await repo.AddAsync(p2, default);
        await repo.AddAsync(Photo(v, LogType.Expense, three.Id), default);
        await repo.AddAsync(Photo(v, LogType.Refueling, one.Id), default); // same id, other kind

        var listed = await repo.ListForLogsAsync(LogType.Expense, [one.Id, two.Id], default);

        Assert.Equal(new[] { p1.ImageId, p2.ImageId }.Order(), listed.Select(p => p.ImageId).Order());
    }

    [Fact]
    public async Task AnImage_CanBeThePhotoOfOneLogOnly()
    {
        await using var db = new TestDatabase();
        var (v, expense) = await AddExpense(db);
        var repo = db.Get<ILogPhotoRepository>();
        var first = Photo(v, LogType.Expense, expense.Id);
        await repo.AddAsync(first, default);
        var duplicate = LogPhoto.Create(v.OwnerId, v.Id, LogType.Expense, expense.Id, first.ImageId, Owner, Now);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => repo.AddAsync(duplicate, default));
    }

    [Fact]
    public async Task PurgingExpenses_RemovesTheirPhotoRows_AndReportsWhichLogs()
    {
        await using var db = new TestDatabase();
        var (v, doomed) = await AddExpense(db);
        var (_, kept) = await AddExpense(db, v);
        var photos = db.Get<ILogPhotoRepository>();
        await photos.AddAsync(Photo(v, LogType.Expense, doomed.Id), default);
        var keptPhoto = Photo(v, LogType.Expense, kept.Id);
        await photos.AddAsync(keptPhoto, default);
        var sameIdOtherKind = Photo(v, LogType.Refueling, doomed.Id); // must not be touched by an expense purge
        await photos.AddAsync(sameIdOtherKind, default);
        var expenses = db.Get<IExpenseRepository>();
        var loaded = (await expenses.FindAsync(doomed.Id, default))!;
        loaded.MarkDeleted(Now);
        await expenses.UpdateAsync(loaded, LinkedChanges.None, default);

        var purged = await expenses.PurgeAsync(OwnerScope.All, default);

        Assert.Equal(1, purged.Count);
        Assert.Equal([(v.Id, doomed.Id)], purged.WithPhotos);
        await using var ctx = await Context(db);
        Assert.Equal(new[] { keptPhoto.ImageId, sameIdOtherKind.ImageId }.Order(), ctx.LogPhotos.Select(p => p.ImageId).ToList().Order());
    }

    [Fact]
    public async Task PurgingExpenses_ReportsOnlyTheLogsThatHadPhotosForFolderCleanup()
    {
        await using var db = new TestDatabase();
        var (v, withPhoto) = await AddExpense(db);
        var (_, without) = await AddExpense(db, v);
        await db.Get<ILogPhotoRepository>().AddAsync(Photo(v, LogType.Expense, withPhoto.Id), default);
        var expenses = db.Get<IExpenseRepository>();
        foreach (var id in new[] { withPhoto.Id, without.Id })
        {
            var loaded = (await expenses.FindAsync(id, default))!;
            loaded.MarkDeleted(Now);
            await expenses.UpdateAsync(loaded, LinkedChanges.None, default);
        }

        var purged = await expenses.PurgeAsync(OwnerScope.All, default);

        Assert.Equal(2, purged.Count); // both were purged
        Assert.Equal([(v.Id, withPhoto.Id)], purged.WithPhotos); // but only one has a folder to remove
    }

    [Fact]
    public async Task PurgingRefuelings_RemovesTheirPhotoRows_AndReportsWhichLogs()
    {
        await using var db = new TestDatabase();
        var v = TestData.Vehicle(Owner, "Car");
        await db.Get<IVehicleRepository>().AddAsync(v, default);
        var log = Refueling.Create(Owner, Owner, v.Id, Day, 40, Cost.Create(Owner, v.Id, Day, 60, "EUR"), OdometerReading.Create(Owner, v.Id, Day, 1000), true, false);
        var refuelings = db.Get<IRefuelingRepository>();
        await refuelings.AddAsync(log, default);
        await db.Get<ILogPhotoRepository>().AddAsync(Photo(v, LogType.Refueling, log.Id), default);
        var loaded = (await refuelings.FindAsync(log.Id, default))!;
        loaded.MarkDeleted(Now);
        await refuelings.UpdateAsync(loaded, LinkedChanges.None, default);

        var purged = await refuelings.PurgeAsync(OwnerScope.All, default);

        Assert.Equal([(v.Id, log.Id)], purged.WithPhotos);
        await using var ctx = await Context(db);
        Assert.Empty(ctx.LogPhotos);
    }

    [Fact]
    public async Task PurgingAVehicle_RemovesItsPhotoRowsInTheDatabase()
    {
        await using var db = new TestDatabase();
        var (v, expense) = await AddExpense(db);
        await db.Get<ILogPhotoRepository>().AddAsync(Photo(v, LogType.Expense, expense.Id), default);
        var vehicles = db.Get<IVehicleRepository>();
        var loaded = (await vehicles.FindAsync(v.Id, default))!;
        loaded.MarkDeleted(Now);
        await vehicles.UpdateAsync(loaded, default);

        var result = await vehicles.PurgeAsync(OwnerScope.All, default);

        Assert.Equal([v.Id], result.VehicleIds);
        await using var ctx = await Context(db);
        Assert.Empty(ctx.LogPhotos);
    }
}
