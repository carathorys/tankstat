namespace Tankstat.Domain.Access;

/// <summary>
/// Implemented by every entity that belongs to a user. All reads and writes of such entities go through the
/// access layer (<see cref="AccessPolicy"/>), so a new entity type only has to implement this interface.
/// </summary>
public interface IOwned
{
    Guid OwnerId { get; }
}
