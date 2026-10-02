using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Tankstat.Application.Imports;

/// <summary>
/// Holds a parsed file for a short while between the upload and the user's confirmation, so the file is not sent twice and nothing is
/// saved before the user has seen the preview. A batch belongs to the user who uploaded it. In memory only: a restart (or a second
/// instance) simply asks the user to upload again.
/// </summary>
public sealed class ImportSessionStore(TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private const int MaxPerUser = 10;

    private sealed record Entry(Guid UserId, ImportBatch Batch, DateTimeOffset Expires);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public string Save(Guid userId, ImportBatch batch)
    {
        Sweep();
        foreach (var old in _entries.Where(e => e.Value.UserId == userId).OrderBy(e => e.Value.Expires).SkipLast(MaxPerUser - 1).ToList())
            _entries.TryRemove(old.Key, out _);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _entries[token] = new Entry(userId, batch, clock.GetUtcNow() + Lifetime);
        return token;
    }

    /// <summary>Null when the token is unknown, expired, or belongs to someone else.</summary>
    public ImportBatch? Find(Guid userId, string token) =>
        _entries.TryGetValue(token, out var e) && e.UserId == userId && e.Expires > clock.GetUtcNow() ? e.Batch : null;

    public void Remove(string token) => _entries.TryRemove(token, out _);

    /// <summary>Drops everything the user still had pending (used when the account is deleted).</summary>
    public void RemoveForUser(Guid userId)
    {
        foreach (var key in _entries.Where(e => e.Value.UserId == userId).Select(e => e.Key).ToList()) _entries.TryRemove(key, out _);
    }

    private void Sweep()
    {
        var now = clock.GetUtcNow();
        foreach (var expired in _entries.Where(e => e.Value.Expires <= now).Select(e => e.Key).ToList()) _entries.TryRemove(expired, out _);
    }
}
