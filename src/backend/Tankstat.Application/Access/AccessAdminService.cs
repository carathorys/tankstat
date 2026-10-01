using Tankstat.Application.Users;
using Tankstat.Domain.Access;

namespace Tankstat.Application.Access;

/// <summary>Administrator-only management of the instance-wide default and per-user grants.</summary>
public sealed class AccessAdminService(
    AccessService access, IAccessSettingsRepository settings, IAccessGrantRepository grants, IUserRepository users)
{
    public async Task<AccessSettings> GetSettingsAsync(CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        return await settings.GetAsync(ct);
    }

    public async Task<AccessSettings> SetDefaultLevelAsync(AccessLevel level, CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        var current = await settings.GetAsync(ct);
        current.SetDefaultLevelForOthers(level);
        await settings.SaveAsync(current, ct);
        return current;
    }

    public async Task<IReadOnlyList<AccessGrant>> ListGrantsAsync(CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        return await grants.ListAsync(ct);
    }

    /// <summary>Gives <paramref name="granteeId"/> the level on <paramref name="ownerId"/>'s data; <see cref="AccessLevel.None"/> removes the grant.</summary>
    public async Task SetGrantAsync(Guid ownerId, Guid granteeId, AccessLevel level, CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        if (await users.FindByIdAsync(ownerId, ct) is null) throw new NotFoundException($"User {ownerId} does not exist.");
        if (await users.FindByIdAsync(granteeId, ct) is null) throw new NotFoundException($"User {granteeId} does not exist.");

        var existing = await grants.FindAsync(ownerId, granteeId, ct);
        if (level == AccessLevel.None)
        {
            if (existing is not null) await grants.RemoveAsync(existing, ct);
        }
        else if (existing is null)
        {
            await grants.AddAsync(AccessGrant.Create(ownerId, granteeId, level), ct);
        }
        else
        {
            existing.ChangeLevel(level);
            await grants.UpdateAsync(existing, ct);
        }
    }
}
