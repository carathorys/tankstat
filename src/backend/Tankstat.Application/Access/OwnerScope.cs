namespace Tankstat.Application.Access;

/// <summary>Which owners' data a query may return: everything, or only the listed owners.</summary>
public sealed class OwnerScope
{
    private readonly HashSet<Guid> _owners;

    private OwnerScope(bool all, HashSet<Guid> owners) { IsAll = all; _owners = owners; }

    public static OwnerScope All { get; } = new(true, []);
    public static OwnerScope Of(IEnumerable<Guid> owners) => new(false, [.. owners]);

    public bool IsAll { get; }
    public IReadOnlyCollection<Guid> Owners => _owners;
    public bool Contains(Guid ownerId) => IsAll || _owners.Contains(ownerId);
}
