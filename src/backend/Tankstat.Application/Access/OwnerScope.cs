namespace Tankstat.Application.Access;

/// <summary>
/// Which data a query may return: everything, or what belongs to the listed owners plus the individual resources
/// (e.g. vehicles) the user has been given a grant on.
/// </summary>
public sealed class OwnerScope
{
    private readonly HashSet<Guid> _owners;
    private readonly HashSet<Guid> _resources;

    private OwnerScope(bool all, HashSet<Guid> owners, HashSet<Guid> resources)
    {
        IsAll = all;
        _owners = owners;
        _resources = resources;
    }

    public static OwnerScope All { get; } = new(true, [], []);
    public static OwnerScope Of(IEnumerable<Guid> owners) => new(false, [.. owners], []);
    public static OwnerScope Of(IEnumerable<Guid> owners, IEnumerable<Guid> resources) => new(false, [.. owners], [.. resources]);

    public bool IsAll { get; }
    public IReadOnlyCollection<Guid> Owners => _owners;

    /// <summary>Individually granted resources (for logs: the vehicle ids), in addition to <see cref="Owners"/>.</summary>
    public IReadOnlyCollection<Guid> Resources => _resources;

    public bool Contains(Guid ownerId) => IsAll || _owners.Contains(ownerId);
    public bool Contains(Guid ownerId, Guid resourceId) => IsAll || _owners.Contains(ownerId) || _resources.Contains(resourceId);
}
