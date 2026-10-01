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

/// <summary>Grants on individual resources (one vehicle's logs, ...), separate from the owner-wide grants.</summary>
public interface IResourceGrantRepository
{
    Task<IReadOnlyList<ResourceGrant>> ListForResourceAsync(ResourceType type, Guid resourceId, CancellationToken ct);
    Task<IReadOnlyList<ResourceGrant>> ListForGranteeAsync(Guid granteeId, ResourceType type, GrantedFeature feature, CancellationToken ct);
    Task<ResourceGrant?> FindAsync(ResourceType type, Guid resourceId, Guid granteeId, GrantedFeature feature, CancellationToken ct);
    Task AddAsync(ResourceGrant grant, CancellationToken ct);
    Task UpdateAsync(ResourceGrant grant, CancellationToken ct);
    Task RemoveAsync(ResourceGrant grant, CancellationToken ct);
}
