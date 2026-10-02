using Tankstat.Domain.Users;

namespace Tankstat.Application.Users;

public interface IUserRepository
{
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct);
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

    /// <summary>Deletes every outstanding token of the user (a new password makes earlier links worthless).</summary>
    Task RemoveForUserAsync(Guid userId, CancellationToken ct);
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
    /// vehicles are purged. Returns the pictures of purged vehicles so the caller can remove the files.
    /// </summary>
    Task<IReadOnlyList<Guid>> DeleteUserAsync(Guid userId, Guid? moveDataTo, CancellationToken ct);
}
