using Tankstat.Domain.Access;

namespace Tankstat.Application.Access;

public interface IAccessGrantRepository
{
    Task<IReadOnlyList<AccessGrant>> ListAsync(CancellationToken ct);
    Task<IReadOnlyList<AccessGrant>> ListForGranteeAsync(Guid granteeId, CancellationToken ct);
    Task<AccessGrant?> FindAsync(Guid ownerId, Guid granteeId, CancellationToken ct);
    Task AddAsync(AccessGrant grant, CancellationToken ct);
    Task UpdateAsync(AccessGrant grant, CancellationToken ct);
    Task RemoveAsync(AccessGrant grant, CancellationToken ct);
}

public interface IAccessSettingsRepository
{
    Task<AccessSettings> GetAsync(CancellationToken ct);
    Task SaveAsync(AccessSettings settings, CancellationToken ct);
}
