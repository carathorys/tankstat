using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Domain;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Users;

/// <summary>Administrator-only user management.</summary>
public sealed class UserService(
    AccessService access, IUserRepository users, PasswordResetService resets, IOptions<AuthOptions> auth)
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

    private async Task<User> Find(Guid id, CancellationToken ct) =>
        await users.FindByIdAsync(id, ct) ?? throw new NotFoundException("user.notFound", $"User {id} does not exist.", new { Id = id });
}
