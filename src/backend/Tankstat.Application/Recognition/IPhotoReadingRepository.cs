using Tankstat.Domain.Recognition;

namespace Tankstat.Application.Recognition;

public interface IPhotoReadingRepository
{
    Task AddAsync(PhotoReading reading, CancellationToken ct);
    Task<IReadOnlyList<PhotoReading>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>The ids of queued readings that are due, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset now, int max, CancellationToken ct);

    /// <summary>
    /// Starts an attempt at a due reading (<see cref="PhotoReading.Claim"/>) in one atomic step, so only one worker gets it; null when
    /// it is not queued and due any more.
    /// </summary>
    Task<PhotoReading?> ClaimAsync(Guid id, DateTimeOffset now, CancellationToken ct);

    /// <summary>Saves the outcome of an attempt: the result, a retry or the failure.</summary>
    Task SaveAsync(PhotoReading reading, CancellationToken ct);

    /// <summary>Readings marked as being read since before <paramref name="claimedBefore"/>: the app stopped while reading them.</summary>
    Task<IReadOnlyList<PhotoReading>> ListStaleAsync(DateTimeOffset claimedBefore, CancellationToken ct);

    Task RemoveAsync(Guid id, CancellationToken ct);
}
