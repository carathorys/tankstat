using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Notifications;
using Tankstat.Application.Users;
using Tankstat.Domain.Notifications;

namespace Tankstat.Application.Backups;

/// <summary>
/// Tells the administrators that a backup the app was to write failed: every enabled administrator, or with sign-in off the anonymous user
/// (who is everyone then). Derived, one a day (the occurrence is the server's local date of the run), so a schedule failing night after night
/// says so once a day, not once a try; the administrator who pressed Back up now sees it on the page and is left out. The arguments are the
/// error key and when the run started, nothing else.
/// </summary>
public sealed class BackupFailureNotifier(IUserRepository users, Notifier notifier, IOptions<AuthOptions> auth, TimeProvider clock)
{
    public async Task NotifyAsync(BackupRun run, Guid? actorId, CancellationToken ct)
    {
        var recipients = auth.Value.Mode == AuthMode.None
            ? [Principal.Anonymous.Id]
            : (await users.ListAsync(ct)).Where(u => u.IsAdmin && !u.IsDisabled).Select(u => u.Id).ToList();
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(run.StartedAt, clock.LocalTimeZone).DateTime).ToString("yyyy-MM-dd");
        var args = new Dictionary<string, string> { ["error"] = run.Error ?? "backup.failed", ["startedAt"] = run.StartedAt.ToString("O") };
        await notifier.NotifyAsync(actorId, recipients.Select(id => new NotificationDraft(id, NotificationKind.BackupFailed, NotificationRef.Instance, null, args, day)), ct);
    }
}
