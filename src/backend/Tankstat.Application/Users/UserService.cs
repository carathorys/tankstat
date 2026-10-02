using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Images;
using Tankstat.Application.Imports;
using Tankstat.Domain;
using Tankstat.Domain.Images;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Users;

/// <summary>Administrator-only user management.</summary>
public sealed class UserService(
    AccessService access, IUserRepository users, IUserDataRepository userData, PasswordResetService resets, IPasswordHasher hasher,
    ImageService images, ImportSessionStore importSessions, IOptions<AuthOptions> auth)
{
    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        return await users.ListAsync(ct);
    }

    /// <summary>Creates a local user without a password and issues a setup link for them.</summary>
    public async Task<(User User, IssuedReset Reset)> CreateLocalAsync(string? email, string? displayName, bool isAdmin, CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        if (auth.Value.Mode != AuthMode.Standalone) throw new DomainException("user.onlyStandalone", "Users are only created here in Standalone authentication mode.");

        var user = User.CreateLocal(email!, displayName, isAdmin);
        if (await users.FindLocalByEmailAsync(user.Email, ct) is not null) throw new DomainException("user.emailExists", "A user with this e-mail already exists.");

        await users.AddAsync(user, ct);
        return (user, await resets.IssueAsync(user, sendEmail: true, ct));
    }

    public async Task<IssuedReset> IssueResetAsync(Guid userId, CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        var user = await Find(userId, ct);
        if (user.Provider != UserProvider.Local) throw new DomainException("user.passwordsLocalOnly", "Only local users have passwords managed here.");
        return await resets.IssueAsync(user, sendEmail: true, ct);
    }

    public async Task<User> SetAdminAsync(Guid userId, bool isAdmin, CancellationToken ct)
    {
        var self = await access.RequireAdminAsync(ct);
        if (!isAdmin && self.Id == userId) throw new DomainException("user.cannotDemoteSelf", "You cannot remove your own administrator rights.");
        var user = await Find(userId, ct);
        user.SetAdmin(isAdmin);
        await users.UpdateAsync(user, ct);
        return user;
    }

    public async Task<User> SetDisabledAsync(Guid userId, bool disabled, CancellationToken ct)
    {
        var self = await access.RequireAdminAsync(ct);
        if (disabled && self.Id == userId) throw new DomainException("user.cannotDisableSelf", "You cannot disable your own account.");
        var user = await Find(userId, ct);
        user.SetDisabled(disabled);
        await users.UpdateAsync(user, ct);
        return user;
    }

    /// <summary>Changes name and e-mail of a local user; users from an external provider keep what the provider says.</summary>
    public async Task<User> UpdateAsync(Guid userId, string? email, string? displayName, CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        var user = await Find(userId, ct);
        if (user.Provider != UserProvider.Local) throw new DomainException("user.profileLocalOnly", "The profile of users from an external provider comes from that provider.");

        var normalized = User.NormalizeEmail(email);
        if (normalized != user.Subject && await users.FindLocalByEmailAsync(normalized, ct) is not null)
            throw new DomainException("user.emailExists", "A user with this e-mail already exists.");

        user.ChangeLocalProfile(normalized, displayName);
        await users.UpdateAsync(user, ct);
        return user;
    }

    /// <summary>Whether <see cref="SetPasswordAsync"/> is switched on (<c>Auth:Standalone:AllowAdminSetPassword</c>).</summary>
    public async Task<bool> CanSetPasswordsAsync(CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        return AdminPasswordsAllowed;
    }

    private bool AdminPasswordsAllowed => auth.Value.Mode == AuthMode.Standalone && auth.Value.Standalone.AllowAdminSetPassword;

    /// <summary>Sets a local user's password directly and signs them out everywhere. Off unless the instance allows it.</summary>
    public async Task SetPasswordAsync(Guid userId, string? newPassword, CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        if (!AdminPasswordsAllowed) throw new DomainException("user.adminPasswordDisabled", "Setting passwords directly is turned off on this instance.");
        var user = await Find(userId, ct);
        if (user.Provider != UserProvider.Local) throw new DomainException("user.passwordsLocalOnly", "Only local users have passwords managed here.");

        PasswordPolicy.Validate(newPassword);
        user.SetPasswordHash(hasher.Hash(newPassword!));
        await users.UpdateAsync(user, ct);
        await resets.RevokeAllAsync(user.Id, ct);
    }

    /// <summary>
    /// Deletes a user. If they own anything the caller has to say what happens to it: move it to another user or purge it.
    /// </summary>
    public async Task DeleteAsync(Guid userId, UserDataDisposition? data, Guid? moveToUserId, CancellationToken ct)
    {
        var self = await access.RequireAdminAsync(ct);
        if (self.Id == userId) throw new DomainException("user.cannotDeleteSelf", "You cannot delete your own account.");
        var user = await Find(userId, ct);
        if (user.Provider != UserProvider.Local)
            throw new DomainException("user.deleteLocalOnly", "Users from an external provider come back at their next sign-in; disable them instead.");
        if (user.IsAdmin && !user.IsDisabled && !(await users.ListAsync(ct)).Any(u => u.Id != userId && u.IsAdmin && !u.IsDisabled))
            throw new DomainException("user.lastAdmin", "The last active administrator cannot be deleted.");

        Guid? target = null;
        if (await userData.OwnsDataAsync(userId, ct))
        {
            switch (data)
            {
                case null:
                    throw new DomainException("user.dataChoiceRequired", "This user owns data: choose to move it to another user or to delete it.");
                case UserDataDisposition.Move:
                    if (moveToUserId is null || moveToUserId == userId) throw new DomainException("user.moveTargetRequired", "Choose another user to receive the data.");
                    target = (await Find(moveToUserId.Value, ct)).Id;
                    break;
            }
        }

        var purged = await userData.DeleteUserAsync(userId, target, ct);
        await images.DeleteVehicleFilesAsync(purged.VehicleIds, user.AvatarImageId is { } avatar ? purged.PictureIds.Append(avatar) : purged.PictureIds, ct);
        await images.DeleteFoldersAsync([ImageFolders.Avatar(userId)], ct); // the avatar's folder (and its rows) goes with the account
        importSessions.RemoveForUser(userId);
    }

    private async Task<User> Find(Guid id, CancellationToken ct) =>
        await users.FindByIdAsync(id, ct) ?? throw new NotFoundException("user.notFound", $"User {id} does not exist.", new { Id = id });
}
