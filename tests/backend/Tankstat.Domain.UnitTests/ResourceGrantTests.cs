using Tankstat.Domain;
using Tankstat.Domain.Access;

namespace Tankstat.Domain.UnitTests;

public class ResourceGrantTests
{
    private static ResourceGrant Grant(AccessLevel level) =>
        ResourceGrant.Create(ResourceType.Vehicle, Guid.NewGuid(), Guid.NewGuid(), GrantedFeature.Logs, level);

    [Theory]
    [InlineData(AccessLevel.Edit)]
    [InlineData(AccessLevel.Delete)]
    public void Create_AcceptsEditAndDelete(AccessLevel level) => Assert.Equal(level, Grant(level).Level);

    [Theory]
    [InlineData(AccessLevel.None)]
    [InlineData(AccessLevel.View)]
    [InlineData((AccessLevel)42)]
    public void Create_RejectsEverythingElse(AccessLevel level) =>
        Assert.Equal("share.levelRequired", Assert.Throws<DomainException>(() => Grant(level)).Key);

    [Fact]
    public void ChangeLevel_FollowsTheSameRule()
    {
        var grant = Grant(AccessLevel.Edit);

        grant.ChangeLevel(AccessLevel.Delete);

        Assert.Equal(AccessLevel.Delete, grant.Level);
        Assert.Throws<DomainException>(() => grant.ChangeLevel(AccessLevel.None));
    }

    [Fact]
    public void LevelsIncludeTheOnesBelow_DeleteIsAboveEdit() =>
        Assert.True(AccessLevel.None < AccessLevel.View && AccessLevel.View < AccessLevel.Edit && AccessLevel.Edit < AccessLevel.Delete);

    [Fact]
    public void Policy_GrantsCanReachDelete_ButOnlyOwnersAndAdminsAlways()
    {
        var owner = Guid.NewGuid();
        var user = Guid.NewGuid();
        var delete = AccessGrant.Create(owner, user, AccessLevel.Delete);

        Assert.Equal(AccessLevel.Delete, AccessPolicy.Resolve(user, false, owner, AccessLevel.None, [delete]));
        Assert.Equal(AccessLevel.Edit, AccessPolicy.Resolve(user, false, owner, AccessLevel.None, [AccessGrant.Create(owner, user, AccessLevel.Edit)]));
    }
}
