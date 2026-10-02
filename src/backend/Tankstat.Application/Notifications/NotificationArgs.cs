using Tankstat.Domain.Access;
using Tankstat.Domain.Notifications;

namespace Tankstat.Application.Notifications;

/// <summary>Builds the display-ready arguments of a notification (the frontend fills its texts with them).</summary>
internal static class NotificationArgs
{
    public static IReadOnlyDictionary<string, string> Of(params (string Name, string? Value)[] args) => args
        .Where(a => a.Value is not null)
        .ToDictionary(a => a.Name, a => a.Value!.Length > Notification.MaxArgValueLength ? a.Value[..Notification.MaxArgValueLength] : a.Value!);

    /// <summary>An access level the way the API spells it (e.g. <c>EDIT</c>), so clients can reuse their translations.</summary>
    public static string Level(AccessLevel level) => level.ToString().ToUpperInvariant();
}
