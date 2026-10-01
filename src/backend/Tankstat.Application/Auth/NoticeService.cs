using Microsoft.Extensions.Options;

namespace Tankstat.Application.Auth;

public enum NoticeSeverity
{
    Info,
    Warning,
}

/// <summary>A message the UI should show to everyone. Kept generic so other kinds of notices can be added later.</summary>
public sealed record Notice(string Code, NoticeSeverity Severity, string Message);

public sealed class NoticeService(IOptions<AuthOptions> auth)
{
    public IReadOnlyList<Notice> GetNotices()
    {
        var notices = new List<Notice>();
        if (auth.Value.Mode == AuthMode.None)
            notices.Add(new Notice(
                "AUTH_DISABLED", NoticeSeverity.Warning,
                "Authentication is disabled: anyone who can reach this app can see and change all data. This is unsafe; configure Auth:Mode."));
        return notices;
    }
}
