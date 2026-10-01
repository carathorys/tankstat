namespace Tankstat.Application.Auth;

public sealed record Principal(Guid Id, string DisplayName, string Email, bool IsAdmin)
{
    /// <summary>The acting identity when authentication is turned off: sees and may change everything.</summary>
    public static Principal Anonymous { get; } = new(Guid.Empty, "Anonymous", "", true);

    public bool IsAnonymous => Id == Guid.Empty;
}
