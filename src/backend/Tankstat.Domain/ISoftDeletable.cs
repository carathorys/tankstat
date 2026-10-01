namespace Tankstat.Domain;

/// <summary>
/// Entities that are first moved to a "trash" (logical deletion with a timestamp) and only physically removed
/// later, either by the scheduled purge or when a user empties the trash.
/// </summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; }
}
