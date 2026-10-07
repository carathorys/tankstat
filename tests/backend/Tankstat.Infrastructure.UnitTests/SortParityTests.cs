using System.Text.Json;
using Tankstat.Application.Expenses;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Vehicles;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>
/// The order of a vehicle's logs for every sort field, from <c>contracts/log-order/log-order.json</c>: the same file the frontend's local
/// resolvers are tested against, so a grid served from the device offline pages exactly like the server (ties, missing values, case).
/// </summary>
public class SortParityTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly JsonElement Fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contracts", "log-order", "log-order.json"))).RootElement;

    private static decimal? Dec(JsonElement row, string name) => row.GetProperty(name).ValueKind == JsonValueKind.Null ? null : row.GetProperty(name).GetDecimal();
    private static long? Long(JsonElement row, string name) => row.GetProperty(name).ValueKind == JsonValueKind.Null ? null : row.GetProperty(name).GetInt64();
    private static string? Str(JsonElement row, string name) => row.GetProperty(name).GetString();

    public static TheoryData<string> RefuelingOrders => new(Fixture.GetProperty("refuelingOrders").EnumerateObject().Select(p => p.Name));
    public static TheoryData<string> ExpenseOrders => new(Fixture.GetProperty("expenseOrders").EnumerateObject().Select(p => p.Name));

    private static (TField Field, SortDirection Direction) Parse<TField>(string order) where TField : struct, Enum
    {
        var parts = order.Split(' ');
        return (Enum.Parse<TField>(parts[0].Replace("_", ""), ignoreCase: true), Enum.Parse<SortDirection>(parts[1], ignoreCase: true));
    }

    [Theory]
    [MemberData(nameof(RefuelingOrders))]
    public async Task Refuelings_AreOrderedAsTheFixtureSays(string order)
    {
        await using var db = new TestDatabase();
        var car = TestData.Vehicle(Owner);
        await db.Get<IVehicleRepository>().AddAsync(car, default);
        var repo = db.Get<IRefuelingRepository>();
        var withConsumption = new List<Refueling>();
        foreach (var row in Fixture.GetProperty("refuelings").EnumerateArray())
        {
            var date = DateOnly.Parse(Str(row, "date")!);
            var total = Dec(row, "totalCost");
            var odometer = Long(row, "odometer");
            var log = Refueling.Create(Owner, Owner, car.Id, date, Dec(row, "volume"), total is { } t ? Cost.Create(Owner, car.Id, date, t, "EUR") : null,
                odometer is { } o ? OdometerReading.Create(Owner, car.Id, date, o) : null, isFullTank: true, missedPreviousFillUp: false, readingPhotos: true,
                id: Guid.Parse(Str(row, "id")!));
            log.SetConsumption(Dec(row, "consumption"));
            await repo.AddAsync(log, default);
            withConsumption.Add(log);
        }
        await repo.SaveConsumptionsAsync(withConsumption, default);
        var (field, direction) = Parse<RefuelingSortField>(order);

        var listed = await repo.ListForVehicleAsync(car.Id, new RefuelingQuery(field, direction, 0, 100), default);

        Assert.Equal(Fixture.GetProperty("refuelingOrders").GetProperty(order).EnumerateArray().Select(e => e.GetString()), listed.Select(r => r.Id.ToString()));
    }

    [Theory]
    [MemberData(nameof(ExpenseOrders))]
    public async Task Expenses_AreOrderedAsTheFixtureSays(string order)
    {
        await using var db = new TestDatabase();
        var car = TestData.Vehicle(Owner);
        await db.Get<IVehicleRepository>().AddAsync(car, default);
        var repo = db.Get<IExpenseRepository>();
        foreach (var row in Fixture.GetProperty("expenses").EnumerateArray())
        {
            var date = DateOnly.Parse(Str(row, "date")!);
            var amount = Dec(row, "amount");
            var odometer = Long(row, "odometer");
            await repo.AddAsync(Expense.Create(Owner, Owner, car.Id, date, Str(row, "title")!, Str(row, "category"),
                amount is { } a ? Cost.Create(Owner, car.Id, date, a, "EUR") : null, odometer is { } o ? OdometerReading.Create(Owner, car.Id, date, o) : null,
                readingPhotos: true, id: Guid.Parse(Str(row, "id")!)), default);
        }
        var (field, direction) = Parse<ExpenseSortField>(order);

        var listed = await repo.ListForVehicleAsync(car.Id, new ExpenseQuery(field, direction, 0, 100), default);

        Assert.Equal(Fixture.GetProperty("expenseOrders").GetProperty(order).EnumerateArray().Select(e => e.GetString()), listed.Select(e => e.Id.ToString()));
    }
}
