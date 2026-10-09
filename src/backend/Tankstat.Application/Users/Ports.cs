using Tankstat.Domain.Users;

namespace Tankstat.Application.Users;

public interface IUserRepository
{
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct);

    /// <summary>The users with these ids in one query (lists: who logged each row); unknown ids are skipped.</summary>
    Task<IReadOnlyList<User>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<User?> FindLocalByEmailAsync(string normalizedEmail, CancellationToken ct);
    Task<User?> FindExternalAsync(UserProvider provider, string subject, CancellationToken ct);
    Task<User?> FindByAvatarImageAsync(Guid imageId, CancellationToken ct);
    Task<IReadOnlyList<User>> ListAsync(CancellationToken ct);
    Task<bool> AnyLocalAdminAsync(CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
    Task UpdateAsync(User user, CancellationToken ct);
}

public interface IPasswordResetTokenRepository
{
    Task<PasswordResetToken?> FindAsync(Guid id, CancellationToken ct);
    Task AddAsync(PasswordResetToken token, CancellationToken ct);
    Task UpdateAsync(PasswordResetToken token, CancellationToken ct);

    /// <summary>Deletes every outstanding token of the user (a new password makes earlier links worthless, a new link replaces them).</summary>
    Task RemoveForUserAsync(Guid userId, CancellationToken ct);

    /// <summary>When the user's most recent token was issued, or null when they have none.</summary>
    Task<DateTimeOffset?> LatestIssuedAtAsync(Guid userId, CancellationToken ct);

    /// <summary>Deletes every token issued before <paramref name="before"/>, whoever it belongs to; returns how many.</summary>
    Task<int> DeleteStaleAsync(DateTimeOffset before, CancellationToken ct);
}

/// <summary>What happens to the data a deleted user owns.</summary>
public enum UserDataDisposition
{
    /// <summary>Everything the user owns (including trashed items) and their sharing grants go to another user.</summary>
    Move,

    /// <summary>Their vehicles and everything that belongs to them are deleted for good.</summary>
    Purge,
}

/// <summary>Everything that hangs off a user and has to follow them when they are deleted. Implementations work in one transaction.</summary>
public interface IUserDataRepository
{
    /// <summary>True if the user owns any vehicle or log, trashed or not.</summary>
    Task<bool> OwnsDataAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Deletes the user. With <paramref name="moveDataTo"/> their data and grants are handed to that user first; without it their
    /// vehicles are purged. Returns what was purged so the caller can remove the files.
    /// </summary>
    Task<PurgedUserData> DeleteUserAsync(Guid userId, Guid? moveDataTo, CancellationToken ct);
}

/// <summary>The vehicles a user's deletion purged (their upload folders go too) and those vehicles' pictures (for ones stored before folders existed).</summary>
public sealed record PurgedUserData(IReadOnlyList<Guid> VehicleIds, IReadOnlyList<Guid> PictureIds)
{
    public static PurgedUserData None { get; } = new([], []);
}

/// <summary>Encrypts a refresh token's secret for storage (the cookies' key ring); <see cref="Unprotect"/> is null when it cannot be read back.</summary>
public interface ISecretProtector
{
    string Protect(string secret);
    string? Unprotect(string protectedSecret);
}

public interface IUserSessionRepository
{
    Task<UserSession?> FindAsync(Guid id, CancellationToken ct);
    Task AddAsync(UserSession session, CancellationToken ct);

    /// <summary>
    /// Saves a rotation (<see cref="UserSession.Rotate"/>) only while the session still holds <paramref name="presentedHash"/> and is
    /// not revoked: false when another refresh rotated it first or it ended meanwhile. Only the rotation's columns are written.
    /// </summary>
    Task<bool> RotateAsync(UserSession rotated, string presentedHash, CancellationToken ct);

    /// <summary>Ends the session unless it ended already; false when it had.</summary>
    Task<bool> RevokeAsync(Guid id, DateTimeOffset now, CancellationToken ct);

    /// <summary>Gives the session the user's new session version (nothing else changes).</summary>
    Task AdoptVersionAsync(Guid id, int sessionVersion, CancellationToken ct);

    /// <summary>The user's sessions that are still usable at <paramref name="now"/>, most recently used first.</summary>
    Task<IReadOnlyList<UserSession>> ListUsableForUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct);

    /// <summary>Revokes every session of the user that is not revoked yet, except <paramref name="except"/>; returns how many.</summary>
    Task<int> RevokeForUserAsync(Guid userId, Guid? except, DateTimeOffset now, CancellationToken ct);

    /// <summary>Deletes the sessions that expired or were revoked before <paramref name="before"/>.</summary>
    Task<int> DeleteStaleAsync(DateTimeOffset before, CancellationToken ct);
}
