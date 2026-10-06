using Tankstat.Domain.Vehicles;

namespace Tankstat.Domain.Recurring;

/// <summary>
/// One schedule an expense covered when it was marked done: a service visit can do several schedules on one bill, and the one expense
/// logged for it remembers each of them. No owner column: the link goes with its schedule (and is removed in code with its expense).
/// </summary>
public sealed class RecurringCompletion
{
    private RecurringCompletion() { }

    public Guid ExpenseId { get; private set; }
    public Guid RecurringExpenseId { get; private set; }

    public static RecurringCompletion Create(Guid expenseId, Guid recurringExpenseId) => new() { ExpenseId = expenseId, RecurringExpenseId = recurringExpenseId };
}

/// <summary>What the one expense logged for several schedules is called when the person did not say (the dialog prefills the same).</summary>
public static class RecurringDoneDefaults
{
    /// <summary>The schedules' titles joined, cut to fit an expense title.</summary>
    public static string Title(IReadOnlyList<RecurringExpense> items)
    {
        var joined = string.Join(", ", items.Select(i => i.Title));
        return joined.Length <= Expense.MaxTitleLength ? joined : joined[..(Expense.MaxTitleLength - 1)].TrimEnd(' ', ',') + "…";
    }

    /// <summary>The first category any of them has (so their common one when they agree); none when none has one.</summary>
    public static string? Category(IReadOnlyList<RecurringExpense> items) => items.Select(i => i.Category).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    /// <summary>A schedule's note says something about that one thing: it goes with the expense only when it covers that one alone.</summary>
    public static string? Note(IReadOnlyList<RecurringExpense> items) => items.Count == 1 ? items[0].Note : null;
}
