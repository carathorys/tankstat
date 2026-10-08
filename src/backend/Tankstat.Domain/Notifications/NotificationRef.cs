namespace Tankstat.Domain.Notifications;

/// <summary>The kinds of things a notification can be about or point to. A new source of notifications adds its entity here.</summary>
public enum NotificationEntityType
{
    /// <summary>The whole instance (e.g. its default access level); the id is always empty.</summary>
    Instance,
    Vehicle,
    RecurringExpense,
    User,
    Refueling,
    Expense,
    SyncChange,
}

/// <summary>
/// An entity a notification is attached to, by type and id. Deliberately not a foreign key: the notification is history and stays
/// readable (its arguments are a snapshot) after the entity is gone; a link to it then just finds nothing.
/// </summary>
public readonly record struct NotificationRef(NotificationEntityType Type, Guid Id)
{
    public static NotificationRef Instance { get; } = new(NotificationEntityType.Instance, Guid.Empty);
    public static NotificationRef Vehicle(Guid id) => new(NotificationEntityType.Vehicle, id);
    public static NotificationRef RecurringExpense(Guid id) => new(NotificationEntityType.RecurringExpense, id);
    public static NotificationRef User(Guid id) => new(NotificationEntityType.User, id);
    public static NotificationRef Refueling(Guid id) => new(NotificationEntityType.Refueling, id);
    public static NotificationRef Expense(Guid id) => new(NotificationEntityType.Expense, id);
    public static NotificationRef SyncChange(Guid id) => new(NotificationEntityType.SyncChange, id);
}
