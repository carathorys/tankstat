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
}
