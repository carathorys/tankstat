using Tankstat.Application.Auth;
using Tankstat.Application.Runs;

namespace Tankstat.Application.UnitTests;

/// <summary>
/// The restore context's guard (#340): the ways into the services that choose ids, owners and creators are a partial restore's only, run
/// by a real administrator. Every such way in calls <see cref="RunRestore.Require"/> first.
/// </summary>
public sealed class RunRestoreTests
{
    private static readonly Principal Admin = new(Guid.NewGuid(), "Ada", "ada@example.com", IsAdmin: true);
    private static readonly Principal User = new(Guid.NewGuid(), "Anna", "anna@example.com", IsAdmin: false);

    [Fact]
    public void OutsideARun_EvenAnAdministratorIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => RunRestore.Require(Admin));
    }

    [Fact]
    public void InAnImportsRun_NotARestore_EvenAnAdministratorIsRefused()
    {
        using var _ = RunContext.Enter(new RunContext(Admin, [], restoring: false));
        Assert.Throws<InvalidOperationException>(() => RunRestore.Require(Admin));
    }

    [Fact]
    public void InARestoresRun_OnlyARealAdministratorPasses()
    {
        using var _ = RunContext.Enter(new RunContext(Admin, [], restoring: true));
        RunRestore.Require(Admin);
        Assert.Throws<InvalidOperationException>(() => RunRestore.Require(User));
        Assert.Throws<InvalidOperationException>(() => RunRestore.Require(Principal.Anonymous)); // mode None: no administrator features
    }
}
