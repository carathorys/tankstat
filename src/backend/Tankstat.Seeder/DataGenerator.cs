using Tankstat.Application.Vehicles;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Seeder;

public sealed record GeneratedVehicle(Vehicle Vehicle, IReadOnlyList<Refueling> Refuelings, IReadOnlyList<RecurringExpense> Recurring);

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
        var scheduleRandom = new Random(options.RandomSeed + 1); // its own sequence, so the vehicles and logs of a seed stay as they were
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
            var log = Refuelings(random, vehicle, market, count, today);
            var schedules = vehicle.IsDeleted ? [] : Recurring(scheduleRandom, vehicle, log.LastOrDefault()?.Odometer, options.RecurringPerVehicle, today);
            yield return new GeneratedVehicle(vehicle, log, schedules);
        }
    }

    private const double KmToMiles = 0.621371;
    private const double LitersPerUsGallon = 3.78541;
    private const double LitersPerImperialGallon = 4.54609;

    /// <summary>How often a fill-up was not logged (the next one says so).</summary>
    private const double MissedFillUpShare = 0.03;

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
    /// each fill-up equal the distance driven since the previous one at the vehicle's consumption (give or take 10%). Now and then a
    /// fill-up was not logged: the odometer runs on by its distance and the next log is marked (<see cref="Refueling.MissedPreviousFillUp"/>).
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

        // A fill-up: how many litres, and how far they took the car (in the vehicle's unit).
        (double Liters, long Distance) FillUp()
        {
            var liters = tank * (0.5 + random.NextDouble() * 0.45);
            var distanceKm = liters / (consumption * (0.9 + random.NextDouble() * 0.2)) * 100;
            return (liters, Math.Max(1, (long)Math.Round(distanceKm * market.DistancePerKm)));
        }

        var odometer = (long)(random.Next(5_000, 250_000) * market.DistancePerKm);
        for (var i = 0; i < count; i++)
        {
            var missed = i > 0 && random.NextDouble() < MissedFillUpShare;
            if (missed) odometer += FillUp().Distance; // a fill-up nobody logged: only the odometer knows
            var (liters, distance) = FillUp();
            odometer += distance;
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
                isFullTank: random.NextDouble() < 0.85, missedPreviousFillUp: missed));
        }
        ConsumptionCalculator.Apply(log); // stored like the app stores it
        return log;
    }

    // ---- Recurring expenses -----------------------------------------------------------------------------------------

    private sealed record Template(string Title, string Category, RecurrenceKind Kind, int? Months, long? DistanceKm);

    private static readonly Template[] Templates =
    [
        new("Insurance", "Insurance", RecurrenceKind.Time, 12, null),
        new("Vignette", "Fees", RecurrenceKind.Time, 12, null),
        new("Technical inspection", "Service", RecurrenceKind.Time, 24, null),
        new("Oil change", "Service", RecurrenceKind.Combined, 12, 15_000),
        new("Tyres", "Service", RecurrenceKind.Odometer, null, 40_000),
    ];

    private enum Due { Overdue, Soon, Upcoming }

    /// <summary>
    /// Schedules with the instance's default warnings, last done so that some are overdue, some due soon and the rest upcoming (the app
    /// works out their reminders when the notifications are looked at). Ones that count distance need a reading, so a vehicle without a
    /// fuel log only gets time-based ones.
    /// </summary>
    private List<RecurringExpense> Recurring(Random random, Vehicle vehicle, long? odometer, IntRange perVehicle, DateOnly today)
    {
        var defaults = new VehicleDefaultsOptions();
        var usable = Templates.Where(t => odometer is not null || t.Kind == RecurrenceKind.Time).OrderBy(_ => random.Next()).ToList();
        var count = Math.Min(usable.Count, random.Next(perVehicle.Min, perVehicle.Max + 1));
        var perKm = vehicle.Units.Distance == DistanceUnit.Miles ? KmToMiles : 1;

        return usable.Take(count).Select(t =>
        {
            var roll = random.NextDouble();
            var due = roll < 0.25 ? Due.Overdue : roll < 0.5 ? Due.Soon : Due.Upcoming;
            var distance = t.DistanceKm is { } km ? (long)Math.Round(km * perKm / 1000) * 1000 : (long?)null;

            // Time: due at last done + interval; "soon" is within the 30 warning days.
            var lastDone = t.Months is { } months
                ? due switch
                {
                    Due.Overdue => today.AddMonths(-months).AddDays(-random.Next(1, 60)),
                    Due.Soon => today.AddMonths(-months).AddDays(random.Next(1, Math.Min(defaults.RecurringWarnDays, 26))),
                    _ => today.AddMonths(-months).AddDays(random.Next(defaults.RecurringWarnDays + 10, months * 28)),
                }
                : today.AddDays(-random.Next(30, 400));
            // Distance: due at last done + interval; "soon" is within the warning distance.
            long? lastOdometer = distance is { } interval && odometer is { } current
                ? Math.Max(0, due switch
                {
                    Due.Overdue => current - interval - random.Next(100, 2_000),
                    Due.Soon => current - interval + random.Next(50, (int)defaults.RecurringWarnDistance - 50),
                    _ => current - random.Next(0, (int)(interval / 2)),
                })
                : null;

            return RecurringExpense.Create(
                vehicle.OwnerId, vehicle.OwnerId, vehicle.Id, t.Title, t.Category, null, t.Kind, t.Months, distance, lastDone, lastOdometer,
                defaults.RecurringWarnDays, defaults.RecurringWarnDistance, clock.GetUtcNow());
        }).ToList();
    }

    // ---- Notifications ----------------------------------------------------------------------------------------------

    private static readonly string[] People = ["Anna Kovács", "Bence Tóth", "Chris Miller", "Dóra Nagy", "Eve Smith", "Frank Weber"];
    private static readonly string[] Levels = ["EDIT", "DELETE", "NONE"];

    /// <summary>
    /// Notifications about access changes for the anonymous user, as the app would have sent them over the last two weeks (made up people,
    /// since the seeder creates no users; the vehicle ones are about the given vehicles): about a third already read, one that stands for
    /// several folded changes and, when there are enough, one "more changes". Deterministic for a given seed.
    /// </summary>
    public IReadOnlyList<Notification> Notifications(SeedOptions options, IReadOnlyList<Vehicle> vehicles)
    {
        var random = new Random(options.RandomSeed + 2);
        var now = clock.GetUtcNow();
        var result = new List<Notification>(options.Notifications);
        for (var i = 0; i < options.Notifications; i++)
        {
            var at = now - TimeSpan.FromMinutes(random.Next(10, 14 * 24 * 60));
            var actor = People[random.Next(People.Length)];
            var other = People[random.Next(People.Length)];
            var otherId = PersonId(other);
            var level = Levels[random.Next(Levels.Length)];
            var vehicle = vehicles.Count > 0 ? vehicles[random.Next(vehicles.Count)] : null;
            var digest = i == options.Notifications - 1 && options.Notifications >= 10;

            Notification n = digest
                ? Create(NotificationKind.MoreActivity, NotificationRef.Instance, null, [], null)
                : (vehicle is null ? random.Next(2, 5) : random.Next(0, 5)) switch
                {
                    0 => Create(NotificationKind.LogAccessChanged, NotificationRef.Vehicle(vehicle!.Id), NotificationRef.Vehicle(vehicle.Id),
                        [("actorName", actor), ("vehicleName", vehicle.Name), ("level", level)], "NONE"),
                    1 => Create(NotificationKind.VehicleShared, NotificationRef.User(otherId), NotificationRef.Vehicle(vehicle!.Id),
                        [("actorName", actor), ("userName", other), ("vehicleName", vehicle.Name), ("level", level)], "NONE"),
                    2 => Create(NotificationKind.DataAccessChanged, NotificationRef.User(otherId), null, [("actorName", actor), ("userName", other), ("level", level == "DELETE" ? "VIEW" : level)], "NONE"),
                    3 => Create(NotificationKind.DataShared, NotificationRef.User(otherId), null, [("actorName", actor), ("userName", other), ("level", level)], "NONE"),
                    _ => Create(NotificationKind.DefaultAccessChanged, NotificationRef.Instance, null, [("actorName", actor), ("level", level == "DELETE" ? "VIEW" : level)], "NONE"),
                };

            // A few stand for several changes made one after the other (the app folds them into one), the digest for many.
            var folded = digest ? random.Next(3, 12) : random.NextDouble() < 0.15 ? random.Next(2, 4) : 1;
            for (var f = 1; f < folded; f++) n.Fold(n.Kind, n.Args, at + TimeSpan.FromMinutes(f * random.Next(1, 30)));
            if (random.NextDouble() < 0.35) n.MarkRead(Min(n.UpdatedAt + TimeSpan.FromMinutes(random.Next(5, 3 * 24 * 60)), now));
            result.Add(n);

            Notification Create(NotificationKind kind, NotificationRef subject, NotificationRef? context, (string Name, string Value)[] args, string? before) =>
                Notification.Create(DataGenerator.NoAuthOwner, kind, subject, context, Guid.NewGuid().ToString("N"), args.ToDictionary(a => a.Name, a => a.Value), before, at);
        }
        return result;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    /// <summary>A stable made-up id per person, so the notifications about one person are about the same "user".</summary>
    private static Guid PersonId(string name) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(name)));
}
