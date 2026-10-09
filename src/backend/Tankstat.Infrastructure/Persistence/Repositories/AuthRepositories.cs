using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Access;
using Tankstat.Application.Users;
using Tankstat.Domain.Access;
using Tankstat.Domain.Users;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(IDbContextFactory<AppDbContext> dbFactory) : IUserRepository
{
    public async Task<User?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<IReadOnlyList<User>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
    }

    public async Task<User?> FindLocalByEmailAsync(string normalizedEmail, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Provider == UserProvider.Local && u.Subject == normalizedEmail, ct);
    }

    public async Task<User?> FindExternalAsync(UserProvider provider, string subject, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Provider == provider && u.Subject == subject, ct);
    }

    public async Task<User?> FindByAvatarImageAsync(Guid imageId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.AvatarImageId == imageId, ct);
    }

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().OrderBy(u => u.DisplayName).ToListAsync(ct);
    }

    public async Task<bool> AnyLocalAdminAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AnyAsync(u => u.Provider == UserProvider.Local && u.IsAdmin && !u.IsDisabled, ct);
    }

    public async Task AddAsync(User user, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(User user, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Users.Update(user);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class PasswordResetTokenRepository(IDbContextFactory<AppDbContext> dbFactory) : IPasswordResetTokenRepository
{
    public async Task<PasswordResetToken?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PasswordResetTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task AddAsync(PasswordResetToken token, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.PasswordResetTokens.Add(token);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PasswordResetToken token, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.PasswordResetTokens.Update(token);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveForUserAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.PasswordResetTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
    }

    public async Task<DateTimeOffset?> LatestIssuedAtAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PasswordResetTokens.Where(t => t.UserId == userId).MaxAsync(t => (DateTimeOffset?)t.IssuedAt, ct);
    }

    public async Task<int> DeleteStaleAsync(DateTimeOffset before, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PasswordResetTokens.Where(t => t.IssuedAt < before).ExecuteDeleteAsync(ct);
    }
}

internal sealed class UserSessionRepository(IDbContextFactory<AppDbContext> dbFactory) : IUserSessionRepository
{
    public async Task<UserSession?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.UserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task AddAsync(UserSession session, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(ct);
    }

    // Targeted, conditional writes rather than saving the whole row: two refreshes, a refresh and a sign-out, or a refresh and a password
    // change can run at the same moment, and a row saved whole would put back what the other one changed.
    public async Task<bool> RotateAsync(UserSession rotated, string presentedHash, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.UserSessions.Where(s => s.Id == rotated.Id && s.SecretHash == presentedHash && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.PreviousSecretHash, rotated.PreviousSecretHash)
                .SetProperty(s => s.SecretHash, rotated.SecretHash)
                .SetProperty(s => s.ProtectedSecret, rotated.ProtectedSecret)
                .SetProperty(s => s.RotatedAt, rotated.RotatedAt)
                .SetProperty(s => s.LastUsedAt, rotated.LastUsedAt)
                .SetProperty(s => s.ExpiresAt, rotated.ExpiresAt), ct) == 1;
    }

    public async Task<bool> RevokeAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.UserSessions.Where(s => s.Id == id && s.RevokedAt == null).ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), ct) == 1;
    }

    public async Task AdoptVersionAsync(Guid id, int sessionVersion, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.UserSessions.Where(s => s.Id == id).ExecuteUpdateAsync(u => u.SetProperty(s => s.SessionVersion, sessionVersion), ct);
    }

    public async Task<IReadOnlyList<UserSession>> ListUsableForUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.UserSessions.AsNoTracking().Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastUsedAt).ThenBy(s => s.Id).ToListAsync(ct);
    }

    public async Task<int> RevokeForUserAsync(Guid userId, Guid? except, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != except)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), ct);
    }

    public async Task<int> DeleteStaleAsync(DateTimeOffset before, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.UserSessions.Where(s => s.ExpiresAt < before || s.RevokedAt < before).ExecuteDeleteAsync(ct);
    }
}

internal sealed class AccessGrantRepository(IDbContextFactory<AppDbContext> dbFactory) : IAccessGrantRepository
{
    public async Task<IReadOnlyList<AccessGrant>> ListAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AccessGrants.AsNoTracking().ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AccessGrant>> ListForGranteeAsync(Guid granteeId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AccessGrants.AsNoTracking().Where(g => g.GranteeId == granteeId).ToListAsync(ct);
    }

    public async Task<AccessGrant?> FindAsync(Guid ownerId, Guid granteeId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AccessGrants.AsNoTracking().FirstOrDefaultAsync(g => g.OwnerId == ownerId && g.GranteeId == granteeId, ct);
    }

    public async Task AddAsync(AccessGrant grant, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.AccessGrants.Add(grant);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(AccessGrant grant, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.AccessGrants.Update(grant);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(AccessGrant grant, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.AccessGrants.Remove(grant);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class AccessSettingsRepository(IDbContextFactory<AppDbContext> dbFactory) : IAccessSettingsRepository
{
    public async Task<AccessSettings> GetAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AccessSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == AccessSettings.SingletonId, ct)
               ?? AccessSettings.Default();
    }

    public async Task SaveAsync(AccessSettings settings, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.AccessSettings.AnyAsync(s => s.Id == settings.Id, ct)) db.AccessSettings.Update(settings);
        else db.AccessSettings.Add(settings);
        await db.SaveChangesAsync(ct);
    }
}
