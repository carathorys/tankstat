using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Imports;

namespace Tankstat.Api.GraphQL;

/// <summary>A car of a restore point on the Cars step of <i>Restore cars</i> (#340); see <see cref="RestoreCarOffer"/>.</summary>
public sealed record RestoreCarOfferInfo(
    BackupCarInfo Car, RestoreCarState State, Guid? OwnerIdHere, string? OwnerNameHere, int MissingRefuelings, int MissingExpenses, int MissingSchedules, int MissingTyreSets,
    int Grants, IReadOnlyList<string> GoneGrantees, Guid? SameEmailUserId);

public sealed record RestoreOwnerInfo(Guid Id, string? Name);

/// <summary>A page of a restore point's cars, how many the search and the owner filter leave, and the backup's owners for the filter.</summary>
public sealed record RestoreCarPageInfo(int Total, IReadOnlyList<RestoreCarOfferInfo> Cars, IReadOnlyList<RestoreOwnerInfo> Owners);

/// <summary>A person here the owner picker offers: every account, with its e-mail (administrators only).</summary>
public sealed record RestorePersonInfo(Guid Id, string Name, string Email);

/// <param name="Token">From <c>openCarRestore</c>, once the restore point is opened.</param>
public sealed record StartCarRestoreInput(string Token, IReadOnlyList<ImportCarChoiceInput> Cars);

/// <param name="Passphrase">The passphrase the backup's key was made from; used for this try and kept nowhere.</param>
/// <param name="Identity">Or the identity (<c>AGE-SECRET-KEY-1...</c>); used for this try and kept nowhere.</param>
public sealed record CarRestoreKeyInput(string Token, string? Passphrase, string? Identity);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class CarRestoreQueries
{
    /// <summary>How the opened restore point stands (administrators). Asked again while it is being opened.</summary>
    public async Task<BackupImportInfo> GetCarRestore(string token, [Service] CarRestoreService restores, CancellationToken ct) =>
        BackupImportInfo.From(await restores.StatusAsync(token, ct));

    /// <summary>
    /// A page of the restore point's cars (by name, at most 50), filtered by <c>search</c> (name or plate) and the backup's owner, each with
    /// its state here: still here (and what it misses), not here any more, not here and its owner without an account, or in the trash here.
    /// </summary>
    public async Task<RestoreCarPageInfo> GetRestoreCars(string token, string? search, Guid? ownerId, int skip, int take, [Service] CarRestoreService restores, CancellationToken ct)
    {
        var page = await restores.CarsAsync(token, search, ownerId, skip, take, ct);
        return new RestoreCarPageInfo(page.Total,
            [.. page.Cars.Select(c => new RestoreCarOfferInfo(BackupCarInfo.From(c.Car), c.State, c.OwnerIdHere, c.OwnerNameHere, c.MissingRefuelings, c.MissingExpenses,
                c.MissingSchedules, c.MissingTyreSets, c.Grants, c.GoneGrantees, c.SameEmailUserId))],
            [.. page.Owners.Select(o => new RestoreOwnerInfo(o.Id, o.Name))]);
    }

    /// <summary>Every account here, for the owner picker of a car whose owner has no account (administrators).</summary>
    public async Task<IReadOnlyList<RestorePersonInfo>> GetRestorePeople([Service] CarRestoreService restores, CancellationToken ct) =>
        [.. (await restores.PeopleAsync(ct)).Select(p => new RestorePersonInfo(p.Id, p.Name, p.Email))];

    /// <summary>What restoring the chosen cars would do, as far as it can be foreseen. Nothing is saved.</summary>
    public async Task<IReadOnlyList<ImportCarReviewInfo>> GetRestoreReview(string token, IReadOnlyList<ImportCarChoiceInput> cars, [Service] CarRestoreService restores, CancellationToken ct) =>
        [.. (await restores.ReviewAsync(token, [.. cars.Select(c => c.ToChoice())], ct)).Select(ImportCarReviewInfo.From)];

    /// <summary>A page of the Choose dialog for a car that is still here (#338's, matched by id).</summary>
    public async Task<ImportEntryPageInfo> GetRestoreEntries(
        string token, Guid carId, string kind, bool differing, int skip, int take, [Service] CarRestoreService restores, CancellationToken ct, ImportTyreChoiceInput? tyres = null)
    {
        var page = await restores.EntriesAsync(token, carId, kind, differing, skip, take, ct, tyres?.ToChoice());
        return new ImportEntryPageInfo(page.Total, [.. page.Items.Select(e => new ImportEntryInfo(e.Id, e.Date, e.Odometer, e.Title, e.Amount, e.Currency, e.Volume,
            [.. e.Differences.Select(d => new ImportFieldDifferenceInfo(d.Field, d.Backup, d.Target))]))]);
    }

    /// <summary>The administrator's restore that goes or waits, else their latest one that is still kept; null without one.</summary>
    public async Task<ImportRunInfo?> GetMyRestoreRun([Service] CarRestoreService restores, [Service] IBackupImportRunner runner, [Service] AccessService access,
        [Service] IOptionsSnapshot<Tankstat.Application.Runs.RunOptions> runs, CancellationToken ct) =>
        await restores.MineAsync(ct) is { } v ? ImportRunInfo.From(v, (await access.RequirePrincipalAsync(ct)).Id, v.FileKept ? runner.SessionOf(v.Id) : null, runs.Value.LimitMinutes) : null;
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class CarRestoreMutations
{
    /// <summary>Opens a restore point of the Backups page (by its file name) to restore cars from: never copied, never deleted.</summary>
    public async Task<BackupImportInfo> OpenCarRestore(string file, [Service] CarRestoreService restores, CancellationToken ct) =>
        BackupImportInfo.From(await restores.OpenAsync(file, ct));

    /// <summary>Tries a passphrase or an identity on the encrypted restore point; no limit on tries.</summary>
    public async Task<BackupImportInfo> SubmitCarRestoreKey(CarRestoreKeyInput input, [Service] CarRestoreService restores, CancellationToken ct) =>
        BackupImportInfo.From(await restores.SubmitKeyAsync(input.Token, input.Passphrase, input.Identity, ct));

    /// <summary>Queues the restore of the chosen cars, in the imports' queue: it starts by itself. Ask <c>importRun</c> how it stands.</summary>
    public async Task<ImportRunInfo> StartCarRestore(StartCarRestoreInput input, [Service] CarRestoreService restores, [Service] AccessService access,
        [Service] IOptionsSnapshot<Tankstat.Application.Runs.RunOptions> runs, CancellationToken ct)
    {
        var v = await restores.StartAsync(input.Token, [.. input.Cars.Select(c => c.ToChoice())], ct);
        return ImportRunInfo.From(v, (await access.RequirePrincipalAsync(ct)).Id, input.Token, runs.Value.LimitMinutes);
    }

    /// <summary>Closes the opened restore point (the file stays in the backups folder).</summary>
    public async Task<bool> EndCarRestore(string token, [Service] CarRestoreService restores, CancellationToken ct)
    {
        await restores.EndAsync(token, ct);
        return true;
    }
}
