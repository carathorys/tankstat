using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Domain;
using Tankstat.Domain.Images;
using Tankstat.Domain.Users;

namespace Tankstat.Application.UnitTests;

public class UserAdministrationTests
{
    private static (World W, User Admin) SignedInAdmin(Action<AuthOptions>? configure = null)
    {
        var w = new World(configure: configure);
        var admin = w.AddUser("root@x.co", admin: true);
        w.Current.SignInAs(admin);
        return (w, admin);
    }

    [Fact]
    public async Task Update_ChangesNameAndEmailOfLocalUsers_AndLoginFollowsTheEmail()
    {
        var (w, _) = SignedInAdmin();
        var alice = w.AddUser("alice@x.co");

        await w.UserService.UpdateAsync(alice.Id, "Alice.New@X.co", "Alice N.", default);

        Assert.Equal("alice.new@x.co", alice.Email);
        Assert.Equal("alice.new@x.co", alice.Subject);
        Assert.Equal("Alice N.", alice.DisplayName);
        await w.Auth.LoginAsync("alice.new@x.co", "password-123456", default);
    }

    [Fact]
    public async Task Update_RejectsTakenEmails_ExternalUsers_AndNonAdministrators()
    {
        var (w, _) = SignedInAdmin();
        var alice = w.AddUser("alice@x.co");
        w.AddUser("bob@x.co");
        var oidc = User.CreateExternal(UserProvider.Oidc, "sub", "o@x.co", "O", false);
        w.Users.Items.Add(oidc);

        Assert.Equal("user.emailExists", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.UpdateAsync(alice.Id, "BOB@x.co", null, default))).Key);
        await w.UserService.UpdateAsync(alice.Id, "alice@x.co", "Same e-mail is fine", default);
        Assert.Equal("user.profileLocalOnly", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.UpdateAsync(oidc.Id, "n@x.co", "N", default))).Key);
        Assert.Equal("user.emailInvalid", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.UpdateAsync(alice.Id, "nope", null, default))).Key);

        w.Current.SignInAs(alice);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.UserService.UpdateAsync(alice.Id, "x@x.co", null, default));
    }

    [Fact]
    public async Task SetPassword_IsOffByDefault()
    {
        var (w, _) = SignedInAdmin();
        var alice = w.AddUser("alice@x.co");

        Assert.False(await w.UserService.CanSetPasswordsAsync(default));
        Assert.Equal("user.adminPasswordDisabled", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.SetPasswordAsync(alice.Id, "brand-new-pass-1", default))).Key);
    }

    [Fact]
    public async Task SetPassword_WhenAllowed_ChangesThePassword_AndSignsTheUserOutEverywhere()
    {
        var (w, _) = SignedInAdmin(o => o.Standalone.AllowAdminSetPassword = true);
        var alice = w.AddUser("alice@x.co");
        var version = alice.SessionVersion;

        Assert.True(await w.UserService.CanSetPasswordsAsync(default));
        await w.UserService.SetPasswordAsync(alice.Id, "brand-new-pass-1", default);

        Assert.Equal(version + 1, alice.SessionVersion);
        await w.Auth.LoginAsync("alice@x.co", "brand-new-pass-1", default);
    }

    [Fact]
    public async Task SetPassword_EnforcesThePolicy_LocalUsersOnly_AndOnlyInStandaloneMode()
    {
        var (w, _) = SignedInAdmin(o => o.Standalone.AllowAdminSetPassword = true);
        var alice = w.AddUser("alice@x.co");
        var oidc = User.CreateExternal(UserProvider.Oidc, "sub", "o@x.co", "O", false);
        w.Users.Items.Add(oidc);

        await Assert.ThrowsAsync<DomainException>(() => w.UserService.SetPasswordAsync(alice.Id, "short", default));
        Assert.Equal("user.passwordsLocalOnly", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.SetPasswordAsync(oidc.Id, "brand-new-pass-1", default))).Key);

        w.Options.Mode = AuthMode.Oidc;
        Assert.Equal("user.adminPasswordDisabled", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.SetPasswordAsync(alice.Id, "brand-new-pass-1", default))).Key);
    }

    [Fact]
    public async Task Delete_UserWithoutData_JustDeletesThem_AndTheirAvatar()
    {
        var (w, _) = SignedInAdmin();
        var alice = w.AddUser("alice@x.co");
        var avatar = Guid.NewGuid();
        w.Images.Items[avatar] = StoredImage.Create(avatar, "image/png", 1, w.Clock.GetUtcNow());
        w.ImageStore.Files[avatar] = [1];
        alice.SetAvatar(avatar);

        await w.UserService.DeleteAsync(alice.Id, null, null, default);

        Assert.Equal([(alice.Id, (Guid?)null)], w.UserData.Deleted);
        Assert.Empty(w.Images.Items);
        Assert.Empty(w.ImageStore.Files);
    }

    [Fact]
    public async Task Delete_UserWithData_NeedsAChoice()
    {
        var (w, _) = SignedInAdmin();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.UserData.Owners.Add(alice.Id);

        Assert.Equal("user.dataChoiceRequired", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.DeleteAsync(alice.Id, null, null, default))).Key);
        Assert.Equal("user.moveTargetRequired", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.DeleteAsync(alice.Id, UserDataDisposition.Move, null, default))).Key);
        Assert.Equal("user.moveTargetRequired", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.DeleteAsync(alice.Id, UserDataDisposition.Move, alice.Id, default))).Key);
        await Assert.ThrowsAsync<NotFoundException>(() => w.UserService.DeleteAsync(alice.Id, UserDataDisposition.Move, Guid.NewGuid(), default));
        Assert.Empty(w.UserData.Deleted);

        await w.UserService.DeleteAsync(alice.Id, UserDataDisposition.Move, bob.Id, default);

        Assert.Equal([(alice.Id, (Guid?)bob.Id)], w.UserData.Deleted);
    }

    [Fact]
    public async Task Delete_Purge_RemovesThePicturesOfPurgedVehicles()
    {
        var (w, _) = SignedInAdmin();
        var alice = w.AddUser("alice@x.co");
        w.UserData.Owners.Add(alice.Id);
        var picture = Guid.NewGuid();
        w.Images.Items[picture] = StoredImage.Create(picture, "image/png", 1, w.Clock.GetUtcNow());
        w.ImageStore.Files[picture] = [1];
        w.UserData.PurgedPictures.Add(picture);

        await w.UserService.DeleteAsync(alice.Id, UserDataDisposition.Purge, null, default);

        Assert.Equal([(alice.Id, (Guid?)null)], w.UserData.Deleted);
        Assert.Empty(w.ImageStore.Files);
    }

    [Fact]
    public async Task Delete_RefusesSelf_AndTheLastActiveAdministrator_AndNonAdministrators()
    {
        var (w, admin) = SignedInAdmin();
        var other = w.AddUser("other@x.co", admin: true);

        Assert.Equal("user.cannotDeleteSelf", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.DeleteAsync(admin.Id, null, null, default))).Key);

        other.SetDisabled(true); // the only active admin left is the one signed in, so deleting a disabled admin is fine ...
        await w.UserService.DeleteAsync(other.Id, null, null, default);

        var alice = w.AddUser("alice@x.co");
        w.Current.SignInAs(alice);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.UserService.DeleteAsync(admin.Id, null, null, default));
    }

    [Fact]
    public async Task Delete_LastActiveAdministrator_IsRefused()
    {
        var (w, _) = SignedInAdmin();
        var second = w.AddUser("second@x.co", admin: true);
        // an administrator signed in as someone who is no longer an admin in the store cannot happen, so simulate: only `second` is active
        w.Users.Items.RemoveAll(u => u.Email == "root@x.co");

        Assert.Equal("user.lastAdmin", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.DeleteAsync(second.Id, null, null, default))).Key);
        Assert.Empty(w.UserData.Deleted);
    }

    [Fact]
    public async Task Delete_ClearsPendingImportSessions()
    {
        var (w, _) = SignedInAdmin();
        var alice = w.AddUser("alice@x.co");
        var token = w.ImportSessions.Save(alice.Id, new Imports.ImportBatch("fuelio", null, [], [], [], []));

        await w.UserService.DeleteAsync(alice.Id, null, null, default);

        Assert.Null(w.ImportSessions.Find(alice.Id, token));
    }

    [Fact]
    public async Task Delete_RefusesExternalUsers_BecauseTheyComeBackAtTheirNextSignIn()
    {
        var (w, _) = SignedInAdmin();
        var oidc = User.CreateExternal(UserProvider.Oidc, "sub", "o@x.co", "O", false);
        w.Users.Items.Add(oidc);

        Assert.Equal("user.deleteLocalOnly", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.DeleteAsync(oidc.Id, null, null, default))).Key);
        Assert.Empty(w.UserData.Deleted);
    }

    [Fact]
    public async Task SetPassword_RevokesEveryOutstandingResetLink()
    {
        var (w, _) = SignedInAdmin(o => o.Standalone.AllowAdminSetPassword = true);
        var (user, reset) = await w.UserService.CreateLocalAsync("new@x.co", null, false, default);

        await w.UserService.SetPasswordAsync(user.Id, "brand-new-pass-1", default);

        await Assert.ThrowsAsync<DomainException>(() => w.Auth.ResetPasswordAsync(reset.Token, "another-pass-1234", default));
        await w.Auth.LoginAsync("new@x.co", "brand-new-pass-1", default);
    }

    [Fact]
    public async Task ResetPassword_RevokesTheOtherLinksOfThatUser()
    {
        var (w, _) = SignedInAdmin();
        var (user, first) = await w.UserService.CreateLocalAsync("new@x.co", null, false, default);
        var second = await w.UserService.IssueResetAsync(user.Id, default);

        await w.Auth.ResetPasswordAsync(first.Token, "chosen-password-1", default);

        await Assert.ThrowsAsync<DomainException>(() => w.Auth.ResetPasswordAsync(second.Token, "another-pass-1234", default));
    }

    [Fact]
    public async Task Delete_RemovesTheUploadFoldersOfTheUserAndOfTheirPurgedVehicles()
    {
        var (w, _) = SignedInAdmin();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        var vehicle = Guid.NewGuid();
        w.UserData.Owners.Add(alice.Id);
        w.UserData.PurgedVehicles.Add(vehicle);
        Guid Put(string folder)
        {
            var id = Guid.NewGuid();
            w.Images.Items[id] = StoredImage.Create(id, "image/png", 1, w.Clock.GetUtcNow(), folder);
            w.ImageStore.Files[id] = [1];
            w.ImageStore.Folders[id] = folder;
            return id;
        }
        var avatar = Put(ImageFolders.Avatar(alice.Id));
        alice.SetAvatar(avatar);
        Put(ImageFolders.VehiclePicture(vehicle));
        Put($"{ImageFolders.Vehicle(vehicle)}/expenses/{Guid.NewGuid():N}");
        var othersAvatar = Put(ImageFolders.Avatar(bob.Id));
        var othersVehicle = Put(ImageFolders.VehiclePicture(Guid.NewGuid()));

        await w.UserService.DeleteAsync(alice.Id, UserDataDisposition.Purge, null, default);

        Assert.Equivalent(new[] { othersAvatar, othersVehicle }, w.ImageStore.Files.Keys);
        Assert.Equivalent(new[] { othersAvatar, othersVehicle }, w.Images.Items.Keys);
    }
}
