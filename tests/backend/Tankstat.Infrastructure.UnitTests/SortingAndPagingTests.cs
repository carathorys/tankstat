using Tankstat.Application.Access;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>Ordering and paging are done by the database (not the client); these run against a real SQLite file.</summary>
public class SortingAndPagingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed record Seed(User Zoe, User Bob, User Amy, Vehicle Beta, Vehicle Alpha, Vehicle Gamma);

    /// <summary>Beta(Zoe, petrol, 5 refuelings), alpha(Bob, LPG, 9), Gamma(Amy, diesel, 1, no plate).</summary>
    private static async Task<Seed> Seeded(TestDatabase db)
    {
        var users = db.Get<IUserRepository>();
        User[] people = [User.CreateLocal("zoe@x.co", "Zoe", false), User.CreateLocal("bob@x.co", "bob", false), User.CreateLocal("amy@x.co", "Amy", false)];
        foreach (var p in people) await users.AddAsync(p, default);

        var vehicles = db.Get<IVehicleRepository>();
        var refuelings = db.Get<IRefuelingRepository>();
        var beta = TestData.Vehicle(people[0].Id, "Beta", "bbb-2", FuelType.Petrol);
        var alpha = TestData.Vehicle(people[1].Id, "alpha", "aaa-1", FuelType.Lpg);
        var gamma = TestData.Vehicle(people[2].Id, "Gamma", null, FuelType.Diesel);
        foreach (var v in new[] { beta, alpha, gamma }) await vehicles.AddAsync(v, default);

        async Task Fuel(Vehicle v, int times)
        {
            for (var i = 0; i < times; i++) await refuelings.AddAsync(TestData.Refueling(v.OwnerId, Guid.Empty, v.Id, new(2026, 9, 1 + i % 28), 10, 10, 100 + i, true), default);
        }
        await Fuel(beta, 5);
        await Fuel(alpha, 9);
        await Fuel(gamma, 1);
        return new Seed(people[0], people[1], people[2], beta, alpha, gamma);
    }

    private static async Task<string[]> Names(TestDatabase db, VehicleSortField field, SortDirection direction, int skip = 0, int take = 50) =>
        (await db.Get<IVehicleRepository>().ListAsync(OwnerScope.All, new VehicleQuery(field, direction, skip, take), default)).Select(v => v.Name).ToArray();

    [Theory]
    [InlineData(VehicleSortField.Name, new[] { "alpha", "Beta", "Gamma" })]          // case-insensitive
    [InlineData(VehicleSortField.LicensePlate, new[] { "Gamma", "alpha", "Beta" })] // no plate sorts first on SQLite
    [InlineData(VehicleSortField.FuelType, new[] { "Gamma", "alpha", "Beta" })]     // DIESEL, LPG, PETROL
    [InlineData(VehicleSortField.Owner, new[] { "Gamma", "alpha", "Beta" })]        // Amy, bob, Zoe (case-insensitive)
    [InlineData(VehicleSortField.RefuelingCount, new[] { "Gamma", "Beta", "alpha" })] // 1, 5, 9
    public async Task SortsAscending_ByEveryField(VehicleSortField field, string[] expected)
    {
        await using var db = new TestDatabase();
        await Seeded(db);

        Assert.Equal(expected, await Names(db, field, SortDirection.Asc));
        Assert.Equal(expected.Reverse(), await Names(db, field, SortDirection.Desc));
    }

    [Theory]
    [InlineData("BETA", new[] { "Beta" })]                 // case does not matter
    [InlineData("a", new[] { "alpha", "Beta", "Gamma" })]  // a name has an a in it
    [InlineData("aaa", new[] { "alpha" })]                 // the license plate counts too
    [InlineData("  gam  ", new[] { "Gamma" })]             // surrounding blanks are ignored
    [InlineData("nothing", new string[0])]
    [InlineData("   ", new[] { "alpha", "Beta", "Gamma" })] // blank: no search
    public async Task Search_FindsVehiclesByNameOrLicensePlate_IgnoringCase(string search, string[] expected)
    {
        await using var db = new TestDatabase();
        await Seeded(db);
        var repo = db.Get<IVehicleRepository>();

        var found = await repo.ListAsync(OwnerScope.All, new VehicleQuery(Search: search), default);
        var count = await repo.CountAsync(OwnerScope.All, new VehicleQuery(Search: search).Normalized().Search, default);

        Assert.Equal(expected, found.Select(v => v.Name));
        Assert.Equal(expected.Length, count);
    }

    [Fact]
    public async Task Search_PagesTheMatchesOnly_AndTreatsWildcardsAsPlainText()
    {
        await using var db = new TestDatabase();
        await Seeded(db);
        var repo = db.Get<IVehicleRepository>();

        var second = await repo.ListAsync(OwnerScope.All, new VehicleQuery(Skip: 1, Take: 1, Search: "a"), default);
        var percent = await repo.ListAsync(OwnerScope.All, new VehicleQuery(Search: "%"), default);

        Assert.Equal(["Beta"], second.Select(v => v.Name));
        Assert.Empty(percent);
    }

    [Fact]
    public async Task Paging_SlicesTheSortedList_AndCountIgnoresPaging()
    {
        await using var db = new TestDatabase();
        await Seeded(db);

        Assert.Equal(["alpha", "Beta"], await Names(db, VehicleSortField.Name, SortDirection.Asc, 0, 2));
        Assert.Equal(["Gamma"], await Names(db, VehicleSortField.Name, SortDirection.Asc, 2, 2));
        Assert.Empty(await Names(db, VehicleSortField.Name, SortDirection.Asc, 3, 2));
        Assert.Equal(3, await db.Get<IVehicleRepository>().CountAsync(OwnerScope.All, null, default));
    }

    [Fact]
    public async Task Pages_AreStable_WhenTheSortKeyTies()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IVehicleRepository>();
        var owner = Guid.NewGuid();
        for (var i = 0; i < 12; i++) await repo.AddAsync(TestData.Vehicle(owner, "Same", null, FuelType.Petrol), default);

        var all = new List<Guid>();
        for (var skip = 0; skip < 12; skip += 5)
            all.AddRange((await repo.ListAsync(OwnerScope.All, new VehicleQuery(VehicleSortField.Name, SortDirection.Asc, skip, 5), default)).Select(v => v.Id));

        Assert.Equal(12, all.Distinct().Count()); // no row repeated or skipped across pages
    }

    [Fact]
    public async Task Sorting_RespectsTheAccessScope_AndCountsOnlyWhatIsInScope()
    {
        await using var db = new TestDatabase();
        var seed = await Seeded(db);
        var repo = db.Get<IVehicleRepository>();
        var scope = OwnerScope.Of([seed.Zoe.Id, seed.Amy.Id]);

        var page = await repo.ListAsync(scope, new VehicleQuery(VehicleSortField.RefuelingCount, SortDirection.Desc), default);

        Assert.Equal(["Beta", "Gamma"], page.Select(v => v.Name));
        Assert.Equal(2, await repo.CountAsync(scope, null, default));
        Assert.Equal(0, await repo.CountAsync(OwnerScope.Of([]), null, default));
    }

    [Fact]
    public async Task Trash_IsSortedAndPagedToo()
    {
        await using var db = new TestDatabase();
        var seed = await Seeded(db);
        var repo = db.Get<IVehicleRepository>();
        foreach (var (v, at) in new[] { (seed.Beta, Now.AddDays(-1)), (seed.Alpha, Now), (seed.Gamma, Now.AddDays(-2)) })
        {
            var loaded = (await repo.FindAsync(v.Id, default))!;
            loaded.MarkDeleted(at);
            await repo.UpdateAsync(loaded, default);
        }

        async Task<string[]> Trash(VehicleSortField f, SortDirection d, int skip = 0, int take = 50) =>
            (await repo.ListDeletedAsync(OwnerScope.All, new VehicleQuery(f, d, skip, take), default)).Select(v => v.Name).ToArray();

        Assert.Equal(["alpha", "Beta", "Gamma"], await Trash(VehicleSortField.DeletedAt, SortDirection.Desc));
        Assert.Equal(["Gamma", "Beta", "alpha"], await Trash(VehicleSortField.DeletedAt, SortDirection.Asc));
        Assert.Equal(["Beta"], await Trash(VehicleSortField.DeletedAt, SortDirection.Desc, 1, 1));
        Assert.Equal(["alpha", "Beta", "Gamma"], await Trash(VehicleSortField.Name, SortDirection.Asc));
        Assert.Equal(3, await repo.CountDeletedAsync(OwnerScope.All, default));
        Assert.Equal(0, await repo.CountAsync(OwnerScope.All, null, default));
    }

    [Fact]
    public async Task RefuelingCount_CountsPerVehicle()
    {
        await using var db = new TestDatabase();
        var seed = await Seeded(db);
        var refuelings = db.Get<IRefuelingRepository>();

        Assert.Equal(5, await refuelings.CountForVehicleAsync(seed.Beta.Id, default));
        Assert.Equal(9, await refuelings.CountForVehicleAsync(seed.Alpha.Id, default));
        Assert.Equal(0, await refuelings.CountForVehicleAsync(Guid.NewGuid(), default));
    }
}
