using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tankstat.Api.IntegrationTests;

/// <summary>UI settings over real GraphQL and a real database: they follow the user, not the browser.</summary>
[Collection(ApiCollection.Name)]
public class UiSettingsApiTests : IDisposable
{
    private const string Read = "{ uiSettings { navOpen language grids { gridId order hidden pageSize sortColumn sortDirection } } }";
    private const string SaveGrid = "mutation($i: GridSettingsInput!) { saveGridSettings(input: $i) { gridId pageSize } }";

    private readonly TestApp _app = TestApp.Standalone();

    public void Dispose() => _app.Dispose();

    private static object Grid(string gridId = "vehicles", int pageSize = 10, string[]? hidden = null) =>
        new { gridId, order = new[] { "name", "owner" }, hidden = hidden ?? ["owner"], pageSize, sortColumn = "name", sortDirection = "DESC" };

    private static string Key(JsonElement body) => body.GetProperty("errors")[0].GetProperty("extensions").GetProperty("key").GetString()!;

    [Fact]
    public async Task Settings_FollowTheUser_NotTheBrowser()
    {
        var people = await _app.Users();
        await people.Alice.Gql("mutation { updateUiSettings(input: { navOpen: false, language: \"hu\" }) { navOpen } }");
        await people.Alice.Gql(SaveGrid, new { i = Grid() });

        var otherBrowser = _app.NewClient();
        await otherBrowser.LoginAs("alice@example.com", "alice-password-1");
        var mine = (await otherBrowser.Gql(Read)).Data().GetProperty("uiSettings");
        var bobs = (await people.Bob.Gql(Read)).Data().GetProperty("uiSettings");

        Assert.Equal((false, "hu"), (mine.GetProperty("navOpen").GetBoolean(), mine.GetProperty("language").GetString()));
        var grid = Assert.Single(mine.GetProperty("grids").EnumerateArray());
        Assert.Equal(("vehicles", 10, "DESC"), (grid.GetProperty("gridId").GetString(), grid.GetProperty("pageSize").GetInt32(), grid.GetProperty("sortDirection").GetString()));
        Assert.Equal(["owner"], grid.GetProperty("hidden").EnumerateArray().Select(h => h.GetString()));
        Assert.Equal(JsonValueKind.Null, bobs.GetProperty("navOpen").ValueKind);
        Assert.Empty(bobs.GetProperty("grids").EnumerateArray());
    }

    [Fact]
    public async Task Updates_ArePartial_AndTheLanguageCanBeForgotten()
    {
        var people = await _app.Users();
        await people.Alice.Gql("mutation { updateUiSettings(input: { navOpen: false }) { navOpen } }");

        var after = (await people.Alice.Gql("mutation { updateUiSettings(input: { language: \"hu\" }) { navOpen language } }")).Data().GetProperty("updateUiSettings");
        var cleared = (await people.Alice.Gql("mutation { updateUiSettings(input: { clearLanguage: true }) { navOpen language } }")).Data().GetProperty("updateUiSettings");

        Assert.Equal((false, "hu"), (after.GetProperty("navOpen").GetBoolean(), after.GetProperty("language").GetString()));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("language").ValueKind);
        Assert.False(cleared.GetProperty("navOpen").GetBoolean());
    }

    [Fact]
    public async Task ColorMode_StartsUnchosen_FollowsTheUser_AndStaysThroughOtherChanges()
    {
        var people = await _app.Users();
        Assert.Equal(JsonValueKind.Null, (await people.Alice.Gql("{ uiSettings { colorMode } }")).Data().GetProperty("uiSettings").GetProperty("colorMode").ValueKind);

        var light = (await people.Alice.Gql("mutation { updateUiSettings(input: { colorMode: LIGHT }) { colorMode } }")).Data().GetProperty("updateUiSettings");
        await people.Alice.Gql("mutation { updateUiSettings(input: { navOpen: true }) { navOpen } }");

        Assert.Equal("LIGHT", light.GetProperty("colorMode").GetString());
        Assert.Equal("LIGHT", (await people.Alice.Gql("{ uiSettings { colorMode } }")).Data().GetProperty("uiSettings").GetProperty("colorMode").GetString());
        Assert.Equal(JsonValueKind.Null, (await people.Bob.Gql("{ uiSettings { colorMode } }")).Data().GetProperty("uiSettings").GetProperty("colorMode").ValueKind);
        // Only the three modes exist: anything else is turned down by GraphQL itself (a request error), before the service sees it.
        var sepia = await people.Alice.PostAsJsonAsync("/graphql", new { query = "mutation { updateUiSettings(input: { colorMode: SEPIA }) { colorMode } }" });
        Assert.Equal(HttpStatusCode.BadRequest, sepia.StatusCode);
        Assert.Equal("LIGHT", (await people.Alice.Gql("{ uiSettings { colorMode } }")).Data().GetProperty("uiSettings").GetProperty("colorMode").GetString());
    }

    [Fact]
    public async Task Surface_StartsUnchosen_FollowsTheUser_AndStaysThroughOtherChanges()
    {
        var people = await _app.Users();
        Assert.Equal(JsonValueKind.Null, (await people.Alice.Gql("{ uiSettings { surface } }")).Data().GetProperty("uiSettings").GetProperty("surface").ValueKind);

        var opaque = (await people.Alice.Gql("mutation { updateUiSettings(input: { surface: OPAQUE }) { surface } }")).Data().GetProperty("updateUiSettings");
        await people.Alice.Gql("mutation { updateUiSettings(input: { colorMode: DARK }) { colorMode } }");

        Assert.Equal("OPAQUE", opaque.GetProperty("surface").GetString());
        Assert.Equal("OPAQUE", (await people.Alice.Gql("{ uiSettings { surface } }")).Data().GetProperty("uiSettings").GetProperty("surface").GetString());
        Assert.Equal(JsonValueKind.Null, (await people.Bob.Gql("{ uiSettings { surface } }")).Data().GetProperty("uiSettings").GetProperty("surface").ValueKind);
        // Only the three styles exist: anything else is a request error, before the service sees it.
        var matte = await people.Alice.PostAsJsonAsync("/graphql", new { query = "mutation { updateUiSettings(input: { surface: MATTE }) { surface } }" });
        Assert.Equal(HttpStatusCode.BadRequest, matte.StatusCode);
        Assert.Equal("OPAQUE", (await people.Alice.Gql("{ uiSettings { surface } }")).Data().GetProperty("uiSettings").GetProperty("surface").GetString());
    }

    [Fact]
    public async Task InvalidSettings_AreValidationErrorsWithKeys()
    {
        var people = await _app.Users();

        var badId = await people.Alice.Gql(SaveGrid, new { i = Grid(gridId: "Vehicles") });
        var badSize = await people.Alice.Gql(SaveGrid, new { i = Grid(pageSize: 0) });
        var badLanguage = await people.Alice.Gql("mutation { updateUiSettings(input: { language: \"english\" }) { language } }");

        Assert.Equal(("VALIDATION_FAILED", "settings.gridIdInvalid"), (badId.ErrorCode(), Key(badId)));
        Assert.Equal(("VALIDATION_FAILED", "settings.pageSizeInvalid"), (badSize.ErrorCode(), Key(badSize)));
        Assert.Equal(500, badSize.GetProperty("errors")[0].GetProperty("extensions").GetProperty("args").GetProperty("max").GetInt32());
        Assert.Equal("settings.languageInvalid", Key(badLanguage));
        Assert.Empty((await people.Alice.Gql(Read)).Data().GetProperty("uiSettings").GetProperty("grids").EnumerateArray());
    }

    [Fact]
    public async Task Reset_ForgetsAGrid_AndSaysWhetherThereWasOne()
    {
        var people = await _app.Users();
        await people.Alice.Gql(SaveGrid, new { i = Grid() });

        Assert.True((await people.Alice.Gql("mutation { resetGridSettings(gridId: \"vehicles\") }")).Data().GetProperty("resetGridSettings").GetBoolean());
        Assert.False((await people.Alice.Gql("mutation { resetGridSettings(gridId: \"vehicles\") }")).Data().GetProperty("resetGridSettings").GetBoolean());
        Assert.Empty((await people.Alice.Gql(Read)).Data().GetProperty("uiSettings").GetProperty("grids").EnumerateArray());
    }

    [Fact]
    public async Task SignedOut_IsRefused()
    {
        Assert.Equal("UNAUTHENTICATED", (await _app.NewClient().Gql(Read)).ErrorCode());
        Assert.Equal("UNAUTHENTICATED", (await _app.NewClient().Gql("mutation { updateUiSettings(input: { navOpen: true }) { navOpen } }")).ErrorCode());
    }
}

/// <summary>With authentication off there is one anonymous user: every visitor shares the same settings and arrangement.</summary>
[Collection(ApiCollection.Name)]
public class UiSettingsWithoutAuthTests : IDisposable
{
    private readonly TestApp _app = new(new() { ["Auth:Mode"] = "None" });

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task EveryVisitor_SharesTheAnonymousUsersSettingsAndOrder()
    {
        var one = _app.NewClient();
        var first = (await one.Gql("mutation { addVehicle(input: { name: \"First\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString();
        var second = (await one.Gql("mutation { addVehicle(input: { name: \"Second\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString();
        await one.Gql("mutation { updateUiSettings(input: { navOpen: false }) { navOpen } }");
        await one.Gql("mutation($ids: [UUID!]!) { setVehicleOrder(vehicleIds: $ids) }", new { ids = new[] { second, first } });

        var other = _app.NewClient();
        var settings = (await other.Gql("{ uiSettings { navOpen } }")).Data().GetProperty("uiSettings");
        var names = (await other.Gql("{ myVehicles { name } }")).Data().GetProperty("myVehicles").EnumerateArray().Select(v => v.GetProperty("name").GetString());

        Assert.False(settings.GetProperty("navOpen").GetBoolean());
        Assert.Equal(["Second", "First"], names);
    }
}
