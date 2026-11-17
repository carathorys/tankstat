using Microsoft.Extensions.Logging;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Users;
using Tankstat.Domain;
using Tankstat.Domain.Imports;

namespace Tankstat.Application.Imports;

/// <summary>
/// A car of a restore point as the Cars step of <i>Restore cars</i> shows it (#340): its state here, today's owner when it is here, what it
/// misses by id (entries and tyre sets), its sharing in the backup (the grantees who have an account, the names of those who have none),
/// and for a car whose owner has no account the person here with the same e-mail, suggested first in the picker.
/// </summary>
public sealed record RestoreCarOffer(
    BackupCarSummary Car, RestoreCarState State, Guid? OwnerIdHere, string? OwnerNameHere, int MissingRefuelings, int MissingExpenses, int MissingSchedules, int MissingTyreSets,
    int Grants, IReadOnlyList<string> GoneGrantees, Guid? SameEmailUserId);

/// <summary>A backup's owner for the Cars step's filter: the id and the name in the backup.</summary>
public sealed record RestoreOwner(Guid Id, string? Name);

/// <summary>A page of the restore point's cars, the file, how many the filter leaves, and the backup's owners for the filter.</summary>
public sealed record RestoreCarPage(BackupFileSummary File, int Total, IReadOnlyList<RestoreCarOffer> Cars, IReadOnlyList<RestoreOwner> Owners);

/// <summary>A person here the owner picker offers (#340): every account, with its e-mail, for administrators only.</summary>
public sealed record RestorePerson(Guid Id, string Name, string Email);

/// <summary>
/// <i>Restore cars</i> (#340): an administrator puts chosen cars of a restore point back as they were, under their own ids, to their owners.
/// The file is one of the Backups page's restore points, opened by the reader (#336) and keyed with a passphrase or an identity, no limit on
/// tries; the review, the start, the run and its report are the import's (<see cref="BackupImportRunService"/>, #337 to #339) in its restore
/// way, a run of its own kind in the same queue.
/// </summary>
public sealed class CarRestoreService(
    AccessService access, IBackupImportSessions sessions, IBackupStore store, BackupImportRules rules, BackupImportRunService runs, IUserRepository users,
    ILogger<CarRestoreService> logger)
{
    public const int MaxPage = 50;

    /// <summary>Opens a restore point of the folder (by its bare name) for the administrator: never copied, never deleted.</summary>
    public async Task<BackupImportStatus> OpenAsync(string file, CancellationToken ct)
    {
        var admin = await access.RequireAdminAsync(ct);
        var status = await sessions.OpenFileAsync(admin.Id, store.PathOfBackup(file), ct);
        logger.LogInformation("Administrator {UserId} opened a restore point to restore cars from (session {Session})", admin.Id, status.Token);
        return status;
    }

    /// <summary>A passphrase or an identity for the restore point (the owner's choice on #336: no limit on tries for a restore point).</summary>
    public async Task<BackupImportStatus> SubmitKeyAsync(string token, string? passphrase, string? identity, CancellationToken ct) =>
        sessions.SubmitKey((await access.RequireAdminAsync(ct)).Id, token, passphrase, identity);

    public async Task<BackupImportStatus> StatusAsync(string token, CancellationToken ct) =>
        sessions.Find((await access.RequireAdminAsync(ct)).Id, token) ?? throw Expired();

    /// <summary>The opened restore point is gone (its half hour, a restart, another file): opened again from the Backups page.</summary>
    public static DomainException Expired() => new("restore.expired", "This restore has expired. Open the backup again from the Backups page.");

    public async Task EndAsync(string token, CancellationToken ct) => sessions.End((await access.RequireAdminAsync(ct)).Id, token);

    /// <summary>
    /// A page of the restore point's cars, by name, filtered by a search (name or plate) and the backup's owner; each car's state and what
    /// it misses are worked out for the page only.
    /// </summary>
    public async Task<RestoreCarPage> CarsAsync(string token, string? search, Guid? ownerId, int skip, int take, CancellationToken ct)
    {
        var admin = await access.RequireAdminAsync(ct);
        var file = (sessions.Find(admin.Id, token) is { Origin: BackupFileOrigin.RestorePoint } ? sessions.Cars(admin.Id, token) : null) ?? throw Expired();
        var words = search?.Trim();
        var matching = file.Summary.Cars
            .Where(c => ownerId is null || c.OwnerId == ownerId)
            .Where(c => string.IsNullOrEmpty(words) || c.Name.Contains(words, StringComparison.CurrentCultureIgnoreCase)
                || (c.LicensePlate?.Contains(words, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .ToList();
        var page = new List<RestoreCarOffer>();
        var everyone = (await users.ListAsync(ct)).ToList();
        var known = everyone.Select(u => u.Id).ToHashSet();
        foreach (var car in matching.Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, MaxPage)))
            page.Add(await OfferAsync(file, car, everyone, known, ct));
        var owners = file.Summary.Cars.GroupBy(c => c.OwnerId).Select(g => new RestoreOwner(g.Key, g.First().OwnerName))
            .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new RestoreCarPage(file.Summary, matching.Count, page, owners);
    }

    /// <summary>Every account here, for the owner picker (by name).</summary>
    public async Task<IReadOnlyList<RestorePerson>> PeopleAsync(CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        return [.. (await users.ListAsync(ct)).Where(u => !u.IsDisabled).OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(u => new RestorePerson(u.Id, u.DisplayName, u.Email))];
    }

    private async Task<RestoreCarOffer> OfferAsync(IBackupCars file, BackupCarSummary car, IReadOnlyList<Domain.Users.User> everyone, IReadOnlySet<Guid> known,
        CancellationToken ct)
    {
        var data = await file.ReadRestoreAsync(car.Id, ct);
        var grants = data?.Grants ?? [];
        var gone = grants.Where(g => !known.Contains(g.GranteeId)).Select(g => g.Name ?? "").ToList();
        var count = grants.Count(g => known.Contains(g.GranteeId) && g.GranteeId != car.OwnerId);
        var here = await rules.FindHereAsync(car.Id, ct);
        if (here is { IsDeleted: true }) return new RestoreCarOffer(car, RestoreCarState.InTrash, null, null, 0, 0, 0, 0, count, gone, null);
        if (here is not null)
        {
            var owner = everyone.FirstOrDefault(u => u.Id == here.OwnerId);
            // In other units: still here, and the review says why it cannot come (nothing is converted).
            if (here.Units != car.Units) return new RestoreCarOffer(car, RestoreCarState.Here, here.OwnerId, owner?.DisplayName, 0, 0, 0, 0, count, gone, null);
            var full = await file.ReadCarAsync(car.Id, ct);
            var compared = full is null ? null : await rules.CompareAsync(full, here, ct, byId: true);
            var tyres = await rules.CompareTyresAsync(file, car.Id, here, ct, byId: true);
            return new RestoreCarOffer(car, RestoreCarState.Here, here.OwnerId, owner?.DisplayName,
                compared?.Refuelings.New.Count ?? 0, compared?.Expenses.New.Count ?? 0, compared?.Schedules.New.Count ?? 0,
                tyres.Sets.Count(s => s.Offer.State == ImportSetState.New), count, gone, null);
        }
        // A disabled account counts as none, as the run and the owner picker count it.
        if (everyone.Any(u => u.Id == car.OwnerId && !u.IsDisabled)) return new RestoreCarOffer(car, RestoreCarState.Gone, null, null, 0, 0, 0, 0, count, gone, null);
        var same = car.OwnerEmail is { } email ? everyone.FirstOrDefault(u => !u.IsDisabled && string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)) : null;
        return new RestoreCarOffer(car, RestoreCarState.OwnerGone, null, null, 0, 0, 0, 0, count, gone, same?.Id);
    }

    public Task<IReadOnlyList<ImportCarReview>> ReviewAsync(string token, IReadOnlyList<ImportCarChoice> choices, CancellationToken ct) =>
        runs.ReviewAsync(token, [.. choices.Select(BackupImportRules.ForRestore)], starting: false, ct, restore: true);

    public Task<ImportEntryPage> EntriesAsync(string token, Guid carId, string kind, bool differing, int skip, int take, CancellationToken ct, ImportTyreChoice? tyres = null) =>
        runs.EntriesAsync(token, carId, carId, kind, differing, skip, take, ct, tyres ?? new ImportTyreChoice(null), restore: true);

    public async Task<ImportRunView> StartAsync(string token, IReadOnlyList<ImportCarChoice> choices, CancellationToken ct)
    {
        foreach (var choice in choices.Where(c => c.OwnerId is not null))
            if (await users.FindByIdAsync(choice.OwnerId!.Value, ct) is not { IsDisabled: false })
                throw new NotFoundException("user.notFound", $"User {choice.OwnerId} does not exist.", new { Id = choice.OwnerId });
        return await runs.StartAsync(token, [.. choices.Select(BackupImportRules.ForRestore)], ct, restore: true);
    }

    /// <summary>The administrator's restore that goes or waits, else their latest one.</summary>
    public async Task<ImportRunView?> MineAsync(CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        return await runs.MineAsync(ct, ImportRunKind.Restore);
    }
}
