using Microsoft.Extensions.Options;
using Tankstat.Application.SystemSettings;

namespace Tankstat.Application.Auth;

public enum NoticeSeverity
{
    Info,
    Warning,
}

/// <summary>A message the UI should show to everyone. Kept generic so other kinds of notices can be added later.</summary>
public sealed record Notice(string Code, NoticeSeverity Severity, string Message);

/// <param name="settingsFile">The app's own settings file as this process started; null where the host did not decide about one (tests, tools).</param>
public sealed class NoticeService(IOptions<AuthOptions> auth, SettingsFileStatus? settingsFile = null)
{
    public IReadOnlyList<Notice> GetNotices()
    {
        var notices = new List<Notice>();
        if (auth.Value.Mode == AuthMode.None)
            notices.Add(new Notice(
                "AUTH_DISABLED", NoticeSeverity.Warning,
                "Authentication is disabled: anyone who can reach this app can see and change all data. This is unsafe; configure Auth:Mode."));
        // Notices are public (the sign-in screen shows them, devices keep them), so this one names no setting: the reasons are in the log.
        if (settingsFile is { Ignored: true })
            notices.Add(new Notice(
                "SETTINGS_FILE_IGNORED", NoticeSeverity.Warning,
                "The settings file could not be used and was left out; the app runs without it until the next start. See the server log for the reason."));
        return notices;
    }
}
