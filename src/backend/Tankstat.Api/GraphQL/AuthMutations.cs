using Microsoft.AspNetCore.Authentication;
using Tankstat.Api.Auth;
using Tankstat.Application.Access;
using Tankstat.Application.Users;

namespace Tankstat.Api.GraphQL;

/// <summary>Sign-in, sign-out and password handling (Standalone mode) plus administrator user/access management.</summary>
[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class AuthMutations
{
    public async Task<UserInfo> Login(
        LoginInput input, [Service] AuthService auth, [Service] IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await auth.LoginAsync(input.Email, input.Password, ct);
        await http.HttpContext!.SignInAsync(
            SessionClaims.CookieScheme, SessionClaims.Create(user, SessionClaims.CookieScheme),
            new AuthenticationProperties { IsPersistent = true });
        return UserInfo.From(user);
    }

    public async Task<bool> Logout([Service] IHttpContextAccessor http)
    {
        await http.HttpContext!.SignOutAsync(SessionClaims.CookieScheme);
        return true;
    }

    public async Task<bool> ChangePassword(
        ChangePasswordInput input, [Service] AuthService auth, [Service] IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await auth.ChangePasswordAsync(input.CurrentPassword, input.NewPassword, ct);
        // Every other session is now invalid (new session version); keep this one signed in.
        await http.HttpContext!.SignInAsync(
            SessionClaims.CookieScheme, SessionClaims.Create(user, SessionClaims.CookieScheme),
            new AuthenticationProperties { IsPersistent = true });
        return true;
    }

    /// <summary>Always returns true so it cannot be used to discover which e-mails are registered.</summary>
    public async Task<bool> RequestPasswordReset(string email, [Service] AuthService auth, CancellationToken ct)
    {
        await auth.RequestPasswordResetAsync(email, ct);
        return true;
    }

    public async Task<bool> ResetPassword(ResetPasswordInput input, [Service] AuthService auth, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(input.Token, input.NewPassword, ct);
        return true;
    }

    public async Task<CreatedUser> CreateUser(CreateUserInput input, [Service] UserService users, CancellationToken ct)
    {
        var (user, reset) = await users.CreateLocalAsync(input.Email, input.DisplayName, input.IsAdmin, ct);
        return new CreatedUser(UserAccount.From(user), PasswordResetLink.From(reset));
    }

    public async Task<PasswordResetLink> IssuePasswordReset(Guid userId, [Service] UserService users, CancellationToken ct) =>
        PasswordResetLink.From(await users.IssueResetAsync(userId, ct));

    public async Task<UserAccount> SetUserAdmin(Guid userId, bool isAdmin, [Service] UserService users, CancellationToken ct) =>
        UserAccount.From(await users.SetAdminAsync(userId, isAdmin, ct));

    public async Task<UserAccount> SetUserDisabled(Guid userId, bool disabled, [Service] UserService users, CancellationToken ct) =>
        UserAccount.From(await users.SetDisabledAsync(userId, disabled, ct));

    public async Task<AccessSettingsInfo> SetDefaultAccess(
        Tankstat.Domain.Access.AccessLevel level, [Service] AccessAdminService admin, CancellationToken ct) =>
        new((await admin.SetDefaultLevelAsync(level, ct)).DefaultLevelForOthers);

    public async Task<bool> SetAccessGrant(SetAccessGrantInput input, [Service] AccessAdminService admin, CancellationToken ct)
    {
        await admin.SetGrantAsync(input.OwnerId, input.GranteeId, input.Level, ct);
        return true;
    }
}
