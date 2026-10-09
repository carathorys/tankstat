using Tankstat.Application.Auth;
using Tankstat.Application.Settings;
using Tankstat.Domain;
using Tankstat.Domain.Settings;

namespace Tankstat.Application.UnitTests;

public class UiSettingsServiceTests
{
    private static GridSettingsValues Grid(int pageSize = 25, params string[] hidden) => new(["name", "licensePlate"], hidden, pageSize, "name", SortDirection.Asc);

    [Fact]
    public async Task Settings_StartEmpty_AndChangesArePartial()
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("alice@x.co"));

        var empty = await w.UiSettings.GetAsync(default);
        Assert.Equal((null, null), (empty.NavOpen, empty.Language));
        Assert.Empty(await w.UiSettings.ListGridsAsync(default));

        await w.UiSettings.UpdateAsync(new UiSettingsChange(NavOpen: false), default);
        var withLanguage = await w.UiSettings.UpdateAsync(new UiSettingsChange(Language: "hu"), default);
        Assert.Equal((false, "hu"), (withLanguage.NavOpen, withLanguage.Language)); // the nav choice stayed

        var cleared = await w.UiSettings.UpdateAsync(new UiSettingsChange(ClearLanguage: true), default);
        Assert.Equal((false, null), (cleared.NavOpen, cleared.Language));
        Assert.Single(w.UiSettingsStore.Items);
    }

    [Fact]
    public async Task ColorMode_IsOneMoreIndependentSetting()
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("alice@x.co"));
        Assert.Null((await w.UiSettings.GetAsync(default)).ColorMode);

        await w.UiSettings.UpdateAsync(new UiSettingsChange(NavOpen: false, Language: "hu"), default);
        var light = await w.UiSettings.UpdateAsync(new UiSettingsChange(ColorMode: ColorMode.Light), default);
        Assert.Equal((false, "hu", ColorMode.Light), (light.NavOpen, light.Language, light.ColorMode)); // the others stayed

        var other = await w.UiSettings.UpdateAsync(new UiSettingsChange(NavOpen: true), default);
        Assert.Equal(ColorMode.Light, other.ColorMode); // and it stays when they change
        Assert.Equal(ColorMode.Light, (await w.UiSettings.GetAsync(default)).ColorMode);
        Assert.False(new UiSettingsChange(ColorMode: ColorMode.System).IsEmpty);
    }

    [Fact]
    public async Task Surface_IsOneMoreIndependentSetting()
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("alice@x.co"));
        Assert.Null((await w.UiSettings.GetAsync(default)).Surface);

        await w.UiSettings.UpdateAsync(new UiSettingsChange(ColorMode: ColorMode.Light), default);
        var opaque = await w.UiSettings.UpdateAsync(new UiSettingsChange(Surface: SurfaceStyle.Opaque), default);
        Assert.Equal((ColorMode.Light, SurfaceStyle.Opaque), (opaque.ColorMode, opaque.Surface)); // the colour mode stayed

        var other = await w.UiSettings.UpdateAsync(new UiSettingsChange(ColorMode: ColorMode.Dark), default);
        Assert.Equal(SurfaceStyle.Opaque, other.Surface); // and it stays when the others change
        Assert.Equal(SurfaceStyle.Opaque, (await w.UiSettings.GetAsync(default)).Surface);
        Assert.False(new UiSettingsChange(Surface: SurfaceStyle.Glossy).IsEmpty);
    }

    [Fact]
    public async Task Grids_AreSavedPerUserAndGrid_ListedById_AndReset()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        await w.UiSettings.SaveGridAsync("vehicles", Grid(10, "licensePlate"), default);
        await w.UiSettings.SaveGridAsync("expenses", Grid(), default);
        await w.UiSettings.SaveGridAsync("vehicles", Grid(50), default); // replaces

        var mine = await w.UiSettings.ListGridsAsync(default);
        Assert.Equal(["expenses", "vehicles"], mine.Select(g => g.GridId));
        Assert.Equal((50, 0), (mine[1].PageSize, mine[1].Hidden.Count));

        w.Current.SignInAs(bob);
        Assert.Empty(await w.UiSettings.ListGridsAsync(default));
        Assert.False(await w.UiSettings.ResetGridAsync("vehicles", default)); // nothing of Bob's to forget

        w.Current.SignInAs(alice);
        Assert.True(await w.UiSettings.ResetGridAsync("vehicles", default));
        Assert.Equal(["expenses"], (await w.UiSettings.ListGridsAsync(default)).Select(g => g.GridId));
    }

    [Fact]
    public async Task InvalidValues_AreRefusedWithKeys()
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("alice@x.co"));

        Assert.Equal("settings.gridIdInvalid", (await Assert.ThrowsAsync<DomainException>(() => w.UiSettings.SaveGridAsync("Vehicles", Grid(), default))).Key);
        Assert.Equal("settings.pageSizeInvalid", (await Assert.ThrowsAsync<DomainException>(() => w.UiSettings.SaveGridAsync("vehicles", Grid(0), default))).Key);
        Assert.Equal("settings.languageInvalid", (await Assert.ThrowsAsync<DomainException>(() => w.UiSettings.UpdateAsync(new UiSettingsChange(Language: "english"), default))).Key);
        Assert.Equal("settings.colorModeInvalid", (await Assert.ThrowsAsync<DomainException>(() => w.UiSettings.UpdateAsync(new UiSettingsChange(ColorMode: (ColorMode)9), default))).Key);
        Assert.Equal("settings.surfaceInvalid", (await Assert.ThrowsAsync<DomainException>(() => w.UiSettings.UpdateAsync(new UiSettingsChange(Surface: (SurfaceStyle)9), default))).Key);
        Assert.Equal("settings.gridIdInvalid", (await Assert.ThrowsAsync<DomainException>(() => w.UiSettings.ResetGridAsync("", default))).Key);
        Assert.Empty(w.UiSettingsStore.Grids);
    }

    [Fact]
    public async Task WithAuthenticationOff_TheAnonymousUserOwnsTheSettings()
    {
        var w = new World(AuthMode.None);

        await w.UiSettings.UpdateAsync(new UiSettingsChange(NavOpen: false), default);

        Assert.Equal(Guid.Empty, Assert.Single(w.UiSettingsStore.Items).UserId);
        Assert.False((await w.UiSettings.GetAsync(default)).NavOpen);
    }

    [Fact]
    public async Task SignedOut_CannotReadOrWrite()
    {
        var w = new World();

        await Assert.ThrowsAsync<UnauthenticatedException>(() => w.UiSettings.GetAsync(default));
        await Assert.ThrowsAsync<UnauthenticatedException>(() => w.UiSettings.SaveGridAsync("vehicles", Grid(), default));
        await Assert.ThrowsAsync<UnauthenticatedException>(() => w.UiSettings.UpdateAsync(new UiSettingsChange(NavOpen: true), default));
    }

    [Fact]
    public async Task LogLines_CarryTheUserAndTheGridId_NothingElse()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        w.Current.SignInAs(alice);

        await w.UiSettings.UpdateAsync(new UiSettingsChange(Language: "en-GB"), default);
        await w.UiSettings.SaveGridAsync("vehicles", Grid(), default);

        var lines = w.Log.From<UiSettingsService>().ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, e => Assert.Equal(alice.Id, e.Values["UserId"]));
        Assert.Equal("vehicles", lines[1].Values["GridId"]);
        Assert.DoesNotContain(lines, e => e.Values.Values.Any(v => Equals(v, "en-GB")) || e.Text.Contains("en-GB", StringComparison.Ordinal)); // a value is never a placeholder
    }

    [Fact]
    public async Task AnEmptyChange_SavesNothing_AndClearWinsOverALanguageGivenAtTheSameTime()
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("alice@x.co"));

        var untouched = await w.UiSettings.UpdateAsync(new UiSettingsChange(), default);
        Assert.Equal((null, null), (untouched.NavOpen, untouched.Language));
        Assert.Empty(w.UiSettingsStore.Items);

        var cleared = await w.UiSettings.UpdateAsync(new UiSettingsChange(Language: "hu", ClearLanguage: true), default);
        Assert.Null(cleared.Language);
    }
}
