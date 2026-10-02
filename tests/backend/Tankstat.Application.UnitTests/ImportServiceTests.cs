using System.Text;
using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Application.Imports;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class ImportServiceTests
{
    private sealed record Scene(World W, User Alice, User Bob, Vehicle Car);

    private static async Task<Scene> Setup()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        var car = await w.VehicleService.AddAsync("Car", null, FuelType.Diesel, MeasurementUnits.Metric, default);
        return new Scene(w, alice, bob, car);
    }

    private static Stream File(string csv = FuelioSample.Csv) => new MemoryStream(Encoding.UTF8.GetBytes(csv));

    private static ImportOptions Options(bool duplicates = false, string? currency = "EUR") => new(currency, duplicates);

    private static Task<ImportUpload> Upload(Scene s, string csv = FuelioSample.Csv) => s.W.Imports.UploadAsync("fuelio", File(csv), default);

    // ---- upload --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_ParsesTheFile_AndSavesNothing()
    {
        var s = await Setup();

        var upload = await Upload(s);

        Assert.Equal("fuelio", upload.Format);
        Assert.NotEmpty(upload.Token);
        Assert.Empty(s.W.Refuelings.Items);
        Assert.Empty(s.W.Expenses.Items);
    }

    [Fact]
    public async Task Upload_RejectsUnknownFormatsAndEmptyFiles()
    {
        var s = await Setup();

        var unknown = await Assert.ThrowsAsync<DomainException>(() => s.W.Imports.UploadAsync("excel", File(), default));
        var empty = await Assert.ThrowsAsync<DomainException>(() => s.W.Imports.UploadAsync("fuelio", File("\"## Log\"\n\"Data\",\"Odo (km)\",\"Fuel (litres)\",\"Price (optional)\"\n"), default));

        Assert.Equal(("import.unknownFormat", "import.nothingFound"), (unknown.Key, empty.Key));
    }

    [Fact]
    public async Task Upload_NeedsASignedInUser()
    {
        var s = await Setup();
        s.W.Current.Principal = null;

        await Assert.ThrowsAsync<UnauthenticatedException>(() => Upload(s));
    }

    // ---- preview -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Preview_SummarisesTheFile()
    {
        var s = await Setup();
        var upload = await Upload(s);

        var preview = await s.W.Imports.PreviewAsync(upload.Token, null, default);

        Assert.Equal((3, 3, 2), (preview.FuelRows, preview.ExpenseRows, preview.RecurringRows));
        Assert.Equal((new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 17)), (preview.FirstDate, preview.LastDate));
        Assert.Equal(["Parking", "Service"], preview.Categories);
        Assert.Equal("Polo", preview.SourceVehicle!.Name);
        Assert.Equal((0, 0, 0), (preview.DuplicateFuelRows, preview.DuplicateExpenseRows, preview.DuplicateRecurringRows));
    }

    [Fact]
    public async Task Preview_CountsDuplicatesOfAnExistingVehicle_AndWarnsAboutDifferentUnits()
    {
        var s = await Setup();
        await s.W.RefuelingService.LogAsync(s.Car.Id, new Application.Refuelings.RefuelingInput(new DateOnly(2026, 7, 11), 40, 100, "EUR", 1000, true, null), default);
        await s.W.ExpenseService.AddAsync(s.Car.Id, new Application.Expenses.ExpenseInput(new DateOnly(2026, 7, 20), "OIL CHANGE", null, 35000, "EUR", null, null), default);
        await s.W.RecurringService.AddAsync(s.Car.Id, new Application.Recurring.RecurringExpenseInput("insurance", null, null, RecurrenceKind.Time, 12, null, new DateOnly(2026, 1, 1), null, null, null), default);
        var upload = await Upload(s);

        var preview = await s.W.Imports.PreviewAsync(upload.Token, s.Car.Id, default);

        Assert.Equal((1, 1, 1), (preview.DuplicateFuelRows, preview.DuplicateExpenseRows, preview.DuplicateRecurringRows)); // expenses and schedules match ignoring case
        Assert.Contains(preview.Issues, i => i.Key == "import.unitsDiffer"); // the file says miles / imperial gallons
    }

    [Fact]
    public async Task Preview_NeedsEditAccessToTheTargetVehicle()
    {
        var s = await Setup();
        var upload = await Upload(s);
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.View));
        s.W.Current.SignInAs(s.Bob);
        var bobsUpload = await Upload(s);

        await Assert.ThrowsAsync<ForbiddenException>(() => s.W.Imports.PreviewAsync(bobsUpload.Token, s.Car.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.Imports.PreviewAsync(upload.Token, s.Car.Id, default)); // someone else's token looks expired
    }

    [Fact]
    public async Task Preview_OfAVehicleTheUserCannotSee_LooksMissing()
    {
        var s = await Setup();
        s.W.Current.SignInAs(s.Bob);
        var upload = await Upload(s);

        var error = await Assert.ThrowsAsync<NotFoundException>(() => s.W.Imports.PreviewAsync(upload.Token, s.Car.Id, default));

        Assert.Equal("vehicle.notFound", error.Key);
    }

    // ---- commit --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Commit_IntoANewVehicle_CreatesItAndSavesEveryRow()
    {
        var s = await Setup();
        var upload = await Upload(s);

        var result = await s.W.Imports.CommitAsync(upload.Token, new ImportTarget(null, new NewVehicleSpec("Polo", "abc-123", FuelType.Diesel, MeasurementUnits.Create(DistanceUnit.Miles, VolumeUnit.ImperialGallons))), Options(), default);

        Assert.Equal((3, 3, 2, 0, 0, 0), (result.FuelImported, result.ExpensesImported, result.RecurringImported, result.FuelSkippedDuplicates, result.ExpensesSkippedDuplicates, result.RecurringSkippedDuplicates));
        Assert.Empty(result.Errors);
        var vehicle = s.W.Vehicles.Items.Single(v => v.Id == result.VehicleId);
        Assert.Equal(("Polo", "ABC-123", DistanceUnit.Miles, VolumeUnit.ImperialGallons), (vehicle.Name, vehicle.LicensePlate, vehicle.Units.Distance, vehicle.Units.Volume));
        Assert.Equal(3, s.W.Refuelings.Items.Count(r => r.VehicleId == result.VehicleId));
        Assert.Equal(3, s.W.Expenses.Items.Count(e => e.VehicleId == result.VehicleId));
        var schedules = s.W.Recurring.Items.Where(r => r.VehicleId == result.VehicleId).OrderBy(r => r.Title).ToList();
        Assert.Equal(["Insurance", "Tyres"], schedules.Select(r => r.Title));
        Assert.Equal((RecurrenceKind.Time, new DateOnly(2026, 1, 10)), (schedules[0].Kind, schedules[0].LastDoneDate));
        Assert.Equal((RecurrenceKind.Combined, 15000L, 6000L), (schedules[1].Kind, schedules[1].IntervalDistance, schedules[1].LastDoneOdometer));
    }

    [Fact]
    public async Task Commit_ReportsARecurringExpenseThatBreaksARule_AndImportsTheRest()
    {
        var s = await Setup();
        var upload = await Upload(s, FuelioSample.Csv.Replace("\"Insurance\",\"2026-01-10\"", "\"Insurance\",\"2030-01-10\""));

        var result = await s.W.Imports.CommitAsync(upload.Token, new ImportTarget(null, new NewVehicleSpec("Polo", null, FuelType.Diesel, MeasurementUnits.Metric)), Options(), default);

        Assert.Equal(1, result.RecurringImported);
        var error = Assert.Single(result.Errors);
        Assert.Equal(("costs", 7, "refueling.dateInFuture"), (error.Section, error.Row, error.Key));
    }

    [Fact]
    public async Task Commit_StartsADistanceScheduleWithoutAnOdometerFromTheImportedReadings()
    {
        var s = await Setup();
        // The reminder neither says where it stands nor has an odometer of its own.
        var upload = await Upload(s, FuelioSample.Csv.Replace("\"Tyres\",\"2026-02-01\",\"900\"", "\"Tyres\",\"2026-02-01\",\"0\"").Replace("\"21000\",\"2027-02-01\"", "\"0\",\"2011-01-01\""));

        var result = await s.W.Imports.CommitAsync(upload.Token, new ImportTarget(null, new NewVehicleSpec("Polo", null, FuelType.Diesel, MeasurementUnits.Metric)), Options(), default);

        Assert.Empty(result.Errors);
        Assert.Equal(1300L, s.W.Recurring.Items.Single(r => r.Title == "Tyres").LastDoneOdometer); // the latest fill-up of the file
    }

    [Fact]
    public async Task Commit_StoresRowsAsTypedByHand_WithTheChosenCurrency_AndTheCreator()
    {
        var s = await Setup();
        var upload = await Upload(s);

        await s.W.Imports.CommitAsync(upload.Token, new ImportTarget(s.Car.Id, null), Options(currency: "huf"), default);

        var fill = s.W.Refuelings.Items.Single(r => r.Odometer == 1300);
        Assert.Equal((new DateOnly(2026, 9, 17), 38.5m, 24669m, "HUF", true, s.Alice.Id), (fill.Date, fill.Volume, fill.TotalCost, fill.Currency, fill.IsFullTank, fill.CreatedById));
        var oil = s.W.Expenses.Items.Single(e => e.Title == "Oil change");
        Assert.Equal(("Service", 35000m, "HUF", 1100L), (oil.Category, oil.Amount, oil.Currency, oil.Odometer));
        Assert.Null(s.W.Expenses.Items.Single(e => e.Title == "Garage").Odometer);
    }

    [Fact]
    public async Task Commit_UsesTheInstanceCurrency_WhenNoneIsGiven()
    {
        var s = await Setup();
        var upload = await Upload(s);

        await s.W.Imports.CommitAsync(upload.Token, new ImportTarget(s.Car.Id, null), Options(currency: null), default);

        Assert.All(s.W.Refuelings.Items, r => Assert.Equal("HUF", r.Currency));
    }

    [Fact]
    public async Task Commit_SkipsDuplicatesUnlessAsked()
    {
        var s = await Setup();
        var first = await Upload(s);
        await s.W.Imports.CommitAsync(first.Token, new ImportTarget(s.Car.Id, null), Options(), default);

        var again = await Upload(s);
        var skipped = await s.W.Imports.CommitAsync(again.Token, new ImportTarget(s.Car.Id, null), Options(), default);

        Assert.Equal((0, 0, 0, 3, 3, 2), (skipped.FuelImported, skipped.ExpensesImported, skipped.RecurringImported, skipped.FuelSkippedDuplicates, skipped.ExpensesSkippedDuplicates, skipped.RecurringSkippedDuplicates));
        Assert.Equal(3, s.W.Refuelings.Items.Count);
        Assert.Equal(2, s.W.Recurring.Items.Count);
    }

    [Fact]
    public async Task Commit_CanImportDuplicatesToo_ButTheOdometerRulesStillApply()
    {
        var s = await Setup();
        await s.W.Imports.CommitAsync((await Upload(s)).Token, new ImportTarget(s.Car.Id, null), Options(), default);

        var result = await s.W.Imports.CommitAsync((await Upload(s)).Token, new ImportTarget(s.Car.Id, null), Options(duplicates: true), default);

        Assert.Equal((3, 3, 0, 0), (result.FuelImported, result.ExpensesImported, result.FuelSkippedDuplicates, result.ExpensesSkippedDuplicates));
        Assert.Equal(6, s.W.Refuelings.Items.Count); // equal readings are allowed
    }

    [Fact]
    public async Task Commit_ReportsRowsThatBreakARule_AndImportsTheRest()
    {
        var s = await Setup();
        await s.W.RefuelingService.LogAsync(s.Car.Id, new Application.Refuelings.RefuelingInput(new DateOnly(2026, 7, 1), 40, 100, "EUR", 2000, true, null), default); // already higher than the file's later readings

        var upload = await Upload(s);
        var result = await s.W.Imports.CommitAsync(upload.Token, new ImportTarget(s.Car.Id, null), Options(), default);

        // 2026-06-01 at 700 fits before 2000; every reading after 2026-07-01 is lower than 2000 and is rejected.
        Assert.Equal(1, result.FuelImported);
        Assert.Contains(result.Errors, e => e.Section == "log" && e.Key == "odometer.belowPrevious");
        var error = result.Errors.First(e => e.Key == "odometer.belowPrevious");
        Assert.Equal(2000L, error.Args["previous"]);
        Assert.Equal(2, s.W.Refuelings.Items.Count);
    }

    [Fact]
    public async Task Commit_ReportsFutureDates()
    {
        var s = await Setup();
        var csv = FuelioSample.Csv.Replace("2026-09-17 18:15", "2030-01-01 10:00");
        var upload = await Upload(s, csv);

        var result = await s.W.Imports.CommitAsync(upload.Token, new ImportTarget(s.Car.Id, null), Options(), default);

        Assert.Equal(2, result.FuelImported);
        Assert.Contains(result.Errors, e => e.Key == "refueling.dateInFuture");
    }

    [Fact]
    public async Task Commit_NeedsEditAccess_AndSavesNothingOtherwise()
    {
        var s = await Setup();
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.View));
        s.W.Current.SignInAs(s.Bob);
        var upload = await Upload(s);

        await Assert.ThrowsAsync<ForbiddenException>(() => s.W.Imports.CommitAsync(upload.Token, new ImportTarget(s.Car.Id, null), Options(), default));

        Assert.Empty(s.W.Refuelings.Items);
        Assert.Empty(s.W.Expenses.Items);
    }

    [Fact]
    public async Task Commit_AllowsLogGranteesToImportIntoTheSharedVehicle()
    {
        var s = await Setup();
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default);
        s.W.Current.SignInAs(s.Bob);

        var result = await s.W.Imports.CommitAsync((await Upload(s)).Token, new ImportTarget(s.Car.Id, null), Options(), default);

        Assert.Equal((3, 3), (result.FuelImported, result.ExpensesImported));
        Assert.All(s.W.Refuelings.Items, r => Assert.Equal(s.Bob.Id, r.CreatedById));
    }

    [Fact]
    public async Task Commit_NeedsATarget_AndATokenIsUsedOnce()
    {
        var s = await Setup();
        var upload = await Upload(s);

        var none = await Assert.ThrowsAsync<DomainException>(() => s.W.Imports.CommitAsync(upload.Token, new ImportTarget(null, null), Options(), default));
        await s.W.Imports.CommitAsync(upload.Token, new ImportTarget(s.Car.Id, null), Options(), default);
        var again = await Assert.ThrowsAsync<NotFoundException>(() => s.W.Imports.CommitAsync(upload.Token, new ImportTarget(s.Car.Id, null), Options(), default));

        Assert.Equal(("import.targetRequired", "import.expired"), (none.Key, again.Key));
    }

    [Fact]
    public async Task Tokens_ExpireAfterHalfAnHour()
    {
        var s = await Setup();
        var upload = await Upload(s);

        s.W.Clock.Advance(TimeSpan.FromMinutes(31));

        await Assert.ThrowsAsync<NotFoundException>(() => s.W.Imports.PreviewAsync(upload.Token, null, default));
    }

    [Fact]
    public async Task Commit_RejectsAnInvalidCurrency_BeforeCreatingAnything()
    {
        var s = await Setup();
        var upload = await Upload(s);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            s.W.Imports.CommitAsync(upload.Token, new ImportTarget(null, new NewVehicleSpec("New", null, FuelType.Petrol, MeasurementUnits.Metric)), Options(currency: "euro"), default));

        Assert.Equal("money.currencyInvalid", error.Key);
        Assert.Single(s.W.Vehicles.Items); // only the car from the setup
    }
}
