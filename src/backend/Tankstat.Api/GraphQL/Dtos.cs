using Tankstat.Api.Media;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Domain.Access;
using Tankstat.Domain.Users;

namespace Tankstat.Api.GraphQL;

/// <summary>GraphQL shapes for users. Domain <see cref="User"/> is never exposed: it carries the password hash.</summary>
public sealed record UserInfo(Guid Id, string DisplayName, string Email, bool IsAdmin, string? AvatarUrl)
{
    public static UserInfo From(Principal p) => new(p.Id, p.DisplayName, p.Email, p.IsAdmin, MediaUrls.Image(p.AvatarImageId));
    public static UserInfo From(User u) => new(u.Id, u.DisplayName, u.Email, u.IsAdmin, MediaUrls.Image(u.AvatarImageId));
}

/// <summary>A user as shown next to data (owner, creator, grantee): enough for a name and an avatar, nothing private.</summary>
public sealed record UserRef(Guid Id, string DisplayName, string? AvatarUrl)
{
    public static UserRef From(User u) => new(u.Id, u.DisplayName, MediaUrls.Image(u.AvatarImageId));
}

public sealed record Session(AuthMode Mode, UserInfo? User);

public sealed record UserAccount(Guid Id, UserProvider Provider, string Email, string DisplayName, bool IsAdmin, bool IsDisabled, string? AvatarUrl)
{
    public static UserAccount From(User u) => new(u.Id, u.Provider, u.Email, u.DisplayName, u.IsAdmin, u.IsDisabled, MediaUrls.Image(u.AvatarImageId));
}

public sealed record PasswordResetLink(string Token, string? Url, bool EmailSent, bool EmailFailed)
{
    public static PasswordResetLink From(IssuedReset r) => new(r.Token, r.Url, r.EmailSent, r.EmailFailed);
}

public sealed record CreatedUser(UserAccount User, PasswordResetLink Reset);

public sealed record AccessSettingsInfo(AccessLevel DefaultLevelForOthers);

public sealed record AccessGrantInfo(Guid Id, Guid OwnerId, Guid GranteeId, AccessLevel Level);

public sealed record LoginInput(string Email, string Password);
public sealed record ChangePasswordInput(string CurrentPassword, string NewPassword);
public sealed record ResetPasswordInput(string Token, string NewPassword);
public sealed record CreateUserInput(string Email, string? DisplayName, bool IsAdmin);
public sealed record UpdateUserInput(Guid UserId, string Email, string? DisplayName);
public sealed record DeleteUserInput(Guid UserId, UserDataDisposition? Data, Guid? MoveToUserId);
public sealed record SetAccessGrantInput(Guid OwnerId, Guid GranteeId, AccessLevel Level);
