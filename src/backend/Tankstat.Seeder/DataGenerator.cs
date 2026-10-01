using Tankstat.Domain.Vehicles;

namespace Tankstat.Seeder;

public sealed record GeneratedVehicle(Vehicle Vehicle, IReadOnlyList<Refueling> Refuelings);

/// <summary>
/// Creates random but internally consistent test data through the domain's own factories (so every rule of the
/// real application applies). Deterministic for a given seed and date. Everything is owned by the anonymous owner
/// used when authentication is off; no users are created.
/// </summary>
public sealed class DataGenerator(TimeProvider clock)
{
    /// <summary>The owner used by the application when authentication is off.</summary>
    public static readonly Guid NoAuthOwner = Guid.Empty;

    private static readonly string[] Models =
    [
        "Skoda Octavia", "Skoda Fabia", "Skoda Superb", "VW Golf", "VW Passat", "VW Polo", "Opel Astra", "Opel Corsa",
        "Ford Focus", "Ford Fiesta", "Toyota Corolla", "Toyota Yaris", "Suzuki Swift", "Suzuki Vitara", "Renault Clio",
        "Renault Megane", "Peugeot 208", "Peugeot 308", "Honda Civic", "Mazda 3", "Hyundai i30", "Kia Ceed", "Fiat Panda",
        "Dacia Duster", "Dacia Sandero", "Seat Leon", "BMW 320d", "Audi A4", "Mercedes C200", "Volvo V60",
    ];

    private static readonly string[] Tags = ["family", "work", "weekend", "city", "old", "spare", "company", "daily"];

    public IEnumerable<GeneratedVehicle> Generate(SeedOptions options)
    {
        var random = new Random(options.RandomSeed);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = options.Vehicles + options.Trashed;

        for (var i = 0; i < total; i++)
        {
            var fuel = PickFuel(random);
            var vehicle = Vehicle.Create(NoAuthOwner, UniqueName(random, names), random.NextDouble() < 0.1 ? null : Plate(random), fuel);

            if (i >= options.Vehicles) vehicle.MarkDeleted(clock.GetUtcNow() - TimeSpan.FromDays(random.Next(1, 61)) - TimeSpan.FromMinutes(random.Next(0, 1440)));

            var count = random.Next(options.RefuelingsPerVehicle.Min, options.RefuelingsPerVehicle.Max + 1);
            yield return new GeneratedVehicle(vehicle, Refuelings(random, vehicle, count, today));
        }
    }

    private static FuelType PickFuel(Random random)
    {
        var roll = random.NextDouble();
        return roll < 0.5 ? FuelType.Petrol : roll < 0.9 ? FuelType.Diesel : FuelType.Lpg;
    }

    private static string UniqueName(Random random, HashSet<string> taken)
    {
        var baseName = $"{Models[random.Next(Models.Length)]} ({Tags[random.Next(Tags.Length)]})";
        var name = baseName;
        for (var n = 2; !taken.Add(name); n++) name = $"{baseName} {n}";
        return name;
    }

    private static string Plate(Random random) =>
        $"{(char)('A' + random.Next(26))}{(char)('A' + random.Next(26))}{(char)('A' + random.Next(26))}-{random.Next(100, 1000)}";

    /// <summary>
    /// A fuel log walked backwards in time from today: the dates only increase, the odometer only increases, and the litres of
    /// each fill-up equal the distance driven since the previous one at the vehicle's consumption (give or take 10%).
    /// </summary>
    private static List<Refueling> Refuelings(Random random, Vehicle vehicle, int count, DateOnly today)
    {
        var log = new List<Refueling>(count);
        if (count == 0) return log;

        var (consumption, tank, pricePerLiter) = vehicle.FuelType switch
        {
            FuelType.Diesel => (4.5 + random.NextDouble() * 3.0, 40 + random.Next(0, 31), 1.50 + random.NextDouble() * 0.2),
            FuelType.Lpg => (8.0 + random.NextDouble() * 4.0, 35 + random.Next(0, 26), 0.80 + random.NextDouble() * 0.15),
            _ => (5.5 + random.NextDouble() * 4.0, 35 + random.Next(0, 36), 1.45 + random.NextDouble() * 0.2),
        };

        // Gaps (days) between fill-ups, then dates counted back from a recent last fill-up.
        var gaps = Enumerable.Range(0, count).Select(_ => random.Next(3, 19)).ToArray();
        var date = today.AddDays(-random.Next(0, 8));
        var dates = new DateOnly[count];
        for (var i = count - 1; i >= 0; i--)
        {
            dates[i] = date;
            date = date.AddDays(-gaps[i]);
        }

        var odometer = random.Next(5_000, 250_000);
        for (var i = 0; i < count; i++)
        {
            var liters = Math.Round((decimal)(tank * (0.5 + random.NextDouble() * 0.45)), 2);
            var distance = Math.Max(1, (int)Math.Round((double)liters / (consumption * (0.9 + random.NextDouble() * 0.2)) * 100));
            odometer += distance;
            pricePerLiter *= 1 + (random.NextDouble() - 0.5) * 0.02; // the price drifts slowly
            var cost = Math.Round(liters * (decimal)pricePerLiter, 2);

            log.Add(Refueling.Create(vehicle.OwnerId, vehicle.Id, dates[i], liters, cost, odometer, isFullTank: random.NextDouble() < 0.85));
        }
        return log;
    }
}
