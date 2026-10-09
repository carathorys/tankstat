using Tankstat.Domain;
using Tankstat.Domain.Access;

namespace Tankstat.Domain.UnitTests;

public class AccessPolicyTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carol = Guid.NewGuid();

    private static AccessLevel Resolve(Guid user, Guid owner, AccessLevel defaults = AccessLevel.None, bool admin = false, params AccessGrant[] grants) =>
        AccessPolicy.Resolve(user, admin, owner, defaults, grants);

    [Fact]
    public void Owner_HasFullAccess_IncludingPermanentDeletion() => Assert.Equal(AccessLevel.Delete, Resolve(Alice, Alice));

    [Fact]
    public void Admin_HasFullAccessToEverything_IncludingPermanentDeletion() => Assert.Equal(AccessLevel.Delete, Resolve(Bob, Alice, admin: true));

    [Fact]
    public void Others_GetNothingByDefault() => Assert.Equal(AccessLevel.None, Resolve(Bob, Alice));

    [Fact]
    public void Others_GetTheInstanceDefault() => Assert.Equal(AccessLevel.View, Resolve(Bob, Alice, AccessLevel.View));

    [Fact]
    public void Grant_RaisesAboveDefault() =>
        Assert.Equal(AccessLevel.Edit, Resolve(Bob, Alice, AccessLevel.View, false, AccessGrant.Create(Alice, Bob, AccessLevel.Edit)));

    [Fact]
    public void Grant_DoesNotLowerTheDefault() =>
        Assert.Equal(AccessLevel.Edit, Resolve(Bob, Alice, AccessLevel.Edit, false, AccessGrant.Create(Alice, Bob, AccessLevel.View)));

    [Fact]
    public void Grant_OnlyAppliesToItsOwnerAndGrantee()
    {
        var grant = AccessGrant.Create(Alice, Bob, AccessLevel.Edit);

        Assert.Equal(AccessLevel.None, Resolve(Carol, Alice, grants: grant)); // other grantee
        Assert.Equal(AccessLevel.None, Resolve(Bob, Carol, grants: grant));   // other owner
    }
}

public class AccessGrantTests
{
    [Fact]
    public void Create_RejectsSelfGrant()
    {
        var id = Guid.NewGuid();
        Assert.Throws<DomainException>(() => AccessGrant.Create(id, id, AccessLevel.View));
    }

    [Fact]
    public void Create_RejectsNoneLevel() =>
        Assert.Throws<DomainException>(() => AccessGrant.Create(Guid.NewGuid(), Guid.NewGuid(), AccessLevel.None));

    [Fact]
    public void Create_RejectsALevelThatDoesNotExist()
    {
        var e = Assert.Throws<DomainException>(() => AccessGrant.Create(Guid.NewGuid(), Guid.NewGuid(), (AccessLevel)42));

        Assert.Equal(("access.unknownLevel", (object?)"42"), (e.Key, e.Args["level"]));
    }

    [Fact]
    public void ChangeLevel_TakesAnyRealLevel_ButNeverNone()
    {
        var grant = AccessGrant.Create(Guid.NewGuid(), Guid.NewGuid(), AccessLevel.View);

        grant.ChangeLevel(AccessLevel.Delete);
        Assert.Equal(AccessLevel.Delete, grant.Level);

        Assert.Equal("access.levelRequired", Assert.Throws<DomainException>(() => grant.ChangeLevel(AccessLevel.None)).Key);
        Assert.Equal("access.levelRequired", Assert.Throws<DomainException>(() => grant.ChangeLevel((AccessLevel)42)).Key);
        Assert.Equal(AccessLevel.Delete, grant.Level);
    }

    [Fact]
    public void Settings_DefaultToNoAccessForOthers()
    {
        var settings = AccessSettings.Default();
        Assert.Equal(AccessLevel.None, settings.DefaultLevelForOthers);

        settings.SetDefaultLevelForOthers(AccessLevel.View);
        Assert.Equal(AccessLevel.View, settings.DefaultLevelForOthers);
        Assert.Throws<DomainException>(() => settings.SetDefaultLevelForOthers((AccessLevel)42));
    }
}
