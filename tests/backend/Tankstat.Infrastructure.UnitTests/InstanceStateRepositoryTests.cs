using Tankstat.Application.SystemSettings;
using Tankstat.Domain.Instance;

namespace Tankstat.Infrastructure.UnitTests;

public class InstanceStateRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheRow_IsAbsentAtFirst_ThenWrittenAndReplaced()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IInstanceStateRepository>();
        Assert.Null(await repo.GetAsync(default));

        var state = InstanceState.New();
        state.RecordStart("None", Now);
        await repo.SaveAsync(state, default);
        var first = (await repo.GetAsync(default))!;
        Assert.Equal(("None", Now), (first.LastStartMode, first.LastStartedAt));

        first.RecordStart("Standalone", Now.AddDays(1));
        await repo.SaveAsync(first, default);
        var again = InstanceState.New(); // a second instance that saw no row (a race) saves too: an update, not an error
        again.RecordStart("Standalone", Now.AddDays(2));
        await repo.SaveAsync(again, default);

        var loaded = (await repo.GetAsync(default))!;
        Assert.Equal(("Standalone", Now.AddDays(2), InstanceState.SingletonId), (loaded.LastStartMode, loaded.LastStartedAt, loaded.Id));
    }
}
