using Tankstat.Application.Sync;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Settings;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class OfflineWindowTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("all")]
    [InlineData("thisYear")]
    [InlineData("thisAndLastYear")]
    [InlineData("from:2024-02-29")]
    [InlineData("span:P2M")]
    [InlineData("span:P2Y6M4DT2H48M12S")]
    [InlineData("span:PT12S")]
    [InlineData("span:P100Y")]
    [InlineData("span:P1200M")]
    public void TheRulesADeviceUnderstands_AreAccepted(string rule) => Assert.Equal(rule, OfflineWindow.Require($" {rule} "));

    [Theory]
    [InlineData("")]
    [InlineData("everything")]
    [InlineData("from:2024-02-30")]
    [InlineData("from:1899-12-31")]
    [InlineData("span:")]
    [InlineData("span:P")]
    [InlineData("span:PT")]
    [InlineData("span:P0D")]
    [InlineData("span:P1W")] // weeks are not part of the grammar: 7 days
    [InlineData("span:P1.5Y")]
    [InlineData("span:P-1D")]
    [InlineData("span:P101Y")]
    [InlineData("span:P99Y13M")]
    [InlineData("span:2M")]
    [InlineData(null)]
    [InlineData("from:2024-02-29xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")] // longer than any rule
    public void AnythingElse_IsRefused(string? rule)
    {
        var e = Assert.Throws<DomainException>(() => OfflineWindow.Require(rule));
        Assert.Equal("settings.offlineWindowInvalid", e.Key);
    }

    [Fact]
    public void WithoutAChoice_ItIsTheLastTwoMonths() => Assert.Equal("span:P2M", OfflineSettings.Create(Guid.NewGuid(), OfflineWindow.Default, DateTimeOffset.UtcNow).DefaultWindow);

    [Fact]
    public void ANewDefault_IsCheckedLikeTheFirst_AndARefusedOneChangesNothing()
    {
        var created = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var settings = OfflineSettings.Create(Guid.NewGuid(), OfflineWindow.Default, created);

        settings.SetDefault(" thisYear ", created.AddDays(1));
        Assert.Equal(("thisYear", created.AddDays(1)), (settings.DefaultWindow, settings.UpdatedAt));

        Assert.Equal("settings.offlineWindowInvalid", Assert.Throws<DomainException>(() => settings.SetDefault("forever", created.AddDays(2))).Key);
        Assert.Equal(("thisYear", created.AddDays(1)), (settings.DefaultWindow, settings.UpdatedAt));
    }
}

public class SyncOptionsTests
{
    [Theory]
    [InlineData(1, 1, null)]
    [InlineData(3650, 3650, null)]
    [InlineData(0, 30, "Sync:TombstoneRetentionDays")]
    [InlineData(3651, 30, "Sync:TombstoneRetentionDays")]
    [InlineData(30, 0, "Sync:RetentionDays")]
    [InlineData(30, 3651, "Sync:RetentionDays")]
    public void TheRetentions_StayWithinTenYears(int tombstoneDays, int retentionDays, string? named)
    {
        var result = new SyncOptionsValidator().Validate(null, new SyncOptions { TombstoneRetentionDays = tombstoneDays, RetentionDays = retentionDays });

        if (named is null) Assert.True(result.Succeeded);
        else Assert.StartsWith(named + " ", Assert.Single(result.Failures!));
    }
}

public class OfflineCursorTests
{
    private static readonly Guid Vehicle = Guid.NewGuid();

    [Fact]
    public void ACursor_ComesBackAsItWasHandedOut()
    {
        var cursor = new OfflineCursor(Vehicle, new DateOnly(2026, 3, 1), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow,
            new OfflineKey(DateTimeOffset.UtcNow.AddMinutes(-5), Guid.NewGuid()), false, null, true);

        var encoded = cursor.Encode();

        Assert.Matches("^[A-Za-z0-9_-]+$", encoded); // safe in a URL or a JSON string as it is
        Assert.Equal(cursor, OfflineCursor.Decode(encoded, Vehicle));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    [InlineData("MnwxMjN8LXwtfDB8LXwt")] // "2|123|-|-|0|-|-": another version
    public void ACursorThisServerDidNotMake_IsRefused(string encoded)
    {
        var e = Assert.Throws<DomainException>(() => OfflineCursor.Decode(encoded, Vehicle));
        Assert.Equal("sync.cursorInvalid", e.Key);
    }

    [Fact]
    public void AFirstPageCursor_WithoutAWindowAndWithNothingSentYet_ComesBackAsItWas()
    {
        var cursor = new OfflineCursor(Vehicle, null, null, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), null, false, null, false);

        Assert.Equal(cursor, OfflineCursor.Decode(cursor.Encode(), Vehicle));
    }

    /// <summary>A cursor as a device could forge one: the right shape, the right vehicle, something wrong inside.</summary>
    private static string Forged(string watermark, string refuelings = "-", string from = "-") =>
        Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"1|{Vehicle:N}|{from}|-|{watermark}|{refuelings}|-")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Theory]
    [InlineData("not-a-number", "-", "-")]
    [InlineData("99999999999999999999999", "-", "-")] // more than a number of ticks can be
    [InlineData("9223372036854775807", "-", "-")] // a number, but no moment
    [InlineData("0", "5", "-")] // a row key without its id
    [InlineData("0", "0:not-a-guid", "-")]
    [InlineData("0", "-", "2026-02-30")]
    public void AForgedCursor_IsRefusedLikeAnyOther(string watermark, string refuelings, string from)
    {
        var e = Assert.Throws<DomainException>(() => OfflineCursor.Decode(Forged(watermark, refuelings, from), Vehicle));

        Assert.Equal("sync.cursorInvalid", e.Key);
    }

    [Fact]
    public void ACursorOfAnotherVehicle_IsRefused()
    {
        var encoded = new OfflineCursor(Guid.NewGuid(), null, null, DateTimeOffset.UtcNow, null, false, null, false).Encode();

        Assert.Equal("sync.cursorInvalid", Assert.Throws<DomainException>(() => OfflineCursor.Decode(encoded, Vehicle)).Key);
    }
}

public class OfflineSettingsServiceTests
{
    [Fact]
    public async Task TheWindows_AreReplacedAsAWhole_ForTheUserOnly_AndNameOnlyVehiclesTheyMaySee()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(bob);
        var bobs = await w.VehicleService.AddAsync("Bobs", null, FuelType.Petrol, default);
        w.Current.SignInAs(alice);
        var golf = await w.VehicleService.AddAsync("Golf", null, FuelType.Petrol, default);
        var polo = await w.VehicleService.AddAsync("Polo", null, FuelType.Petrol, default);

        Assert.Equal(OfflineWindow.Default, (await w.OfflineSettings.GetAsync(default)).DefaultWindow);
        await w.OfflineSettings.SetAsync("all", [(golf.Id, "none"), (polo.Id, "thisYear"), (golf.Id, "all")], default);
        await w.OfflineSettings.SetAsync("thisYear", [(polo.Id, "span:P1Y")], default);

        var mine = await w.OfflineSettings.GetAsync(default);
        Assert.Equal("thisYear", mine.DefaultWindow);
        Assert.Equal([(polo.Id, "span:P1Y")], mine.Vehicles.Select(v => (v.VehicleId, v.Window)));

        var notHers = await Assert.ThrowsAsync<NotFoundException>(() => w.OfflineSettings.SetAsync("all", [(bobs.Id, "all")], default));
        Assert.Equal("vehicle.notFound", notHers.Key);
        Assert.Equal("thisYear", (await w.OfflineSettings.GetAsync(default)).DefaultWindow); // nothing changed

        w.Current.SignInAs(bob);
        Assert.Equal(OfflineWindow.Default, (await w.OfflineSettings.GetAsync(default)).DefaultWindow);
    }

    [Fact]
    public async Task AVehicleNoLongerSeen_IsNotListed_SoSavingTheListAgainWorks_AndDropsIt()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        var shared = await w.VehicleService.AddAsync("Shared", null, FuelType.Petrol, default);
        var trashed = await w.VehicleService.AddAsync("Trashed", null, FuelType.Petrol, default);
        await w.Sharing.SetLogAccessAsync(shared.Id, bob.Id, AccessLevel.Edit, default);
        await w.Sharing.SetLogAccessAsync(trashed.Id, bob.Id, AccessLevel.Edit, default);
        w.Current.SignInAs(bob);
        await w.OfflineSettings.SetAsync("all", [(shared.Id, "none"), (trashed.Id, "thisYear")], default);

        w.Current.SignInAs(alice);
        await w.Sharing.SetLogAccessAsync(shared.Id, bob.Id, AccessLevel.None, default); // unshared
        await w.VehicleService.DeleteAsync(trashed.Id, default); // in the trash
        w.Current.SignInAs(bob);

        var listed = await w.OfflineSettings.GetAsync(default);
        Assert.Empty(listed.Vehicles);
        await w.OfflineSettings.SetAsync("thisYear", [.. listed.Vehicles.Select(v => (v.VehicleId, v.Window))], default);
        Assert.DoesNotContain(w.OfflineSettingsStore.Vehicles, v => v.UserId == bob.Id);
    }

    [Fact]
    public async Task AWindowThatIsNotValid_SavesNothing()
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("alice@x.co"));
        var golf = await w.VehicleService.AddAsync("Golf", null, FuelType.Petrol, default);

        var e = await Assert.ThrowsAsync<DomainException>(() => w.OfflineSettings.SetAsync("all", [(golf.Id, "span:P0D")], default));

        Assert.Equal("settings.offlineWindowInvalid", e.Key);
        Assert.Empty(w.OfflineSettingsStore.Rows);
    }
}
