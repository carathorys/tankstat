using Tankstat.Domain.Access;

namespace Tankstat.Domain.Measurements;

/// <summary>
/// An amount of money together with the currency it was paid in, at a date. Entities that cost something (a refuelling today,
/// an inspection or service fee later) do not carry an amount of their own: they link to a cost. It shares the lifecycle of
/// the entity that owns it (trash, restore, delete), and all costs of a vehicle live in one table, so spending can later be
/// summed across kinds of entities.
/// </summary>
public sealed class Cost : IOwned, ISoftDeletable
{
    private Cost() { } // EF Core

    public Guid Id { get; private set; }

    /// <summary>The owner of the vehicle (denormalized, like on the logs).</summary>
    public Guid OwnerId { get; private set; }
    public Guid VehicleId { get; private set; }

    /// <summary>When it was paid.</summary>
    public DateOnly Date { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>ISO 4217 code of the currency it was paid in (can differ from cost to cost, e.g. when travelling).</summary>
    public string Currency { get; private set; } = "";
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static Cost Create(Guid ownerId, Guid vehicleId, DateOnly date, decimal amount, string? currency)
    {
        var cost = new Cost { Id = Guid.NewGuid(), OwnerId = ownerId, VehicleId = vehicleId };
        cost.Update(date, amount, currency);
        return cost;
    }

    public void Update(DateOnly date, decimal amount, string? currency)
    {
        if (amount < 0) throw new DomainException("cost.negative", "A cost cannot be negative.");
        Date = date;
        Amount = amount;
        Currency = CurrencyCode.Normalize(currency);
    }

    public void MarkDeleted(DateTimeOffset now) => DeletedAt = now;

    public void Restore() => DeletedAt = null;
}
