using Microsoft.Extensions.Time.Testing;
using Tankstat.Domain.Vehicles;
using Tankstat.Seeder;

namespace Tankstat.Seeder.UnitTests;

public class DataGeneratorTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static List<GeneratedVehicle> Generate(int vehicles = 30, int trashed = 7, IntRange? refuelings = null, int seed = 42) =>
        new DataGenerator(Clock).Generate(new SeedOptions(vehicles, trashed, refuelings ?? new IntRange(5, 25), seed, true, null, null)).ToList();

    [Fact]
    public void CreatesTheRequestedNumberOfLiveAndTrashedVehicles()
    {
        var all = Generate(30, 7);

        Assert.Equal(37, all.Count);
        Assert.Equal(30, all.Count(g => !g.Vehicle.IsDeleted));
        Assert.Equal(7, all.Count(g => g.Vehicle.IsDeleted));
    }

    [Fact]
    public void NothingRequested_NothingCreated() => Assert.Empty(Generate(0, 0));

    [Fact]
    public void OnlyTrashedVehiclesAreAllowedToo() => Assert.All(Generate(0, 4), g => Assert.True(g.Vehicle.IsDeleted));

    [Fact]
    public void EverythingBelongsToTheNoAuthOwner_AndNoUserIsInvolved()
    {
        var all = Generate();

        Assert.All(all, g =>
        {
            Assert.Equal(Guid.Empty, g.Vehicle.OwnerId);
            Assert.All(g.Refuelings, r => Assert.Equal(Guid.Empty, r.OwnerId));
        });
    }

    [Fact]
    public void TrashedVehicles_WereDeletedInThePast_WithinTwoMonths()
    {
        var trashed = Generate(5, 20).Where(g => g.Vehicle.IsDeleted).ToList();

        Assert.All(trashed, g =>
        {
            Assert.True(g.Vehicle.DeletedAt < Clock.GetUtcNow());
            Assert.True(g.Vehicle.DeletedAt > Clock.GetUtcNow().AddDays(-62));
        });
        Assert.True(trashed.Select(g => g.Vehicle.DeletedAt).Distinct().Count() > 1); // varied, not one timestamp
    }

    [Fact]
    public void VehicleDetails_AreVariedAndValid()
    {
        var all = Generate(200, 0);

        Assert.Equal(all.Count, all.Select(g => g.Vehicle.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(3, all.Select(g => g.Vehicle.FuelType).Distinct().Count());
        Assert.Contains(all, g => g.Vehicle.LicensePlate is null);
        Assert.All(all.Where(g => g.Vehicle.LicensePlate is not null), g => Assert.Matches("^[A-Z]{3}-[0-9]{3}$", g.Vehicle.LicensePlate!));
    }

    [Fact]
    public void RefuelingCounts_FollowTheRange_AndBelongToTheirVehicle()
    {
        var all = Generate(60, 10, new IntRange(5, 25));

        Assert.All(all, g =>
        {
            Assert.InRange(g.Refuelings.Count, 5, 25);
            Assert.All(g.Refuelings, r => Assert.Equal(g.Vehicle.Id, r.VehicleId));
        });
        Assert.True(all.Select(g => g.Refuelings.Count).Distinct().Count() > 3); // actually varies
    }

    [Fact]
    public void ExactCountAndZero()
    {
        Assert.All(Generate(10, 0, new IntRange(12, 12)), g => Assert.Equal(12, g.Refuelings.Count));
        Assert.All(Generate(10, 2, new IntRange(0, 0)), g => Assert.Empty(g.Refuelings));
    }

    [Fact]
    public void FuelLogs_AreChronologicalAndTheOdometerOnlyIncreases()
    {
        foreach (var g in Generate(80, 20, new IntRange(2, 40)))
        {
            var log = g.Refuelings;
            for (var i = 1; i < log.Count; i++)
            {
                Assert.True(log[i].Date > log[i - 1].Date, $"{g.Vehicle.Name}: dates must increase");
                Assert.True(log[i].OdometerKm > log[i - 1].OdometerKm, $"{g.Vehicle.Name}: odometer must increase");
            }
        }
    }

    [Fact]
    public void FuelLogs_NeverEndInTheFuture_AndStayRecent()
    {
        foreach (var g in Generate(80, 0, new IntRange(1, 40)))
        {
            Assert.All(g.Refuelings, r => Assert.True(r.Date <= Today));
            Assert.True(g.Refuelings[^1].Date >= Today.AddDays(-8)); // the last fill-up is recent
        }
    }

    [Fact]
    public void Amounts_ArePositive_AndPlausible()
    {
        foreach (var r in Generate(80, 10, new IntRange(5, 30)).SelectMany(g => g.Refuelings))
        {
            Assert.InRange(r.Liters, 10m, 80m);
            Assert.True(r.TotalCost > 0);
            Assert.InRange(r.TotalCost / r.Liters, 0.5m, 2.5m); // price per litre
            Assert.True(r.OdometerKm > 0);
        }
    }

    [Fact]
    public void LitersFollowThePerVehicleConsumption()
    {
        // Litres per 100 km between consecutive fill-ups stay near one value per vehicle (the generator allows +-10%).
        foreach (var g in Generate(60, 0, new IntRange(10, 30)))
        {
            var log = g.Refuelings;
            var consumption = Enumerable.Range(1, log.Count - 1)
                .Select(i => (double)log[i].Liters / (log[i].OdometerKm - log[i - 1].OdometerKm) * 100).ToList();

            Assert.InRange(consumption.Min(), 3.5, 14.5);
            Assert.True(consumption.Max() / consumption.Min() < 1.4, $"{g.Vehicle.Name}: consumption varies {consumption.Min():F1}..{consumption.Max():F1}");
        }
    }

    [Fact]
    public void SameSeedSameData_DifferentSeedDifferentData()
    {
        static string Fingerprint(List<GeneratedVehicle> all) =>
            string.Join("|", all.Select(g => $"{g.Vehicle.Name}/{g.Vehicle.LicensePlate}/{g.Vehicle.FuelType}/{g.Vehicle.IsDeleted}/" +
                string.Join(",", g.Refuelings.Select(r => $"{r.Date:o}:{r.Liters}:{r.TotalCost}:{r.OdometerKm}:{r.IsFullTank}"))));

        Assert.Equal(Fingerprint(Generate(seed: 5)), Fingerprint(Generate(seed: 5)));
        Assert.NotEqual(Fingerprint(Generate(seed: 5)), Fingerprint(Generate(seed: 6)));
    }

    [Fact]
    public void MostFillUpsAreFullTanks_ButNotAll()
    {
        var all = Generate(60, 0, new IntRange(20, 30)).SelectMany(g => g.Refuelings).ToList();

        var full = all.Count(r => r.IsFullTank) / (double)all.Count;
        Assert.InRange(full, 0.7, 0.95);
    }
}
