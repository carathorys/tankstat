using Tankstat.Domain.Users;

namespace Tankstat.Domain.UnitTests;

public class UserSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    [Fact]
    public void ASession_IsUsableUntilItExpiresOrEnds()
    {
        var session = UserSession.Issue(Guid.NewGuid(), 3, "HASH", "SEALED", "Firefox on Linux", Now, Lifetime);

        Assert.Equal((3, "HASH", "Firefox on Linux", Now, Now + Lifetime), (session.SessionVersion, session.SecretHash, session.Client, session.LastUsedAt, session.ExpiresAt));
        Assert.True(session.IsUsable(Now.AddDays(89)));
        Assert.False(session.IsUsable(Now.AddDays(90)));

        session.Revoke(Now.AddDays(1));
        session.Revoke(Now.AddDays(2)); // ending it again keeps when it ended
        Assert.Equal(Now.AddDays(1), session.RevokedAt);
        Assert.False(session.IsUsable(Now.AddDays(1)));
    }

    [Fact]
    public void Rotating_KeepsThePreviousSecret_AndGivesAFullLifetimeFromNow()
    {
        var session = UserSession.Issue(Guid.NewGuid(), 1, "OLD", "SEALED-OLD", null, Now, Lifetime);
        var later = Now.AddDays(30);

        session.Rotate("NEW", "SEALED-NEW", later, Lifetime);

        Assert.Equal(("NEW", "SEALED-NEW", "OLD", later, later, later + Lifetime), (session.SecretHash, session.ProtectedSecret, session.PreviousSecretHash, session.RotatedAt, session.LastUsedAt, session.ExpiresAt));
    }

    [Fact]
    public void TheClientLabel_IsCutToItsLimit() =>
        Assert.Equal(UserSession.MaxClientLength, UserSession.Issue(Guid.NewGuid(), 1, "H", "S", new string('x', 500), Now, Lifetime).Client!.Length);
}
