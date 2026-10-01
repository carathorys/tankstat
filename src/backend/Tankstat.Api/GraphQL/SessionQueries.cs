using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;

namespace Tankstat.Api.GraphQL;

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class SessionQueries
{
    /// <summary>Public: lets the UI decide between login screen and app.</summary>
    public async Task<Session> GetSession([Service] SessionService sessions, CancellationToken ct)
    {
        var s = await sessions.GetAsync(ct);
        return new Session(s.Mode, s.User is { IsAnonymous: false } u ? UserInfo.From(u) : null);
    }

    /// <summary>Public messages the UI must show (e.g. the "authentication disabled" warning).</summary>
    public IReadOnlyList<Notice> GetNotices([Service] NoticeService notices) => notices.GetNotices();

    public async Task<IReadOnlyList<UserAccount>> GetUsers([Service] UserService users, CancellationToken ct) =>
        (await users.ListAsync(ct)).Select(UserAccount.From).ToList();

    public async Task<AccessSettingsInfo> GetAccessSettings([Service] AccessAdminService admin, CancellationToken ct) =>
        new((await admin.GetSettingsAsync(ct)).DefaultLevelForOthers);

    public async Task<IReadOnlyList<AccessGrantInfo>> GetAccessGrants([Service] AccessAdminService admin, CancellationToken ct) =>
        (await admin.ListGrantsAsync(ct)).Select(g => new AccessGrantInfo(g.Id, g.OwnerId, g.GranteeId, g.Level)).ToList();
}
