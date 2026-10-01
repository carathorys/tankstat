using Tankstat.Domain;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Domain.UnitTests;

public class ExpenseTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Car = Guid.NewGuid();

    private static Expense Make(string title = "Oil change", string? category = "Service", decimal amount = 100, long? odometer = 5000, string? note = null)
    {
        var cost = Cost.Create(Owner, Car, Day, amount, "EUR");
        var reading = odometer is { } v ? OdometerReading.Create(Owner, Car, Day, v) : null;
        return Expense.Create(Owner, Owner, Car, Day, title, category, cost, reading, note);
    }

    [Fact]
    public void Create_TrimsText_AndExposesTheLinkedValues()
    {
        var expense = Make(" Oil change ", " Service ", 99.5m, 5000, " with filter ");

        Assert.Equal(("Oil change", "Service", "with filter"), (expense.Title, expense.Category, expense.Note));
        Assert.Equal((99.5m, "EUR", 5000L), (expense.Amount, expense.Currency, expense.Odometer));
    }

    [Fact]
    public void TheOdometerIsOptional()
    {
        var expense = Make(odometer: null);

        Assert.Null(expense.Odometer);
        Assert.Null(expense.OdometerReadingId);
    }

    [Fact]
    public void BlankCategoryAndNoteBecomeNull()
    {
        var expense = Make(category: " ", note: "");

        Assert.Equal((null, null), (expense.Category, expense.Note));
    }

    [Theory]
    [InlineData(" ", "expense.titleRequired")]
    [InlineData("TOOLONG", "expense.titleTooLong")]
    public void RejectsBadTitles(string title, string key)
    {
        if (title == "TOOLONG") title = new string('x', Expense.MaxTitleLength + 1);

        Assert.Equal(key, Assert.Throws<DomainException>(() => Make(title)).Key);
    }

    [Fact]
    public void RejectsLongCategoriesNotesAndNegativeAmounts()
    {
        Assert.Equal("expense.categoryTooLong", Assert.Throws<DomainException>(() => Make(category: new string('c', Expense.MaxCategoryLength + 1))).Key);
        Assert.Equal("expense.noteTooLong", Assert.Throws<DomainException>(() => Make(note: new string('n', Expense.MaxNoteLength + 1))).Key);
        Assert.Equal("cost.negative", Assert.Throws<DomainException>(() => Make(amount: -1)).Key);
    }

    [Fact]
    public void ACostOrReadingOfAnotherVehicleIsRejected()
    {
        var cost = Cost.Create(Owner, Guid.NewGuid(), Day, 1, "EUR");

        var error = Assert.Throws<DomainException>(() => Expense.Create(Owner, Owner, Car, Day, "x", null, cost, null));

        Assert.Equal("expense.wrongVehicle", error.Key);
    }

    [Fact]
    public void Update_ChangesEverything_AndKeepsTheReadingInStep()
    {
        var expense = Make();
        var other = new DateOnly(2026, 9, 5);

        var detached = expense.Update(other, "Tyres", "Maintenance", 400, "HUF", 5200, "four", out var created);

        Assert.Null(detached);
        Assert.Null(created);
        Assert.Equal((other, "Tyres", "Maintenance", 400m, "HUF", 5200L), (expense.Date, expense.Title, expense.Category, expense.Amount, expense.Currency, expense.Odometer));
        Assert.Equal(other, expense.OdometerReading!.Date);
        Assert.Equal(other, expense.Cost.Date);
    }

    [Fact]
    public void Update_CanAddAndRemoveTheOdometerReading()
    {
        var expense = Make(odometer: null);

        expense.Update(Day, "x", null, 1, "EUR", 777, null, out var created);
        Assert.Equal((created, 777L), (expense.OdometerReading, expense.Odometer));
        Assert.Equal(created!.Id, expense.OdometerReadingId);

        var detached = expense.Update(Day, "x", null, 1, "EUR", null, null, out var none);
        Assert.Same(created, detached);
        Assert.Null(none);
        Assert.Null(expense.OdometerReadingId);
    }

    [Fact]
    public void TrashAndRestore_CarryTheCostAndReading()
    {
        var expense = Make();
        var now = DateTimeOffset.UtcNow;

        expense.MarkDeleted(now);
        Assert.True(expense.IsDeleted && expense.Cost.IsDeleted && expense.OdometerReading!.IsDeleted);
        Assert.Equal("expense.alreadyTrashed", Assert.Throws<DomainException>(() => expense.MarkDeleted(now)).Key);
        Assert.Equal("expense.trashedCannotEdit", Assert.Throws<DomainException>(() => expense.Update(Day, "x", null, 1, "EUR", null, null, out _)).Key);

        expense.Restore();
        Assert.False(expense.IsDeleted || expense.Cost.IsDeleted || expense.OdometerReading!.IsDeleted);
        Assert.Equal("expense.notTrashed", Assert.Throws<DomainException>(expense.Restore).Key);
    }
}
