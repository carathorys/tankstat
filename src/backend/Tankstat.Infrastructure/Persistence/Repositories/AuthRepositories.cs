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
