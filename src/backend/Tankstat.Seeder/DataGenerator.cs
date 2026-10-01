using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
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
            var market = PickMarket(random);
            var vehicle = Vehicle.Create(NoAuthOwner, UniqueName(random, names), random.NextDouble() < 0.1 ? null : Plate(random), fuel, market.Units);

            if (i >= options.Vehicles) vehicle.MarkDeleted(clock.GetUtcNow() - TimeSpan.FromDays(random.Next(1, 61)) - TimeSpan.FromMinutes(random.Next(0, 1440)));

            var count = random.Next(options.RefuelingsPerVehicle.Min, options.RefuelingsPerVehicle.Max + 1);
            yield return new GeneratedVehicle(vehicle, Refuelings(random, vehicle, market, count, today));
        }
    }

    private const double KmToMiles = 0.621371;
    private const double LitersPerUsGallon = 3.78541;
    private const double LitersPerImperialGallon = 4.54609;

    /// <summary>Where a vehicle "lives": its units, the currency it is normally paid in (and one for the odd trip abroad) and a price level.</summary>
    private sealed record Market(MeasurementUnits Units, string HomeCurrency, double HomeRate, string AbroadCurrency, double AbroadRate)
    {
        public double LitersPerUnit => Units.Volume switch { VolumeUnit.UsGallons => LitersPerUsGallon, VolumeUnit.ImperialGallons => LitersPerImperialGallon, _ => 1 };
        public double DistancePerKm => Units.Distance == DistanceUnit.Miles ? KmToMiles : 1;
    }

    // Prices are generated in euro per litre and multiplied by the rate of the currency they are paid in.
    private static readonly Market[] Markets =
    [
        new(MeasurementUnits.Metric, "EUR", 1.0, "HUF", 400),
        new(MeasurementUnits.Metric, "HUF", 400, "EUR", 1.0),
        new(MeasurementUnits.Create(DistanceUnit.Miles, VolumeUnit.ImperialGallons), "GBP", 0.85, "EUR", 1.0),
        new(MeasurementUnits.Create(DistanceUnit.Miles, VolumeUnit.UsGallons), "USD", 1.1, "EUR", 1.0),
    ];

    private static Market PickMarket(Random random)
    {
        var roll = random.NextDouble();
        return roll < 0.45 ? Markets[0] : roll < 0.75 ? Markets[1] : roll < 0.88 ? Markets[2] : Markets[3];
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
    private static List<Refueling> Refuelings(Random random, Vehicle vehicle, Market market, int count, DateOnly today)
    {
        var log = new List<Refueling>(count);
        if (count == 0) return log;

        // Consumption (litres per 100 km), tank size (litres) and price (euro per litre) depend on the fuel.
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

        var odometer = (long)(random.Next(5_000, 250_000) * market.DistancePerKm);
        for (var i = 0; i < count; i++)
        {
            var liters = tank * (0.5 + random.NextDouble() * 0.45);
            var distanceKm = liters / (consumption * (0.9 + random.NextDouble() * 0.2)) * 100;
            odometer += Math.Max(1, (long)Math.Round(distanceKm * market.DistancePerKm)); // in the vehicle's unit
            pricePerLiter *= 1 + (random.NextDouble() - 0.5) * 0.02; // the price drifts slowly

            // Mostly paid at home; now and then abroad, in the other currency.
            var abroad = random.NextDouble() < 0.1;
            var currency = abroad ? market.AbroadCurrency : market.HomeCurrency;
            var rate = abroad ? market.AbroadRate : market.HomeRate;

            var volume = Math.Round((decimal)(liters / market.LitersPerUnit), 2);
            var cost = Math.Round((decimal)(liters * pricePerLiter * rate), 2);

            log.Add(Refueling.Create(
                vehicle.OwnerId, vehicle.OwnerId, vehicle.Id, dates[i], volume,
                Cost.Create(vehicle.OwnerId, vehicle.Id, dates[i], cost, currency),
                OdometerReading.Create(vehicle.OwnerId, vehicle.Id, dates[i], odometer),
                isFullTank: random.NextDouble() < 0.85));
        }
        return log;
    }
}
