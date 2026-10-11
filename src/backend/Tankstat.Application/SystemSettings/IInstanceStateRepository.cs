using Tankstat.Domain.Instance;

namespace Tankstat.Application.SystemSettings;

public interface IInstanceStateRepository
{
    /// <summary>The one row, or null before the first start that wrote it.</summary>
    Task<InstanceState?> GetAsync(CancellationToken ct);

    /// <summary>Adds the row or replaces it; two starts at once both end with a row.</summary>
    Task SaveAsync(InstanceState state, CancellationToken ct);
}
