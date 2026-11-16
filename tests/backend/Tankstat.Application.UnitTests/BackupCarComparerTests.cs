using Tankstat.Application.Expenses;
using Tankstat.Application.Imports;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

/// <summary>A backup's car against a car that is here (#336): the same, differs, or only in the backup; by id inside the target, then by key.</summary>
public class BackupCarComparerTests
{
    private static readonly DateOnly Day = new(2026, 3, 1);
    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private static BackupRefueling Fill(int id, DateOnly date, long? odometer, decimal volume = 40, string? note = null, decimal? cost = 60) =>
        new(Id(id), new RefuelingInput(date, volume, cost, cost is null ? null : "EUR", odometer, true, note));

    private static BackupExpense Cost(int id, DateOnly date, string title, decimal? amount, string? category = null) =>
        new(Id(id), new ExpenseInput(date, title, category, amount, amount is null ? null : "EUR", null, null), []);

    private static BackupSchedule Plan(int id, string title, int months = 12) =>
        new(Id(id), new RecurringExpenseInput(title, null, null, RecurrenceKind.Time, months, null, Day, null, 30, 500));

    private static BackupCar Car(IEnumerable<BackupRefueling>? fills = null, IEnumerable<BackupExpense>? costs = null, IEnumerable<BackupSchedule>? plans = null) =>
        new(new BackupCarSummary(Id(999), "Octavia", null, FuelType.Petrol, MeasurementUnits.Metric, Id(998), "Alice", 0, 0, 0), [.. fills ?? []], [.. costs ?? []], [.. plans ?? []]);

    [Fact]
    public void TheSameById_IsTheSame_WhateverWasWorkedOutOrHowANumberIsWritten()
    {
        var result = BackupCarComparer.Compare(
            Car([Fill(1, Day, 10_000, volume: 40.0m, note: "")]),
            Car([Fill(1, Day, 10_000, volume: 40m, note: null)]));

        var entry = Assert.Single(result.Refuelings.Entries);
        Assert.Equal((BackupEntryMatch.Same, Id(1), true), (entry.Match, entry.TargetId, entry.MatchedById));
        Assert.Empty(entry.Differences);
        Assert.Equal((0, 0, 1), (result.Refuelings.OnlyInBackup, result.Refuelings.Differs, result.Refuelings.Same));
    }

    [Fact]
    public void AnEntryWithAnotherId_IsTheTargetsByItsKey_ARefuellingByDateAndOdometer_AnExpenseByDateTitleAndAmount_AScheduleByTitle()
    {
        var result = BackupCarComparer.Compare(
            Car([Fill(1, Day, 10_000)], [Cost(2, Day, "  Car WASH ", 12)], [Plan(3, "Oil Change")]),
            Car([Fill(11, Day, 10_000)], [Cost(12, Day, "car wash", 12.00m)], [Plan(13, "oil change")]));

        Assert.Equal((BackupEntryMatch.Same, Id(11), false), (result.Refuelings.Entries[0].Match, result.Refuelings.Entries[0].TargetId, result.Refuelings.Entries[0].MatchedById));
        // The title is the key without its case; as a value it differs, which an edit could change.
        Assert.Equal((BackupEntryMatch.Differs, Id(12)), (result.Expenses.Entries[0].Match, result.Expenses.Entries[0].TargetId));
        Assert.Equal(new BackupFieldDifference("title", "Car WASH", "car wash"), Assert.Single(result.Expenses.Entries[0].Differences));
        Assert.Equal((BackupEntryMatch.Differs, Id(13)), (result.Schedules.Entries[0].Match, result.Schedules.Entries[0].TargetId));
    }

    [Fact]
    public void AnEntryThatDiffers_SaysWhichFields_WithBothSidesValues()
    {
        var result = BackupCarComparer.Compare(
            Car([Fill(1, Day, 10_000, volume: 41.5m, note: "Full")], plans: [Plan(3, "Oil", months: 24)]),
            Car([Fill(1, Day, 10_050, volume: 40m)], plans: [Plan(3, "Oil", months: 12)]));

        var fill = Assert.Single(result.Refuelings.Entries);
        Assert.Equal(BackupEntryMatch.Differs, fill.Match);
        Assert.Equal(
            [new BackupFieldDifference("volume", 41.5m, 40m), new BackupFieldDifference("odometer", 10_000L, 10_050L), new BackupFieldDifference("note", "Full", null)],
            fill.Differences);
        Assert.Equal(new BackupFieldDifference("intervalMonths", 24, 12), Assert.Single(Assert.Single(result.Schedules.Entries).Differences));
    }

    [Fact]
    public void WhatTheTargetLacks_IsOnlyInTheBackup_AlsoWhenItsIdIsSomeoneElsesHere()
    {
        // The target is one car's entries: an id that exists in another person's car is not among them, and tells nothing.
        var result = BackupCarComparer.Compare(Car([Fill(1, Day, 10_000), Fill(2, Day.AddDays(7), 10_600)]), Car([Fill(1, Day, 10_000)]));

        Assert.Equal([BackupEntryMatch.Same, BackupEntryMatch.OnlyInBackup], result.Refuelings.Entries.Select(e => e.Match));
        Assert.Equal((null, false), (result.Refuelings.Entries[1].TargetId, result.Refuelings.Entries[1].MatchedById));
    }

    [Fact]
    public void TwoEntriesOfTheBackupOnOneOfTheTargets_TheFirstByDateThenIdTakesIt_TheOthersAreOnlyInTheBackup()
    {
        var result = BackupCarComparer.Compare(
            Car(costs: [Cost(5, Day, "Parking", 3), Cost(4, Day, "Parking", 3), Cost(6, Day, "Parking", 3)]),
            Car(costs: [Cost(40, Day, "Parking", 3), Cost(41, Day, "Parking", 3)]));

        // In the backup's order; the two of the target go to the two lowest ids.
        Assert.Equal([(Id(5), BackupEntryMatch.Same, (Guid?)Id(41)), (Id(4), BackupEntryMatch.Same, Id(40)), (Id(6), BackupEntryMatch.OnlyInBackup, null)],
            result.Expenses.Entries.Select(e => (e.Entry.Id, e.Match, e.TargetId)));
    }

    [Fact]
    public void AKeyNeverTakesTheEntryAnotherOneIsById()
    {
        // The backup's 2 shares its key with the target's 1, which is the backup's 1 by id.
        var result = BackupCarComparer.Compare(
            Car([Fill(2, Day, 10_000, note: "copy"), Fill(1, Day, 10_000)]),
            Car([Fill(1, Day, 10_000)]));

        Assert.Equal([(Id(2), BackupEntryMatch.OnlyInBackup), (Id(1), BackupEntryMatch.Same)], result.Refuelings.Entries.Select(e => (e.Entry.Id, e.Match)));
    }

    [Fact]
    public void TheEntriesComeInPages_AlsoOfOneAnswerOnly()
    {
        var backup = Car(Enumerable.Range(1, 120).Select(n => Fill(n, Day.AddDays(n), 10_000 + n * 100)));
        var target = Car(Enumerable.Range(1, 120).Where(n => n % 2 == 0).Select(n => Fill(n, Day.AddDays(n), 10_000 + n * 100)));

        var result = BackupCarComparer.Compare(backup, target).Refuelings;

        Assert.Equal((60, 0, 60), (result.OnlyInBackup, result.Differs, result.Same));
        Assert.Equal(Enumerable.Range(51, 50).Select(Id), result.Page(50, 50).Select(e => e.Entry.Id));
        Assert.Equal(new[] { 101, 103, 105 }.Select(Id), result.Page(50, 3, BackupEntryMatch.OnlyInBackup).Select(e => e.Entry.Id));
        Assert.Equal(120, result.Page(0, 5000).Count);
        Assert.Equal(200, new BackupKindComparison<BackupRefueling>([.. Enumerable.Range(1, 300).Select(n => new BackupEntryComparison<BackupRefueling>(Fill(n, Day, n), BackupEntryMatch.Same, null, false, []))]).Page(0, 5000).Count); // a page is never more than 200
    }
}
