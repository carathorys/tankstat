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
        Assert.Equal(new ImportedFuelLog(1, new DateOnly(2026, 9, 17), 1300, 38.5m, 24669m, true, "Holiday, \"big\" trip"), logs[0]); // quoted comma and quotes survive
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

        Assert.Equal(new ImportedFuelLog(1, new DateOnly(2026, 1, 2), 500, 10, 1000, true, "hi"), log);
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
}
