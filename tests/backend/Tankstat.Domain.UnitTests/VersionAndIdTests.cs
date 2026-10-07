using Tankstat.Domain;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Vehicles;
using Tankstat.TestSupport;

namespace Tankstat.Domain.UnitTests;

/// <summary>
/// A client may choose the id of what it adds (so a replayed add finds what it created), and every entity a client edits counts its saves
/// (so an edit of an old copy is noticed). Derived values and pictures do not count.
/// </summary>
public class VersionAndIdTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 1);
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static Refueling Waiting(Guid vehicle) =>
        Refueling.Create(Owner, Owner, vehicle, Day, null, null, null, true, false, readingPhotos: true);

    private static Expense Expense(bool readingPhotos = false)
    {
        var vehicle = Guid.NewGuid();
        return Vehicles.Expense.Create(Owner, Owner, vehicle, Day, "Service", null, readingPhotos ? null : Cost.Create(Owner, vehicle, Day, 10, "EUR"), null,
            readingPhotos: readingPhotos);
    }

    private static RecurringExpense Schedule(Guid? id = null) =>
        RecurringExpense.Create(Owner, Owner, Guid.NewGuid(), "Insurance", null, null, RecurrenceKind.Time, 12, null, Day, null, 30, 500, Now, id);

    [Fact]
    public void AnIdTheClientChose_IsKept_AndWithoutOneTheServerMakesOneUp()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, Vehicle.Create(Owner, "Car", null, FuelType.Petrol, MeasurementUnits.Metric, id).Id);
        Assert.Equal(id, Refueling.Create(Owner, Owner, Guid.NewGuid(), Day, 10, null, null, true, false, readingPhotos: true, id: id).Id);
        Assert.Equal(id, Schedule(id).Id);
        Assert.NotEqual(Guid.Empty, TestData.Vehicle(Owner).Id);
    }

    [Fact]
    public void AnEmptyId_IsRefused() =>
        Assert.Equal("id.empty", Assert.Throws<DomainException>(() => Vehicle.Create(Owner, "Car", null, FuelType.Petrol, MeasurementUnits.Metric, Guid.Empty)).Key);

    [Fact]
    public void AVehicle_CountsEdits_AndTheTrash_ButNotItsPicture()
    {
        var v = TestData.Vehicle(Owner);
        Assert.Equal(1, v.Version);

        v.Update("New", null, FuelType.Diesel, MeasurementUnits.Metric);
        v.SetPicture(Guid.NewGuid());
        Assert.Equal(2, v.Version);

        v.MarkDeleted(Now);
        v.Restore();
        Assert.Equal(4, v.Version);
    }

    [Fact]
    public void ARefueling_CountsEdits_TheTrash_AndWhatAPhotoFilledIn_ButNotItsConsumption()
    {
        var log = TestData.Refueling(Owner, Owner, Guid.NewGuid(), Day);
        Assert.Equal(1, log.Version);

        log.SetConsumption(6.5m);
        Assert.Equal(1, log.Version);

        log.Update(Day, 41, 61, "EUR", 1001, true, false, null);
        log.MarkDeleted(Now);
        log.Restore();
        Assert.Equal(4, log.Version);

        var waiting = Waiting(Guid.NewGuid());
        waiting.FillFromPhoto(new PhotoValues(Odometer: -1, Volume: 0, Total: null, Currency: null)); // nothing usable
        waiting.FinishReading(readingPhotos: true);
        Assert.Equal(1, waiting.Version);
        waiting.FillFromPhoto(new PhotoValues(Odometer: 1200, Volume: null, Total: null, Currency: null));
        Assert.Equal(2, waiting.Version);
    }

    [Fact]
    public void AnExpense_CountsEdits_TheTrash_AndWhatAPhotoFilledIn()
    {
        var e = Expense();
        Assert.Equal(1, e.Version);

        e.Update(Day, "Oil", null, 20, "EUR", null, null);
        e.MarkDeleted(Now);
        e.Restore();
        Assert.Equal(4, e.Version);

        var waiting = Expense(readingPhotos: true);
        waiting.FillFromPhoto(new PhotoValues(Odometer: null, Volume: null, Total: 30, Currency: "EUR"));
        Assert.Equal(2, waiting.Version);
    }

    [Fact]
    public void ASchedule_CountsEdits_AndBeingMarkedDone()
    {
        var s = Schedule();
        Assert.Equal(1, s.Version);

        s.Update("Insurance", null, null, RecurrenceKind.Time, 12, null, Day, null, 30, 500);
        s.MarkDone(Day.AddDays(1), null);
        Assert.Equal(3, s.Version);

        Assert.Throws<DomainException>(() => s.MarkDone(Day, null)); // refused: nothing counted
        Assert.Equal(3, s.Version);
    }
}
