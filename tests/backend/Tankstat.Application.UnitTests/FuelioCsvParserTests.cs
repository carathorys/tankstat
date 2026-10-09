using System.Text;
using Tankstat.Application.Imports;
using Tankstat.Domain;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class FuelioCsvParserTests
{
    private static ImportBatch Parse(string csv, bool bom = false)
    {
        var bytes = (bom ? Encoding.UTF8.GetPreamble() : []).Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return new FuelioCsvParser().Parse(new MemoryStream(bytes));
    }

    [Fact]
    public void ReadsTheVehicleSection_IncludingUnitsAndFuelType()
    {
        var vehicle = Parse(FuelioSample.Csv).Vehicle!;

        Assert.Equal(("Polo", "abc-123", FuelType.Diesel), (vehicle.Name, vehicle.LicensePlate, vehicle.FuelType));
        Assert.Equal((DistanceUnit.Miles, VolumeUnit.ImperialGallons), (vehicle.Distance, vehicle.Volume));
    }

    [Fact]
    public void ReadsFuelLogs_WithTheDateOnly_AndTheTotalPrice()
    {
        var logs = Parse(FuelioSample.Csv).FuelLogs;

        Assert.Equal(3, logs.Count);
        Assert.Equal(new ImportedFuelLog(1, new DateOnly(2026, 9, 17), 1300, 38.5m, 24669m, true, false, "Holiday, \"big\" trip"), logs[0]); // quoted comma and quotes survive
        Assert.False(logs[1].IsFullTank);
        Assert.Equal(new DateOnly(2026, 6, 1), logs[2].Date); // a date without a time
        Assert.Equal((700L, 35.25m), (logs[2].Odometer, logs[2].Volume));
    }

    [Fact]
    public void ReadsExpenses_WithCategoryNames_AndUnknownOdometerAsNull()
    {
        var expenses = Parse(FuelioSample.Csv).Expenses;

        Assert.Equal(3, expenses.Count);
        Assert.Equal(new ImportedExpense(1, new DateOnly(2026, 7, 20), "Oil change", "Service", 35000m, 1100, "with filters"), expenses[0]);
        Assert.Null(expenses[1].Odometer); // "0" means not noted
        Assert.Equal("Parking", expenses[1].Category);
        Assert.Equal(("Parking", "Parking"), (expenses[2].Title, expenses[2].Category)); // no title: the category stands in
    }

    [Fact]
    public void ReadsRepeatingTemplatesAsRecurringExpenses()
    {
        var recurring = Parse(FuelioSample.Csv).Recurring;

        Assert.Equal(2, recurring.Count);
        // No reminder date (2011-01-01 means none): counting starts from the row's own date and odometer.
        Assert.Equal(new ImportedRecurring(7, "Insurance", "Parking", "yearly", RecurrenceKind.Time, 12, null, new DateOnly(2026, 1, 10), 1000), recurring[0]);
        // A reminder that stands at 2027-02-01 / 21000 and repeats every 24 months / 15000: one interval earlier is the baseline.
        Assert.Equal(new ImportedRecurring(8, "Tyres", "Service", null, RecurrenceKind.Combined, 24, 15000, new DateOnly(2025, 2, 1), 6000), recurring[1]);
    }

    [Fact]
    public void ARepeatingTemplateNeedsOnlyOneOfTheIntervals()
    {
        var csv = FuelioSample.Csv.Replace("\"2011-01-01\",\"1\",\"0\",\"12\"", "\"2011-01-01\",\"1\",\"8000\",\"0\"");

        var insurance = Parse(csv).Recurring[0];

        Assert.Equal((RecurrenceKind.Odometer, (int?)null, (long?)8000), (insurance.Kind, insurance.IntervalMonths, insurance.IntervalDistance));
    }

    [Fact]
    public void SkipsTemplatesIncomeAndBrokenRows_AndSaysWhy()
    {
        var issues = Parse(FuelioSample.Csv).Issues;

        Assert.Equal(
            ["import.templateSkipped", "import.incomeSkipped", "import.badDate"],
            issues.Select(i => i.Key));
        Assert.Equal(["costs", "costs", "costs"], issues.Select(i => i.Section));
        Assert.Equal([3, 4, 6], issues.Select(i => i.Row));
    }

    [Fact]
    public void ReportsBadLogRows_AndKeepsTheGoodOnes()
    {
        var csv = FuelioSample.Csv.Replace("\"2026-07-11 18:37\",\"1000.0\",\"40.0\"", "\"2026-07-11 18:37\",\"1000.0\",\"0.0\"");

        var batch = Parse(csv);

        Assert.Equal(2, batch.FuelLogs.Count);
        var issue = Assert.Single(batch.Issues, i => i.Section == "log");
        Assert.Equal(("import.badNumber", 2), (issue.Key, issue.Row));
        Assert.Equal("volume", issue.Args["field"]);
    }

    [Fact]
    public void ToleratesABomAndWindowsLineEndings()
    {
        var batch = Parse(FuelioSample.Csv.Replace("\n", "\r\n"), bom: true);

        Assert.Equal((3, 3), (batch.FuelLogs.Count, batch.Expenses.Count));
    }

    [Fact]
    public void FindsColumnsByName_SoReorderedOrExtraColumnsDoNotMatter()
    {
        const string csv = "\"## Log\"\n\"Notes (optional)\",\"extra\",\"Fuel (litres)\",\"Price (optional)\",\"Full\",\"Odo (km)\",\"Data\"\n\"hi\",\"x\",\"10\",\"1000\",\"1\",\"500\",\"2026-01-02 10:00\"\n";

        var log = Assert.Single(Parse(csv).FuelLogs);

        Assert.Equal(new ImportedFuelLog(1, new DateOnly(2026, 1, 2), 500, 10, 1000, true, false, "hi"), log);
    }

    [Fact]
    public void AcceptsADecimalComma()
    {
        const string csv = "\"## Log\"\n\"Data\",\"Odo (km)\",\"Fuel (litres)\",\"Full\",\"Price (optional)\"\n\"2026-01-02\",\"500\",\"10,5\",\"1\",\"1000,5\"\n";

        var log = Assert.Single(Parse(csv).FuelLogs);

        Assert.Equal((10.5m, 1000.5m), (log.Volume, log.TotalCost));
    }

    [Theory]
    [InlineData("")]
    [InlineData("just,some,csv\n1,2,3\n")]
    [InlineData("\"## Vehicle\"\n\"Name\"\n\"Car\"\n")]
    public void RejectsFilesThatAreNotFuelioExports(string csv)
    {
        var error = Assert.Throws<DomainException>(() => Parse(csv));

        Assert.Equal("import.unreadable", error.Key);
    }

    [Fact]
    public void CsvReader_HandlesQuotedLineBreaksAndBlankLines()
    {
        var rows = CsvReader.Read(new StringReader("a,\"b\nc\",\"d\"\"e\"\n\n,x,\n")).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b\nc", "d\"e"], rows[0]);
        Assert.Equal(["", "x", ""], rows[1]);
    }

    /// <summary>One section of an export: its marker line, its header and its rows.</summary>
    private static string Section(string name, string header, params string[] rows) => $"\"## {name}\"\n{header}\n{string.Join("\n", rows)}\n";

    [Theory]
    [InlineData("0", "0", "100", DistanceUnit.Kilometers, VolumeUnit.Liters, FuelType.Petrol)]
    [InlineData("1", "1", "310", DistanceUnit.Miles, VolumeUnit.UsGallons, FuelType.Lpg)]
    [InlineData("2", "2", "200", null, VolumeUnit.ImperialGallons, FuelType.Diesel)]
    [InlineData("7", "9", "500", null, null, null)] // values Fuelio may add later: left for the person to choose
    [InlineData("", "", "none", null, null, null)]
    public void ReadsTheUnitsAndFuelTypesFuelioWrites_AndLeavesTheRestOpen(string dist, string fuel, string tank, DistanceUnit? distance, VolumeUnit? volume, FuelType? type)
    {
        var csv = Section("Vehicle", "Name,DistUnit,FuelUnit,Tank1Type", $"Car,{dist},{fuel},{tank}") + Section("Log", "Data,Odo,Fuel,Price");

        var vehicle = Parse(csv).Vehicle!;

        Assert.Equal((distance, volume, type), (vehicle.Distance, vehicle.Volume, vehicle.FuelType));
    }

    [Fact]
    public void AnExportWithOnlyOtherCosts_HasNoVehicleAndNoFillUps()
    {
        var batch = Parse(Section("Costs", "CostTitle,Date,Cost", "Parking,2026-01-02,5"));

        Assert.Null(batch.Vehicle);
        Assert.Empty(batch.FuelLogs);
        Assert.Equal("Parking", Assert.Single(batch.Expenses).Title);
    }

    [Fact]
    public void ReportsEveryKindOfBadFillUp_NamingTheField()
    {
        var csv = Section("Log", "Data,Odo,Fuel,Price",
            ",100,10,1000", // no date
            "2026-01-02,abc,10,1000",
            "2026-01-03,-5,10,1000",
            "2026-01-04,100,,1000", // no volume
            "2026-01-05,100,10,abc",
            "2026-01-06,100,10,-1",
            "2026-01-07,100,10,0"); // a free fill-up is still one

        var batch = Parse(csv);

        Assert.Equal(7, Assert.Single(batch.FuelLogs).SourceRow);
        Assert.Equal(
            [("import.badDate", ""), ("import.badNumber", "odometer"), ("import.badNumber", "odometer"), ("import.badNumber", "volume"), ("import.badNumber", "price"), ("import.badNumber", "price")],
            batch.Issues.Select(i => (i.Key, (string)i.Args["field"]!)));
        Assert.Equal([1, 2, 3, 4, 5, 6], batch.Issues.Select(i => i.Row));
        Assert.All(batch.Issues, i => Assert.Equal("log", i.Section));
    }

    [Fact]
    public void ReportsBadCosts_AndNamesACategoryOnlyWhenTheExportDefinesIt()
    {
        var csv = Section("CostCategories", "CostTypeID,Name", "1,Service", ",Nameless", "2,") +
            Section("Costs", "CostTitle,Date,Odo,CostTypeID,Cost",
                "Wash,2026-01-02,0,9,10", // a category the export does not define
                "Toll,2026-01-03,0,,5", // no category at all
                "Tax,2026-01-04,0,2,7", // a category without a name
                ",2026-01-05,0,9,7", // no title, and no category to stand in for it
                "Fee,2026-01-06,0,1,abc",
                "Fee,2026-01-07,0,1,-3",
                "Oil,2026-01-08,,1,20"); // the odometer left empty: not noted

        var batch = Parse(csv);

        Assert.Equal([("Wash", null), ("Toll", null), ("Tax", null), ("Oil", "Service")], batch.Expenses.Select(e => (e.Title, e.Category)));
        Assert.Null(batch.Expenses[^1].Odometer);
        Assert.Equal(["import.titleMissing", "import.badNumber", "import.badNumber"], batch.Issues.Select(i => i.Key));
        Assert.Equal([4, 5, 6], batch.Issues.Select(i => i.Row));
        Assert.Equal("", batch.Issues[0].Args["value"]); // nothing to quote
        Assert.Equal("cost", batch.Issues[1].Args["field"]);
    }

    [Fact]
    public void ReadsTemplates_ThatLackATitleOrADate_AsFarAsTheyCanBeRead()
    {
        var csv = Section("CostCategories", "CostTypeID,Name", "1,Service") +
            Section("Costs", "CostTitle,Date,Odo,CostTypeID,isTemplate,RepeatMonths,RepeatOdo,RemindDate,RemindOdo",
                ",2026-01-01,100,9,1,12,0,2011-01-01,0", // no title, and a category the export does not define: skipped
                ",2026-01-01,100,1,1,12,0,2011-01-01,0", // no title: its category names it
                "Inspection,bad,100,,1,12,0,2011-01-01,0", // no reminder date and no date of its own: nothing to count from
                "Inspection,bad,100,,1,12,0,2027-05-01,0", // a reminder date: one interval before it
                "Chain,2026-02-01,0,,1,0,5000,,3000"); // by distance, with a reminder below one interval and no odometer of its own

        var batch = Parse(csv);

        Assert.Equal(
            [
                new ImportedRecurring(2, "Service", "Service", null, RecurrenceKind.Time, 12, null, new DateOnly(2026, 1, 1), 100),
                new ImportedRecurring(4, "Inspection", null, null, RecurrenceKind.Time, 12, null, new DateOnly(2026, 5, 1), 100),
                new ImportedRecurring(5, "Chain", null, null, RecurrenceKind.Odometer, null, 5000, new DateOnly(2026, 2, 1), null),
            ],
            batch.Recurring);
        Assert.Equal([1, 3], batch.Issues.Select(i => i.Row));
        Assert.All(batch.Issues, i => Assert.Equal("import.templateSkipped", i.Key));
    }

    [Fact]
    public void ATitleLongerThanTheAppTakes_IsCut()
    {
        var title = new string('t', 300);
        var csv = Section("Costs", "CostTitle,Date,Cost,isTemplate,RepeatMonths", $"{title},2026-01-02,5,0,0", $"{title},2026-01-02,5,1,12");

        var batch = Parse(csv);

        Assert.Equal(Expense.MaxTitleLength, Assert.Single(batch.Expenses).Title.Length);
        Assert.Equal(RecurringExpense.MaxTitleLength, Assert.Single(batch.Recurring).Title.Length);
    }

    [Fact]
    public void ReadsAFillUpMarkedAsFollowingAMissedOne()
    {
        const string csv = "\"## Log\"\n\"Data\",\"Odo (km)\",\"Fuel (litres)\",\"Full\",\"Price (optional)\",\"Missed\"\n\"2026-01-02\",\"500\",\"10\",\"1\",\"1000\",\"1\"\n";

        Assert.True(Assert.Single(Parse(csv).FuelLogs).MissedPreviousFillUp);
    }
}
