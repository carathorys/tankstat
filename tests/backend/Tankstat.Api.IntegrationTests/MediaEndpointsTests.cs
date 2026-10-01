using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tankstat.Api.IntegrationTests;

/// <summary>Picture upload and download over real HTTP: avatars (Standalone) and vehicle pictures, with the access rules.</summary>
[Collection(ApiCollection.Name)]
public class MediaEndpointsTests : IDisposable
{
    private readonly TestApp _app = TestApp.Standalone();

    public void Dispose() => _app.Dispose();

    private static byte[] Png(byte marker = 0) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker, 1, 2, 3, 4];
    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    private static async Task<HttpResponseMessage> Put(HttpClient c, string path, byte[] body, string contentType = "application/octet-stream")
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await c.PutAsync(path, content);
    }

    private async Task<(HttpClient Admin, HttpClient Alice, HttpClient Bob, string AliceId, string BobId)> Users()
    {
        var admin = _app.NewClient();
        await admin.LoginAs("root@example.com", "initial-password-1");
        async Task<(HttpClient, string)> Create(string name)
        {
            var created = (await admin.Gql("mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } reset { token } } }", new { i = new { email = $"{name}@example.com", displayName = name, isAdmin = false } })).Data().GetProperty("createUser");
            await _app.NewClient().Gql("mutation($i: ResetPasswordInput!) { resetPassword(input: $i) }", new { i = new { token = created.GetProperty("reset").GetProperty("token").GetString(), newPassword = name + "-password-1" } });
            var client = _app.NewClient();
            await client.LoginAs($"{name}@example.com", name + "-password-1");
            return (client, created.GetProperty("user").GetProperty("id").GetString()!);
        }
        var (alice, aliceId) = await Create("alice");
        var (bob, bobId) = await Create("bob");
        return (admin, alice, bob, aliceId, bobId);
    }

    private static async Task<(string Id, string Url)> Uploaded(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("id").GetString()!, json.GetProperty("url").GetString()!);
    }

    private static async Task<string?> ErrorKey(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString();

    // ---- avatars ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task AUserUploadsAnAvatar_AndEveryoneSignedInSeesItWithSafeHeaders()
    {
        var u = await Users();

        var (_, url) = await Uploaded(await Put(u.Alice, "/media/me/avatar", Png(1), "image/png"));

        var response = await u.Bob.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png(1), await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("immutable", response.Headers.CacheControl!.ToString());
        Assert.Contains("private", response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("sandbox", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
    }

    [Fact]
    public async Task TheAvatarUrl_AppearsWhereUsersAreShown()
    {
        var u = await Users();
        var (_, url) = await Uploaded(await Put(u.Alice, "/media/me/avatar", Jpeg()));

        var session = (await u.Alice.Gql("{ session { user { avatarUrl } } }")).Data().GetProperty("session").GetProperty("user");
        var users = (await u.Admin.Gql("{ users { displayName avatarUrl } }")).Data().GetProperty("users").EnumerateArray();

        Assert.Equal(url, session.GetProperty("avatarUrl").GetString());
        Assert.Equal(url, users.Single(x => x.GetProperty("displayName").GetString() == "alice").GetProperty("avatarUrl").GetString());
        Assert.Equal(JsonValueKind.Null, users.Single(x => x.GetProperty("displayName").GetString() == "bob").GetProperty("avatarUrl").ValueKind);
    }

    [Fact]
    public async Task ReplacingTheAvatar_GivesANewUrl_AndTheOldOneStopsWorking()
    {
        var u = await Users();
        var (_, first) = await Uploaded(await Put(u.Alice, "/media/me/avatar", Png(1)));

        var (_, second) = await Uploaded(await Put(u.Alice, "/media/me/avatar", Png(2)));

        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.NotFound, (await u.Bob.GetAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await u.Bob.GetAsync(second)).StatusCode);
        Assert.Single(Directory.GetFiles(_app.UploadsPath)); // the old file is gone
    }

    [Fact]
    public async Task RemovingTheAvatar_DeletesIt()
    {
        var u = await Users();
        var (_, url) = await Uploaded(await Put(u.Alice, "/media/me/avatar", Png()));

        Assert.Equal(HttpStatusCode.NoContent, (await u.Alice.DeleteAsync("/media/me/avatar")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await u.Bob.GetAsync(url)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await u.Alice.Gql("{ session { user { avatarUrl } } }")).Data().GetProperty("session").GetProperty("user").GetProperty("avatarUrl").ValueKind);
        Assert.Empty(Directory.GetFiles(_app.UploadsPath));
    }

    [Fact]
    public async Task OnlySignedInUsersMaySeeOrChangeAvatars()
    {
        var u = await Users();
        var (_, url) = await Uploaded(await Put(u.Alice, "/media/me/avatar", Png()));
        var anonymous = _app.NewClient();

        var get = await anonymous.GetAsync(url);
        var put = await Put(anonymous, "/media/me/avatar", Png());

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal("auth.unauthenticated", await ErrorKey(get));
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("/media/me/avatar")).StatusCode);
    }

    [Theory]
    [InlineData("svg", "image.unsupportedType")]
    [InlineData("html", "image.unsupportedType")]
    [InlineData("empty", "image.empty")]
    public async Task BadFiles_AreRejected_WhateverTheyClaimToBe(string kind, string key)
    {
        var u = await Users();
        var body = kind switch
        {
            "svg" => "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray(),
            "html" => "<html></html>"u8.ToArray(),
            _ => [],
        };

        var response = await Put(u.Alice, "/media/me/avatar", body, "image/png"); // claims to be a PNG

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(key, await ErrorKey(response));
        Assert.False(Directory.Exists(_app.UploadsPath) && Directory.GetFiles(_app.UploadsPath).Length > 0);
    }

    [Fact]
    public async Task TooLargeFiles_AreRejectedWith413()
    {
        var u = await Users();
        var big = new byte[2 * 1024 * 1024 + 1];
        Png().CopyTo(big, 0);

        var response = await Put(u.Alice, "/media/me/avatar", big, "image/png");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("image.tooLarge", await ErrorKey(response));
    }

    [Fact]
    public async Task UnknownAndMalformedAddresses_AreNotFound()
    {
        var u = await Users();

        Assert.Equal(HttpStatusCode.NotFound, (await u.Alice.GetAsync($"/media/{Guid.NewGuid():N}")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await u.Alice.GetAsync("/media/not-a-guid")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await u.Alice.GetAsync("/media/..%2F..%2Fappsettings.json")).StatusCode);
    }

    // ---- vehicle pictures ----------------------------------------------------------------------------------

    private static async Task<string> AddVehicle(HttpClient owner) =>
        (await owner.Gql("mutation { addVehicle(input: { name: \"Pictured\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;

    [Fact]
    public async Task OwnersUploadVehiclePictures_AndOnlyWhoCanSeeTheVehicleSeeThePicture()
    {
        var u = await Users();
        var car = await AddVehicle(u.Alice);
        var (_, url) = await Uploaded(await Put(u.Alice, $"/media/vehicles/{car}/picture", Jpeg(), "image/jpeg"));

        var pictureUrl = (await u.Alice.Gql("query($id: UUID!) { vehicle(id: $id) { pictureUrl } }", new { id = car })).Data().GetProperty("vehicle").GetProperty("pictureUrl").GetString();
        Assert.Equal(url, pictureUrl);
        Assert.Equal("image/jpeg", (await u.Alice.GetAsync(url)).Content.Headers.ContentType!.MediaType);

        Assert.Equal(HttpStatusCode.NotFound, (await u.Bob.GetAsync(url)).StatusCode); // a stranger cannot see it

        await u.Admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }", new { i = new { ownerId = u.AliceId, granteeId = u.BobId, level = "VIEW" } });
        Assert.Equal(HttpStatusCode.OK, (await u.Bob.GetAsync(url)).StatusCode); // a viewer can
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(u.Bob, $"/media/vehicles/{car}/picture", Png())).StatusCode); // but not change it
        Assert.Equal(HttpStatusCode.Forbidden, (await u.Bob.DeleteAsync($"/media/vehicles/{car}/picture")).StatusCode);
    }

    [Fact]
    public async Task Strangers_CannotUploadToAVehicle()
    {
        var u = await Users();
        var car = await AddVehicle(u.Alice);

        var response = await Put(u.Bob, $"/media/vehicles/{car}/picture", Png());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("vehicle.notFound", await ErrorKey(response));
    }

    [Fact]
    public async Task ALogGrantee_SeesThePicture_ButCannotChangeIt()
    {
        var u = await Users();
        var car = await AddVehicle(u.Alice);
        var (_, url) = await Uploaded(await Put(u.Alice, $"/media/vehicles/{car}/picture", Png()));
        await u.Alice.Gql("mutation($i: SetLogAccessInput!) { setVehicleLogAccess(input: $i) }", new { i = new { vehicleId = car, userId = u.BobId, level = "EDIT" } });

        Assert.Equal(HttpStatusCode.OK, (await u.Bob.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(u.Bob, $"/media/vehicles/{car}/picture", Png(9))).StatusCode);
    }

    [Fact]
    public async Task ATrashedVehiclesPicture_IsHidden_AndDeletedForGoodWithTheVehicle()
    {
        var u = await Users();
        var car = await AddVehicle(u.Alice);
        var (_, url) = await Uploaded(await Put(u.Alice, $"/media/vehicles/{car}/picture", Png()));
        await u.Alice.Gql("mutation($id: UUID!) { deleteVehicle(id: $id) { id } }", new { id = car });

        Assert.Equal(HttpStatusCode.NotFound, (await u.Alice.GetAsync(url)).StatusCode);
        Assert.Single(Directory.GetFiles(_app.UploadsPath)); // still on disk: it can be restored
        await u.Alice.Gql("mutation { emptyTrash }");

        Assert.Empty(Directory.GetFiles(_app.UploadsPath));
    }

    [Fact]
    public async Task WithoutAuthentication_VehiclePicturesWork_ButThereAreNoProfilePictures()
    {
        using var none = new TestApp(new() { ["Auth:Mode"] = "None" });
        var client = none.NewClient();
        var car = (await client.Gql("mutation { addVehicle(input: { name: \"Plain\", fuelType: LPG }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString();

        var (_, url) = await Uploaded(await Put(client, $"/media/vehicles/{car}/picture", Png()));
        var avatar = await Put(client, "/media/me/avatar", Png());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, avatar.StatusCode);
        Assert.Equal("image.noAccount", await ErrorKey(avatar));
    }
}
