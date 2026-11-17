using Microsoft.Extensions.Logging;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Runs;
using Tankstat.Application.Users;
using Tankstat.Domain;
using Tankstat.Domain.Imports;

namespace Tankstat.Application.Imports;

/// <summary>
/// Importing cars from an opened backup or export (#337), as the person drives it: the cars the file offers and their targets, the
/// review, the start, how the run stands, its cancel and its report. The import itself is <see cref="BackupImportWork"/>, run by the
/// server's queue (<see cref="IBackupImportRunner"/>): one run at a time on the instance, the others wait in line.
/// </summary>
public sealed class BackupImportRunService(
    AccessService access, BackupImportService imports, IBackupImportSessions sessions, IImportTargetReader targets, BackupImportRules rules, BackupMediaWork media,
    IImportRunRepository runs, IBackupImportRunner runner, IRunScope scope, IUserRepository users, ImportRunNotifier notifier, TimeProvider clock,
    ILogger<BackupImportRunService> logger)
{
    public const string AlreadyRunningKey = "import.alreadyRunning";
    public const string RestoreAlreadyRunningKey = "restore.alreadyRunning";

    /// <summary>One start at a time per person in this process: the look for a run of theirs and the new run's row are one step (a second tab, a double press).</summary>
    private static readonly StripedLocks Starting = new();

    /// <summary>The file's cars and the person's own cars to add one to, with the app's suggestion: the same plate, else the same name, in the same units.</summary>
    public async Task<ImportCarOffers> OffersAsync(string token, CancellationToken ct)
    {
        var (me, file) = await FileAsync(token, ct);
        var owned = await targets.OwnedAsync(me.Id, ct);
        static string? Plate(string? plate) => string.IsNullOrWhiteSpace(plate) ? null : new string([.. plate.Where(char.IsLetterOrDigit)]).ToUpperInvariant();
        var offers = file.Summary.Cars.Select(car =>
        {
            var usable = owned.Where(v => v.Units == car.Units).ToList();
            var match = usable.FirstOrDefault(v => Plate(car.LicensePlate) is { } plate && Plate(v.LicensePlate) == plate)
                ?? usable.FirstOrDefault(v => string.Equals(v.Name.Trim(), car.Name.Trim(), StringComparison.CurrentCultureIgnoreCase));
            return new ImportCarOffer(car, car.OwnerId == me.Id, match?.Id);
        }).ToList();
        return new ImportCarOffers(file.Summary, offers, [.. owned.Select(v => new ImportOwnCar(v.Id, v.Name, v.LicensePlate, v.FuelType, v.Units))]);
    }

    /// <summary>What importing the chosen cars would do, as far as it can be foreseen. Nothing is saved, and a rule's refusal is only met by the run.</summary>
    public Task<IReadOnlyList<ImportCarReview>> ReviewAsync(string token, IReadOnlyList<ImportCarChoice> choices, CancellationToken ct) => ReviewAsync(token, choices, starting: false, ct);

    /// <param name="restore">
    /// A partial restore (#340; administrators, a restore point): each car's target is the car of its id here, entries match by id alone,
    /// and the review says whose the car will be and who gets access back.
    /// </param>
    internal async Task<IReadOnlyList<ImportCarReview>> ReviewAsync(string token, IReadOnlyList<ImportCarChoice> choices, bool starting, CancellationToken ct, bool restore = false)
    {
        var (me, file) = await FileAsync(token, ct, restore);
        BackupImportRules.RequireSound(choices, file.Summary);
        var reviews = new List<ImportCarReview>();
        foreach (var choice in choices)
        {
            var car = await file.ReadCarAsync(choice.CarId, ct) ?? throw new DomainException("import.unknownCar", "The file has no such car.");
            var target = restore ? await rules.RestoreTargetAsync(car.Car, ct) : choice.TargetVehicleId is { } id ? await rules.TargetAsync(me.Id, car.Car, id, ct) : null;
            var compared = target is null ? BackupImportRules.Whole(car) : await rules.CompareAsync(car, target, ct, byId: restore);
            // The tyres always (#339): the Cars step offers each set from this answer.
            var tyres = await rules.CompareTyresAsync(file, car.Car.Id, target, ct, byId: restore);
            var (restoreReview, people) = restore ? await RestoreReviewAsync(file, car.Car, target, choice, me, ct) : (null, null);
            var tyreChoice = choice.Kinds?.Tyres;
            BackupImportRules.RequireSound(car.Car, compared, choice.Kinds, tyres.Coming(tyreChoice));
            // A Replace all asks for the target's name, typed (#338): checked when the start is accepted, never again when a waiting
            // run's turn comes, so a car renamed meanwhile does not end the run.
            if (starting && target is not null && (choice.Kinds ?? ImportCarKinds.Missing).All.Any(k => k.Mode == ImportKindMode.ReplaceAll)
                && !string.Equals(choice.ConfirmedName?.Trim(), target.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new DomainException(BackupImportRules.ReplaceNotConfirmedKey, "Type the car's name to confirm what is deleted.", new { Car = target.Name });
            var tyreExpenses = tyres.ExpensesOf(tyreChoice);
            var work = BackupImportRules.Work(compared, choice.Kinds, tyreExpenses);
            // The media (#339): where each of the backup's logs will be (Guid.Empty: it comes as a new one), so its photos are counted.
            var mediaChoice = choice.Kinds?.Media;
            var adds = (work.Refuelings.Adds.Select(e => e.Id).ToHashSet(), work.Expenses.Adds.Select(e => e.Id).Concat(tyreExpenses).ToHashSet());
            Guid? LogOf(Domain.Photos.LogType type, Guid log) => type == Domain.Photos.LogType.Refueling
                ? adds.Item1.Contains(log) ? Guid.Empty : work.Refuelings.Counterparts.TryGetValue(log, out var fuel) ? fuel : null
                : adds.Item2.Contains(log) ? Guid.Empty : work.Expenses.Counterparts.TryGetValue(log, out var cost) ? cost : null;
            var mediaReview = await media.ReviewAsync(file, car.Car.Id, target, me, LogOf, ct, people);
            var photosComing = mediaChoice?.Photos == true;
            var fuelLost = target is not null && work.Refuelings.Purge ? await media.PhotosLostAsync(file, car.Car.Id, target, Domain.Photos.LogType.Refueling, photosComing, ct) : 0;
            var expensesLost = target is not null && work.Expenses.Purge ? await media.PhotosLostAsync(file, car.Car.Id, target, Domain.Photos.LogType.Expense, photosComing, ct) : 0;
            var mediaSteps = mediaChoice is null ? 0 : (mediaChoice.Photos ? mediaReview.PhotosNew + mediaReview.PhotosHere : 0) + ((mediaChoice.Picture ?? !mediaReview.TargetPicture) && mediaReview.Picture ? 1 : 0)
                + (mediaChoice.Charts ? mediaReview.ChartsNew + mediaReview.ChartsHere : 0)
                + ((mediaChoice.Dashboard ?? !mediaReview.TargetDashboard) && mediaReview.Dashboard ? 1 : 0)
                + ((mediaChoice.OwnDashboard ?? !mediaReview.TargetOwnDashboard) && mediaReview.OwnDashboard ? 1 : 0);
            var (fuel, expenses, schedules) = (BackupImportRules.Left(compared.Refuelings, work.Refuelings), BackupImportRules.Left(compared.Expenses, work.Expenses), BackupImportRules.Left(compared.Schedules, work.Schedules));
            reviews.Add(new ImportCarReview(car.Car.Id, car.Car.Name, car.Car.OwnerName, car.Car.OwnerId == me.Id, target?.Id, target?.Name,
                work.Refuelings.Adds.Count, work.Expenses.Adds.Count, work.Schedules.Adds.Count,
                fuel.Same, expenses.Same, schedules.Same, fuel.Differ + expenses.Differ + schedules.Differ, fuel.InTrash + expenses.InTrash,
                [
                    KindReview(BackupImportRules.Refueling, compared.Refuelings, work.Refuelings, fuelLost),
                    KindReview(BackupImportRules.Expense, compared.Expenses, work.Expenses, expensesLost),
                    KindReview(BackupImportRules.Recurring, compared.Schedules, work.Schedules),
                ], work.Links, work.LinksLost,
                new ImportTyreReview([.. tyres.Sets.Select(s => s.Offer)], tyres.NoSetLines.Count,
                    tyres.NoSetLines.Where(l => tyres.Backup.ExpenseOf(l) is { IsDeleted: false }).Select(l => l.ExpenseId).Distinct().Count(), tyreExpenses.Count),
                TyreSteps: tyres.Chosen(tyreChoice).Count + (tyreChoice is { NoSetLines: true } && tyres.NoSetLines.Count > 0 ? 1 : 0),
                Media: mediaReview, MediaSteps: mediaSteps, Restore: restoreReview));
        }
        return reviews;
    }

    /// <summary>
    /// "Every new set but these" (the page's way) turned into the sets new when the start is accepted, by id: a set the car's history
    /// refuses by the time the run reaches it is then that set's refusal in the report, never left out without a word (#339). A set the
    /// review already showed as unable to come stays out, as it did.
    /// </summary>
    private static ImportCarKinds? WithSetsNamed(ImportCarKinds? kinds, ImportTyreReview? tyres) =>
        kinds?.Tyres is { Sets: null } choice && tyres is not null
            ? kinds with { Tyres = choice with { Sets = [.. tyres.Sets.Where(s => s.State == ImportSetState.New && choice.Wants(s.Id)).Select(s => s.Id)], Except = null } }
            : kinds;

    /// <summary>
    /// A restore's car (#340): whose it will be (today's owner when it is here; the backup's when that person has an account, else the one
    /// picked, the administrator by default), who gets access back with a gone car and who had access but has no account, and who may have
    /// their own widgets and dashboard back (an account, and the car in sight afterwards).
    /// </summary>
    private async Task<(RestoreCarReview, RestoreMedia)> RestoreReviewAsync(IBackupCars file, BackupCarSummary car, Domain.Vehicles.Vehicle? target, ImportCarChoice choice,
        Principal me, CancellationToken ct)
    {
        var data = await file.ReadRestoreAsync(car.Id, ct) ?? new BackupRestoreData(new Dictionary<RestoreTable, IReadOnlyDictionary<Guid, RestoreStamp>>(), []);
        if (target is not null)
        {
            var owner = await users.FindByIdAsync(target.OwnerId, ct);
            return (new RestoreCarReview(RestoreCarState.Here, target.OwnerId, owner?.DisplayName, [], []),
                new RestoreMedia(async (id, token) => await users.FindByIdAsync(id, token) is { } u && await access.LogLevelOfAsync(u.Id, u.IsAdmin, target, token) >= Domain.Access.AccessLevel.View));
        }
        var backupOwner = await users.FindByIdAsync(car.OwnerId, ct);
        var state = backupOwner is null ? RestoreCarState.OwnerGone : RestoreCarState.Gone;
        var ownerId = backupOwner?.Id ?? choice.OwnerId ?? me.Id;
        var chosen = backupOwner ?? await users.FindByIdAsync(ownerId, ct) ?? throw new NotFoundException("user.notFound", $"User {ownerId} does not exist.", new { Id = ownerId });
        var (back, gone) = (new List<string>(), new List<string>());
        var granted = new HashSet<Guid>();
        foreach (var grant in data.Grants.Where(g => g.GranteeId != chosen.Id))
            if (await users.FindByIdAsync(grant.GranteeId, ct) is { } user)
            {
                back.Add(user.DisplayName);
                granted.Add(user.Id);
            }
            else gone.Add(grant.Name ?? "");
        // The car as it will be (not saved): its owner, and its grants as they come back.
        var standIn = Domain.Vehicles.Vehicle.Create(chosen.Id, car.Name, null, car.FuelType, car.Units, car.Id);
        return (new RestoreCarReview(state, chosen.Id, chosen.DisplayName, back, gone),
            new RestoreMedia(async (id, token) => await users.FindByIdAsync(id, token) is { } u
                && (granted.Contains(u.Id) || await access.LogLevelOfAsync(u.Id, u.IsAdmin, standIn, token) >= Domain.Access.AccessLevel.View)));
    }

    private static ImportKindReview KindReview<T>(string kind, ImportKindComparison<T> compared, ImportKindWork<T> work, int photosLost = 0) where T : IBackupEntry => new(
        kind, work.Mode, compared.Backup.Count, compared.New.Count, compared.Differ.Count, compared.Same.Count, compared.InTrash, work.Adds.Count, work.Takes.Count,
        compared.TargetIds.Count, compared.TargetTrashed, compared.WithPhotos, compared.OnTyreLines, photosLost);

    /// <summary>
    /// A page of one section of the <i>Choose…</i> dialog (#338): the entries of a kind that are only in the backup, or that differ from
    /// the target's (with both sides of each field that does), in the backup's order, at most 200 at a time.
    /// </summary>
    /// <param name="tyres">The car's tyre choice (#339): the expenses its lines hold come with the tyres, so they are not offered here.</param>
    /// <param name="restore">A partial restore's (#340): the car of the same id is the target, and entries match by id alone.</param>
    public async Task<ImportEntryPage> EntriesAsync(string token, Guid carId, Guid targetVehicleId, string kind, bool differing, int skip, int take, CancellationToken ct,
        ImportTyreChoice? tyres = null, bool restore = false)
    {
        var (me, file) = await FileAsync(token, ct, restore);
        var car = await file.ReadCarAsync(carId, ct) ?? throw new DomainException("import.unknownCar", "The file has no such car.");
        var target = restore
            ? await rules.RestoreTargetAsync(car.Car, ct) ?? throw new NotFoundException("vehicle.notFound", $"Vehicle {carId} does not exist.", new { Id = carId })
            : await rules.TargetAsync(me.Id, car.Car, targetVehicleId, ct);
        var compared = await rules.CompareAsync(car, target, ct, byId: restore);
        if (tyres is not null && (await rules.CompareTyresAsync(file, carId, target, ct, byId: restore)).ExpensesOf(tyres) is { Count: > 0 } held)
            compared = compared with
            {
                Expenses = compared.Expenses with { New = [.. compared.Expenses.New.Where(e => !held.Contains(e.Id))], Backup = [.. compared.Expenses.Backup.Where(e => !held.Contains(e.Id))] },
            };
        (skip, take) = (Math.Max(0, skip), Math.Clamp(take, 1, 200));
        return kind switch
        {
            BackupImportRules.Refueling => Page(compared.Refuelings, r => new ImportEntryView(r.Id, r.Values.Date, r.Values.Odometer, null, r.Values.TotalCost, r.Values.Currency, r.Values.Volume, [])),
            BackupImportRules.Expense => Page(compared.Expenses, e => new ImportEntryView(e.Id, e.Values.Date, e.Values.Odometer, e.Values.Title, e.Values.Amount, e.Values.Currency, null, [])),
            BackupImportRules.Recurring => Page(compared.Schedules, r => new ImportEntryView(r.Id, r.Values.LastDoneDate, r.Values.LastDoneOdometer, r.Values.Title, null, null, null, [])),
            _ => throw new DomainException("import.unknownKind", "There is no such kind of entry."),
        };

        ImportEntryPage Page<T>(ImportKindComparison<T> of, Func<T, ImportEntryView> view) where T : IBackupEntry => differing
            ? new ImportEntryPage(of.Differ.Count, [.. of.Differ.Skip(skip).Take(take).Select(d => view(d.Entry) with
            {
                Differences = [.. d.Differences.Select(f => new ImportFieldDifference(f.Field, Text(f.Backup), Text(f.Target)))],
            })])
            : new ImportEntryPage(of.New.Count, [.. of.New.Skip(skip).Take(take).Select(view)]);

        static string? Text(object? value) => value switch
        {
            null => null,
            DateOnly day => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            bool yes => yes ? "true" : "false",
            // As GraphQL names it (TIME, IMPORT_THE_REST): the page words it by that name. Before the numbers: an enum is formattable too.
            Enum name => string.Concat(name.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToUpperInvariant(),
            IFormattable number => number.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    /// <summary>
    /// Queues the import and answers its run: it starts by itself when no other run is open, else when its turn comes. One run per
    /// person at a time. Where the database cannot give a run its one transaction, nothing is queued (<see cref="IBackupImportRunner.UnavailableKey"/>).
    /// </summary>
    /// <param name="restore">A partial restore (#340): its own kind of run, whose cars keep their ids and whose gone cars' owners are decided now.</param>
    public async Task<ImportRunView> StartAsync(string token, IReadOnlyList<ImportCarChoice> choices, CancellationToken ct, bool restore = false)
    {
        var (me, file) = await FileAsync(token, ct, restore);
        if (runner.UnavailableKey is { } unavailable) throw new DomainException(unavailable, "Importing from a backup is not available on this server's database.");
        var gate = Starting.For(me.Id);
        await gate.WaitAsync(ct);
        try
        {
            if (await runs.LatestOfAsync(me.Id, ct) is { IsActive: true })
                throw restore ? new DomainException(RestoreAlreadyRunningKey, "A restore of yours is running or waiting already.")
                    : new DomainException(AlreadyRunningKey, "An import of yours is running or waiting already.");
            var reviews = await ReviewAsync(token, choices, starting: true, ct, restore); // every refusal that can be foreseen, before anything is queued
            var plan = new ImportRunPlan([.. choices.Zip(reviews, (choice, review) => new ImportCarPlan(
                choice.CarId, review.Name, review.TargetVehicleId, review.TargetName, choice.Policy, review.Kinds.Sum(k => k.Adds + k.Takes) + review.TyreSteps + review.MediaSteps,
                WithSetsNamed(choice.Kinds, review.Tyres), review.Restore is { State: not RestoreCarState.Here } r ? r.OwnerId : null))], restore, restore ? file.Summary.CreatedAt : null);
            var run = ImportRun.Queue(Guid.NewGuid(), restore ? ImportRunKind.Restore : ImportRunKind.Import, me.Id, runner.Server, ImportRunJson.Write(plan), plan.Cars.Count,
                plan.Cars.Sum(c => c.Entries), clock.GetUtcNow());
            // The file first: the queue ends a waiting run of this server that it holds no file for, so the row must never be there without it.
            runner.Enqueue(run.Id, me.Id, token);
            try
            {
                await runs.AddAsync(run, ct);
            }
            catch
            {
                runner.Dropped(run.Id);
                throw;
            }
            runner.Wake();
            logger.LogInformation("User {UserId} queued {Kind} run {RunId}: {Cars} cars, {Entries} entries", me.Id, restore ? "restore" : "import", run.Id, run.Cars, run.Entries);
            return await ViewOfAsync(run, me, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>A run: its starter's always (the report with it, while it is kept), an administrator's while it goes or waits. Anyone else finds nothing.</summary>
    public async Task<ImportRunView?> FindAsync(Guid id, CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        if (await runs.FindAsync(id, ct) is not { } run) return null;
        if (run.StartedById != me.Id && !(IsAdministrator(me) && run.IsActive)) return null;
        return await ViewOfAsync(run, me, ct);
    }

    /// <summary>The person's run that goes or waits, else their latest one (its report is shown until another file is chosen); null when they have none kept.</summary>
    /// <param name="kind">The page's own (#340): the Import page never shows a restore, nor the Restore cars page an import.</param>
    public async Task<ImportRunView?> MineAsync(CancellationToken ct, ImportRunKind kind = ImportRunKind.Import)
    {
        var me = await access.RequirePrincipalAsync(ct);
        return await runs.LatestOfAsync(me.Id, ct, kind) is { } run ? await ViewOfAsync(run, me, ct) : null;
    }

    /// <summary>For administrators: every run that goes or waits, the oldest first, without their reports.</summary>
    public async Task<IReadOnlyList<ImportRunView>> ActiveAsync(CancellationToken ct)
    {
        var me = await access.RequireAdminAsync(ct);
        var active = await runs.ListActiveAsync(ct);
        var views = new List<ImportRunView>();
        foreach (var run in active) views.Add(await ViewOfAsync(run, me, ct, active)); // the line is read once, not again for every run in it
        return views;
    }

    /// <summary>
    /// Cancels a run: its starter or any administrator. A waiting one ends at once; a running one stops between its entries and rolls
    /// back whole. False when it is over already (or is nobody's to cancel here).
    /// </summary>
    public async Task<bool> CancelAsync(Guid id, CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        if (await runs.FindAsync(id, ct) is not { IsActive: true } run) return false;
        if (run.StartedById != me.Id && !IsAdministrator(me)) return false;
        logger.LogInformation("User {UserId} cancels import run {RunId} of user {StarterId}", me.Id, id, run.StartedById);
        // Only while it still waits: one given its turn since the look above is running, and is told below like any running one.
        if (run.State == ImportRunState.Waiting && await runs.FailAsync(id, ImportRunFailure.Cancelled, me.Id, clock.GetUtcNow(), ct, waitingOnly: true))
        {
            runner.Dropped(id);
            await notifier.NotFinishedAsync(run, ImportRunFailure.Cancelled, me.Id, ct);
            runner.Wake();
            return true;
        }
        // Running (or given its turn just now): the run is told, and its end is written by the server that runs it.
        runner.Cancelling(id, me.Id);
        // The run's mark may not exist yet (its turn came a moment ago): the runner keeps the request and the run honours it first.
        return await scope.CancelAsync(id, me, ct) || await runs.FindAsync(id, ct) is { State: ImportRunState.Running };
    }

    private async Task<(Principal Me, IBackupCars File)> FileAsync(string token, CancellationToken ct, bool restore = false)
    {
        var me = restore ? await access.RequireAdminAsync(ct) : await imports.RequireImporterAsync(ct);
        // A restore keeps ids, owners and creators: only from a restore point of the server's own (the owner's decision on #334), never an upload.
        if (restore && sessions.Find(me.Id, token) is { Origin: not BackupFileOrigin.RestorePoint }) throw new DomainException("import.expired", "This import has expired. Choose the file again.");
        return (me, sessions.Cars(me.Id, token) ?? throw new DomainException("import.expired", "This import has expired. Choose the file again."));
    }

    private static bool IsAdministrator(Principal p) => p.IsAdmin && !p.IsAnonymous;

    private async Task<ImportRunView> ViewOfAsync(ImportRun run, Principal viewer, CancellationToken ct, IReadOnlyList<ImportRun>? active = null)
    {
        var mine = run.StartedById == viewer.Id;
        var ahead = run.State == ImportRunState.Waiting ? (active ?? await runs.ListActiveAsync(ct)).Count(r => r.Id != run.Id && (r.State == ImportRunState.Running || r.QueuedAt < run.QueuedAt)) : 0;
        var plan = ImportRunJson.Read<ImportRunPlan>(run.Plan) ?? new ImportRunPlan([]);
        var progress = runner.ProgressOf(run.Id) ?? new ImportRunProgress(Math.Min(run.DoneCars + 1, run.Cars), run.Cars, run.DoneEntries, run.Entries);
        return new ImportRunView(
            run.Id, run.Kind, run.StartedById, mine ? viewer.DisplayName : (await users.FindByIdAsync(run.StartedById, ct))?.DisplayName, run.State, run.Failure, run.CancelledById,
            run.QueuedAt, run.StartedAt, run.EndedAt, ahead, progress, plan, mine ? ImportRunJson.Read<ImportRunReport>(run.Report) : null,
            mine && runner.SessionOf(run.Id) is not null);
    }
}
