using Microsoft.Extensions.Time.Testing;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Vehicles;
using Tankstat.Seeder;

namespace Tankstat.Seeder.UnitTests;

/// <summary>The expenses the seeder makes: the visits of the recurring expenses and the one-off ones.</summary>
public class SeededExpensesTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static SeedOptions Options(int vehicles = 60, int trashed = 5, IntRange? refuelings = null, IntRange? recurring = null, IntRange? expenses = null, int seed = 42) =>
        new(vehicles, trashed, refuelings ?? new IntRange(5, 30), seed, true, null, null, null, recurring ?? new IntRange(0, 5), 15,
            ExpensesPerVehicle: expenses ?? new IntRange(0, 5));

    private static List<GeneratedVehicle> Generate(SeedOptions options) => new DataGenerator(Clock).Generate(options).ToList();

    private static List<Expense> OneOffs(GeneratedVehicle g) => [.. g.Expenses.Where(e => g.Completions.All(c => c.ExpenseId != e.Id))];

    [Fact]
    public void OneOffExpenses_FollowTheRange_AndNoneAreMadeWhenNoneAreAskedFor()
    {
        Assert.All(Generate(Options(expenses: new IntRange(2, 4))), g => Assert.InRange(OneOffs(g).Count, 2, 4));

        var none = Generate(Options(expenses: new IntRange(0, 0)));
        Assert.All(none, g => Assert.Empty(OneOffs(g)));
        Assert.Contains(none, g => g.Expenses.Count > 0); // the visits of the schedules are still there
    }

    [Theory]
    [InlineData(1, 0, 30)]
    [InlineData(2, 0, 1)]
    [InlineData(3, 1, 3)]
    [InlineData(4, 20, 60)]
    public void EveryReadingOfAVehicle_FitsBetweenTheReadingsOfTheDaysBeforeAndAfter(int seed, int minRefuelings, int maxRefuelings)
    {
        foreach (var g in Generate(Options(vehicles: 80, refuelings: new IntRange(minRefuelings, maxRefuelings), recurring: new IntRange(0, 5), expenses: new IntRange(0, 15), seed: seed)))
        {
            // the same rule the app applies to a new reading (OdometerService): no reading of an earlier day above it, none of a later day below
            var readings = g.Refuelings.Select(r => (r.Date, Value: r.Odometer!.Value))
                .Concat(g.Expenses.Where(e => e.Odometer is not null).Select(e => (e.Date, Value: e.Odometer!.Value))).ToList();
            foreach (var a in readings)
                Assert.DoesNotContain(readings, b => b.Date > a.Date && b.Value < a.Value);

            // a schedule's baseline is its last visit's reading, so it fits too
            foreach (var s in g.Recurring.Where(s => s.LastDoneOdometer is not null))
                Assert.DoesNotContain(readings, b => (b.Date > s.LastDoneDate && b.Value < s.LastDoneOdometer) || (b.Date < s.LastDoneDate && b.Value > s.LastDoneOdometer));
        }
    }

    [Fact]
    public void ExpensesAreInThePast_InTheVehiclesOwnCurrency_AndTheOneOffsWithinTheFuelLog()
    {
        var all = Generate(Options(refuelings: new IntRange(0, 30)));

        Assert.All(all.SelectMany(g => g.Expenses), e => Assert.True(e.Date <= Today && e.Amount > 0));
        foreach (var g in all)
        {
            Assert.True(g.Expenses.Select(e => e.Currency).Distinct().Count() <= 1); // never abroad, unlike a fill-up now and then
            if (g.Refuelings.Count >= 10 && g.Expenses.Count > 0) // the currency the vehicle's fill-ups are mostly paid in
                Assert.Equal(g.Refuelings.GroupBy(r => r.Currency).MaxBy(c => c.Count())!.Key, g.Expenses[0].Currency);

            var (from, to) = g.Refuelings.Count > 0 ? (g.Refuelings[0].Date, g.Refuelings[^1].Date) : (Today.AddDays(-180), Today);
            Assert.All(OneOffs(g), e => Assert.InRange(e.Date, from, to));
            if (g.Refuelings.Count == 0) Assert.All(g.Expenses, e => Assert.Null(e.Odometer));
        }

        var oneOffs = all.SelectMany(OneOffs).ToList();
        Assert.Equal(["Accessories", "Parking", "Repairs", "Washing"], oneOffs.Select(e => e.Category).Distinct().Order());
        Assert.InRange(oneOffs.Count(e => e.Odometer is not null) / (double)oneOffs.Count, 0.3, 0.7);
    }

    [Fact]
    public void CostsAndReadings_BelongToTheirExpense()
    {
        foreach (var g in Generate(Options()))
            foreach (var e in g.Expenses)
            {
                Assert.Equal((g.Vehicle.OwnerId, g.Vehicle.OwnerId, g.Vehicle.Id), (e.OwnerId, e.CreatedById, e.VehicleId));
                Assert.Equal((e.CostId, g.Vehicle.Id, e.Date, g.Vehicle.OwnerId), (e.Cost!.Id, e.Cost.VehicleId, e.Cost.Date, e.Cost.OwnerId));
                if (e.OdometerReading is { } r) Assert.Equal((e.OdometerReadingId, g.Vehicle.Id, e.Date, g.Vehicle.OwnerId), (r.Id, r.VehicleId, r.Date, r.OwnerId));
                Assert.False(e.IsDeleted);
            }
    }

    [Fact]
    public void EachVisitIsLinkedToItsSchedule_LikeMarkingItDone_AndTheLastOneIsTheBaseline()
    {
        var all = Generate(Options(vehicles: 100, refuelings: new IntRange(10, 40)));

        foreach (var g in all)
        {
            Assert.Equal(g.Completions.Count, g.Completions.Select(c => c.ExpenseId).Distinct().Count()); // one schedule per visit
            foreach (var s in g.Recurring)
            {
                var visits = g.Completions.Where(c => c.RecurringExpenseId == s.Id).Select(c => g.Expenses.Single(e => e.Id == c.ExpenseId)).OrderBy(e => e.Date).ToList();
                Assert.NotEmpty(visits);
                Assert.All(visits, v => Assert.Equal((s.Title, s.Category, s.Note), (v.Title, v.Category, v.Note)));
                Assert.Equal((s.LastDoneDate, s.LastDoneOdometer), (visits[^1].Date, visits[^1].Odometer));
                Assert.All(visits, v => Assert.Equal(s.UsesDistance, v.Odometer is not null)); // only one that counts distance needs the odometer
                for (var i = 1; i < visits.Count; i++)
                {
                    Assert.True(visits[i - 1].Date < visits[i].Date);
                    if (s.UsesDistance) Assert.True(visits[i - 1].Odometer < visits[i].Odometer);
                    // an interval apart: by months or by distance, and for one that counts both, by one of them with the other no further back
                    var byTime = s.UsesTime && visits[i - 1].Date == visits[i].Date.AddMonths(-s.IntervalMonths!.Value);
                    var byDistance = s.UsesDistance && visits[i - 1].Odometer == visits[i].Odometer - s.IntervalDistance!.Value;
                    Assert.True(byTime || byDistance, $"{s.Title}: {visits[i - 1].Date} at {visits[i - 1].Odometer}, then {visits[i].Date} at {visits[i].Odometer}");
                    if (s.UsesTime) Assert.True(visits[i - 1].Date >= visits[i].Date.AddMonths(-s.IntervalMonths!.Value));
                    if (s.UsesDistance) Assert.True(visits[i - 1].Odometer >= visits[i].Odometer - s.IntervalDistance!.Value);
                }
                Assert.All(visits.SkipLast(1), v => Assert.True(v.Date >= Today.AddYears(-3)));
            }
        }
        Assert.Contains(all.SelectMany(x => x.Completions).GroupBy(c => c.RecurringExpenseId), visits => visits.Count() > 1); // earlier visits too
    }

    [Fact]
    public void TrashedVehicles_KeepTheirOneOffExpenses_AndHaveNoVisits()
    {
        var trashed = Generate(Options(vehicles: 5, trashed: 20, expenses: new IntRange(3, 3))).Where(g => g.Vehicle.IsDeleted).ToList();

        Assert.Equal(20, trashed.Count);
        Assert.All(trashed, g => Assert.Equal(3, g.Expenses.Count));
        Assert.All(trashed, g => Assert.Empty(g.Completions));
    }

    [Fact]
    public void Schedules_StillComeInEveryState_EachKindAboutAsDrawn()
    {
        var all = Generate(Options(vehicles: 200, recurring: new IntRange(5, 5)));

        var states = all.SelectMany(g => g.Recurring.Select(r => (r.Title, RecurrenceCalculator.Evaluate(r, Today, g.Refuelings.LastOrDefault()?.Odometer).State))).ToList();
        Assert.Equal([RecurrenceState.Upcoming, RecurrenceState.DueSoon, RecurrenceState.Overdue], states.Select(s => s.State).Distinct().Order());
        Assert.Equal(5, states.Select(s => s.Title).Distinct().Count()); // every kind is there to be judged
        // every kind about as drawn, a quarter overdue and a quarter due soon: the app goes by the limit further along, so an oil change
        // (time and distance) whose odometer was not placed for the state drawn too would mostly be overdue
        foreach (var kind in states.GroupBy(s => s.Title))
            foreach (var state in new[] { RecurrenceState.Overdue, RecurrenceState.DueSoon })
            {
                var share = kind.Count(s => s.State == state) / (double)kind.Count();
                Assert.True(share is >= 0.1 and <= 0.4, $"{kind.Key}: {share:P0} {state}");
            }
        // no expense reading is newer than the last fill-up, so the fill-up's is the latest the app reads for these states
        Assert.All(all, g => Assert.All(g.Expenses.Where(e => e.Odometer is not null), e => Assert.True(e.Date <= g.Refuelings[^1].Date)));
    }

    [Fact]
    public void SameSeedSameExpenses_AndAskingForMoreMovesNothingElse()
    {
        static string Expenses(SeedOptions options) => string.Join("|", Generate(options).SelectMany(g => g.Expenses.Select(e => $"{g.Vehicle.Name}:{e.Date}:{e.Title}:{e.Category}:{e.Amount}:{e.Currency}:{e.Odometer}")));
        static string Rest(SeedOptions options)
        {
            var generator = new DataGenerator(Clock);
            var all = generator.Generate(options).ToList();
            return string.Join("|", all.Select(g => $"{g.Vehicle.Name}/{g.Vehicle.IsDeleted}/" +
                string.Join(",", g.Refuelings.Select(r => $"{r.Date}:{r.TotalCost}:{r.Odometer}")) + "/" +
                string.Join(",", g.Recurring.Select(r => $"{r.Title}:{r.LastDoneDate}:{r.LastDoneOdometer}")))
                .Concat(generator.Notifications(options, [.. all.Select(g => g.Vehicle).Where(v => !v.IsDeleted)]).Select(n => $"{n.Kind}:{n.Count}:{n.ReadAt}")));
        }

        Assert.Equal(Expenses(Options(seed: 5)), Expenses(Options(seed: 5)));
        Assert.NotEqual(Expenses(Options(seed: 5)), Expenses(Options(seed: 6)));
        Assert.Equal(Rest(Options(seed: 5, expenses: new IntRange(0, 0))), Rest(Options(seed: 5, expenses: new IntRange(5, 20))));
    }
}
