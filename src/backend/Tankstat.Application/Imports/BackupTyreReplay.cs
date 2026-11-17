using Tankstat.Application.Expenses;
using Tankstat.Application.Tyres;
using Tankstat.Domain.Tyres;

namespace Tankstat.Application.Imports;

/// <summary>
/// What a set's replay made: the new set's id, its counts, the new ids by the backup's (installations, lines, tyres, expenses), the expenses
/// it made, and the amounts it left off because the same expense was among the car's already and a fee cannot be tied to one.
/// </summary>
public sealed record TyreReplayed(Guid SetId, int Installations, int Lines, int Tyres, IReadOnlyDictionary<Guid, Guid> Ids, int ExpensesMade, int AmountsHere = 0);

/// <summary>
/// The car's expenses no tyre log line holds (#339: never a cost twice): an expenses-only import brought a tyre cost as a plain expense,
/// and a later tyres import finds it here, by its day, title (without case) and amount, instead of making it again.
/// </summary>
public sealed class ImportExpensePool(IEnumerable<(Guid Id, DateOnly Date, string Title, decimal? Amount)> free)
{
    private List<(Guid Id, DateOnly Date, string Title, decimal? Amount)> _free = [.. free.Select(f => (f.Id, f.Date, f.Title.Trim().ToLowerInvariant(), f.Amount))];

    public Guid? Take(ExpenseInput values)
    {
        var title = values.Title.Trim().ToLowerInvariant();
        var at = _free.FindIndex(f => f.Date == values.Date && f.Title == title && f.Amount == values.Amount);
        if (at < 0) return null;
        var id = _free[at].Id;
        _free.RemoveAt(at);
        return id;
    }

    /// <summary>As it is now, to put back when what took from it is rolled back (a set refused in its savepoint).</summary>
    public object Mark() => _free.ToList();

    public void Restore(object mark) => _free = [.. (List<(Guid, DateOnly, string, decimal?)>)mark];
}

/// <summary>
/// A backup's tyre history replayed into a car through the tyre and expense services (#339), never copied: every rule of today applies,
/// every row gets a new id, and the devices follow by the normal sync. A set goes in this order, which the services need:
/// <list type="number">
/// <item>the set into storage, with the rough figure its single tyres carry (they copy the set's at the break-up) and its price;</item>
/// <item>its times on the car, oldest first, then each swap line's note, reading and fee;</item>
/// <item>by day: the break-up, the lines (a group of lines sharing one expense in one call), the replacements (a tyre's lines dated before
/// its replacement first, as the service refuses them after);</item>
/// <item>the single tyres' places put right where the break-up and the pairs gave another corner or other days;</item>
/// <item>the fees' expenses put right (a fee carries no note or discount), the set's rough figure as it is now, and its retirement.</item>
/// </list>
/// What cannot be reproduced is accepted: the swap reminder starts afresh, who logged a row and versions are the importer's.
/// </summary>
public sealed class BackupTyreReplay(TyreService tyres, ExpenseService expenses)
{
    private sealed class Run(TyreBook backup, ImportExpensePool? pool)
    {
        public TyreBook Backup { get; } = backup;
        public ImportExpensePool? Pool { get; } = pool;
        public Dictionary<Guid, Guid> Ids { get; } = [];
        public List<(Guid NewId, ExpenseInput Values)> Fees { get; } = [];
        private readonly Dictionary<Guid, TyreFee?> _fees = [];
        public int Lines { get; set; }
        public int ExpensesMade { get; set; }
        public int AmountsHere { get; set; }

        public Guid New(Guid old)
        {
            if (!Ids.TryGetValue(old, out var made)) Ids[old] = made = Guid.NewGuid();
            return made;
        }

        public Guid? Map(Guid? old) => old is { } id ? New(id) : null;

        /// <summary>The live expense a line holds in the backup, as the service takes one.</summary>
        public ExpenseInput? ExpenseOf(TyreEntry line) =>
            Backup.ExpenseOf(line) is { IsDeleted: false } expense ? BackupCarShapes.Of(expense, []).Values : null;

        /// <summary>
        /// A line's amount as a fee: its expense's id the new one, kept to be put right afterwards. When the same expense is among the car's
        /// already, none: a fee always makes its expense, so the line comes without its amount and the report says it is there.
        /// </summary>
        public TyreFee? FeeOf(TyreEntry line)
        {
            if (ExpenseOf(line) is not { } values) return null;
            var old = line.ExpenseId!.Value;
            if (_fees.TryGetValue(old, out var known)) return known;
            if (Pool?.Take(values) is not null)
            {
                AmountsHere++;
                return _fees[old] = null;
            }
            var id = New(old);
            Fees.Add((id, values));
            ExpensesMade++;
            return _fees[old] = new TyreFee(values.Amount, values.Currency, values.Category, values.Title, id);
        }
    }

    /// <summary>The live rows of one set (or of no set) in the backup's book.</summary>
    private static IReadOnlyList<TyreEntry> LinesOf(TyreBook book, Guid? setId) => [.. book.Entries.Where(e => e.SetId == setId && book.IsLive(e))];

    public async Task<TyreReplayed> SetAsync(Guid vehicleId, TyreBook backup, TyreSet set, CancellationToken ct, ImportExpensePool? pool = null)
    {
        var run = new Run(backup, pool);
        var newSet = run.New(set.Id);
        var installations = backup.Installations.Where(i => i.SetId == set.Id && !i.IsDeleted).OrderBy(i => i.From).ToList();
        var lines = LinesOf(backup, set.Id);
        var singles = backup.SingleTyres.Where(t => t.SetId == set.Id && !t.IsDeleted).ToList();
        var places = backup.TyrePlaces.Where(p => !p.IsDeleted && singles.Any(t => t.Id == p.TyreId)).ToList();
        var replacements = singles.Where(t => singles.Any(o => o.ReplacedById == t.Id)).Select(t => t.Id).ToHashSet();
        var originals = singles.Where(t => !replacements.Contains(t.Id)).OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase).ToList();

        // 1. Into storage. The rough figure is the one the tyres copied at the break-up (a later change of the set's does not reach them).
        var (rough, roughUntil) = set.IsBrokenUp && originals.Count > 0 ? (originals[0].RoughDistance, originals[0].RoughUntil) : (set.RoughDistance, set.RoughUntil);
        var bought = lines.FirstOrDefault(l => l.Kind == TyreEntryKind.Bought && l.TyreId is null && run.ExpenseOf(l) is not null);
        var day = roughUntil ?? (installations.Count > 0 ? installations[0].From : set.Since);
        await tyres.AddSetAsync(new NewTyreSet(
            vehicleId, new TyreSetInput(set.Name, set.Season, set.Size, set.Since, set.MadeWeek, set.MadeYear, rough, set.UsualMonths?.ToList(), set.ReminderMonth),
            Fitted: false, day, Odometer: null, PairOneFront: null, Price: bought is null ? null : run.FeeOf(bought), BoughtOn: bought?.Day, BoughtEntryId: run.Map(bought?.Id)),
            ct, id: newSet);
        var done = new HashSet<Guid>();
        if (bought is not null)
        {
            done.Add(bought.Id);
            run.Lines++;
            if (bought.Note is not null || bought.Odometer is not null)
                await tyres.UpdateEntryAsync(run.New(bought.Id), new TyreEntryInput(bought.Kind, newSet, bought.Day, bought.Odometer, bought.Note, run.FeeOf(bought)), ct);
        }

        // 2. Its times on the car (the line of one starting the day the set came off is a change of positions, as the service sees it).
        foreach (var installation in installations)
        {
            var line = lines.FirstOrDefault(l => l.Id == installation.Id && l.IsSwapLine);
            await tyres.AddInstallationAsync(newSet, new TyreInstallationInput(installation.From, installation.Until, installation.PairOneFront, line?.Odometer, null), ct,
                id: run.New(installation.Id));
            if (line is null) continue;
            done.Add(line.Id);
            run.Lines++;
            var fee = run.FeeOf(line);
            if (line.Note is not null || fee is not null)
                await tyres.UpdateEntryAsync(run.New(line.Id), new TyreEntryInput(line.Kind, newSet, line.Day, line.Odometer, line.Note, fee), ct);
        }

        // 3. By day: the break-up, the lines, the replacements.
        var replaced = singles.Where(t => t.ReplacedById is not null && t.RetiredOn is not null).GroupBy(t => (Day: t.RetiredOn!.Value, Why: t.RetiredWhy ?? TyreRetiredWhy.Damaged)).ToList();
        var boughtFor = lines.Where(l => l.Kind == TyreEntryKind.Bought && l.TyreId is { } t && replacements.Contains(t)).ToList();
        foreach (var l in boughtFor) done.Add(l.Id);
        var groups = lines.Where(l => !done.Contains(l.Id)).GroupBy(l => l.GroupId ?? l.Id).Select(g => g.OrderBy(l => l.Id == g.Key ? 0 : 1).ToList()).ToList();
        var days = groups.Select(g => g[0].Day).Concat(replaced.Select(r => r.Key.Day)).Append(set.BrokenUpOn ?? DateOnly.MaxValue).Where(d => d != DateOnly.MaxValue).Distinct().Order();
        var made = new HashSet<Guid>(originals.Select(t => t.Id)); // the tyres there are once the set is broken up
        // The break-up gives its corners to the time on the car that is open, else the last one: all of them are in by then.
        var known = (installations.FirstOrDefault(i => i.Until is null) ?? installations.LastOrDefault())?.Id;
        foreach (var on in days)
        {
            if (set.BrokenUpOn == on)
                await tyres.BreakUpAsync(new TyreBreakUpInput(newSet, on, [.. originals.Select(t => new NewTyre(run.New(t.Id), t.Label, CornerOn(places, t.Id, known)))]), ct);
            // The lines of tyres there before the day's replacements, then the replacements, then the lines of the tyres they brought.
            var today = groups.Where(g => g[0].Day == on).ToList();
            foreach (var group in today.Where(g => g.All(l => l.TyreId is not { } t || made.Contains(t))))
                await LinesAsync(run, vehicleId, newSet, group, ct);
            foreach (var replacement in replaced.Where(r => r.Key.Day == on))
            {
                var news = replacement.Select(old => singles.First(t => t.Id == old.ReplacedById)).ToList();
                var boughtLines = news.Select(t => boughtFor.FirstOrDefault(l => l.TyreId == t.Id)).ToList();
                var first = boughtLines.FirstOrDefault(l => l is not null);
                await tyres.ReplaceTyresAsync(new TyreReplaceInput(newSet, on, first?.Odometer, replacement.Key.Why,
                    [.. replacement.Select((old, n) => new TyreReplacement(run.New(old.Id), run.New(news[n].Id), news[n].Label, news[n].Name, news[n].MadeWeek, news[n].MadeYear,
                        run.Map(boughtLines[n]?.Id)))],
                    first is null ? null : run.FeeOf(first)), ct);
                foreach (var t in news) made.Add(t.Id);
                run.Lines += boughtLines.Count(l => l is not null);
                foreach (var line in boughtLines.Where(l => l?.Note is not null)) await tyres.UpdateEntryAsync(run.New(line!.Id),
                    new TyreEntryInput(line.Kind, newSet, line.Day, line.Odometer, line.Note, run.FeeOf(line)), ct);
            }
            foreach (var group in today) await LinesAsync(run, vehicleId, newSet, group, ct); // those replayed above are skipped
        }

        // 4. The places as they were: the break-up gives exact corners only to the time on the car running through its day.
        await PlacesAsync(run, vehicleId, places, ct);

        // 5. The fees' expenses (a note or discount a fee cannot carry), the rough figure as it is now, the retirement.
        await FeesAsync(run, ct);
        if (set.IsBrokenUp && (set.RoughDistance != rough || set.RoughUntil != roughUntil) && set.RoughUntil is { } until)
            await tyres.SetRoughAsync(newSet, set.RoughDistance, until, ct);
        if (set.RetiredOn is { } retired) await tyres.RetireAsync(newSet, retired, ct);
        return new TyreReplayed(newSet, installations.Count, run.Lines, singles.Count, run.Ids, run.ExpensesMade, run.AmountsHere);
    }

    /// <summary>The tyre log's lines of no set (#339, the owner's decision 6): replayed as a set's other lines.</summary>
    public async Task<TyreReplayed> LinesOfNoSetAsync(Guid vehicleId, TyreBook backup, IReadOnlyCollection<Guid>? only, CancellationToken ct, ImportExpensePool? pool = null)
    {
        var run = new Run(backup, pool);
        foreach (var group in LinesOf(backup, null).Where(l => only is null || only.Contains(l.Id)).GroupBy(l => l.GroupId ?? l.Id).OrderBy(g => g.Min(l => l.Day)))
            await LinesAsync(run, vehicleId, null, [.. group], ct);
        await FeesAsync(run, ct);
        return new TyreReplayed(Guid.Empty, 0, run.Lines, 0, run.Ids, run.ExpensesMade, run.AmountsHere);
    }

    /// <summary>
    /// One line, or a group of lines made together for several tyres: with an amount through the expense service (the expense and its lines
    /// in one save), else through the tyre log; then the lines' note, which the expense's add does not carry.
    /// </summary>
    private async Task LinesAsync(Run run, Guid vehicleId, Guid? setId, IReadOnlyList<TyreEntry> group, CancellationToken ct)
    {
        if (run.Ids.ContainsKey(group[0].Id)) return; // replayed already
        var first = group[0];
        var single = first.TyreId is not null;
        var tyreLines = single ? group.Select(l => new TyreLine(run.New(l.TyreId!.Value), run.New(l.Id))).ToList() : null;
        if (run.ExpenseOf(first) is { } values)
        {
            var details = new ExpenseTyreDetails(first.Kind, setId, tyreLines, LineId: single ? null : run.New(first.Id));
            Guid expenseId;
            if (run.Pool?.Take(values) is { } here)
            {
                // The same expense is the car's already, a plain one (an expenses-only import brought it): the lines are tied to it.
                run.Ids[first.ExpenseId!.Value] = expenseId = here;
                await expenses.UpdateAsync(here, values, ct, tyre: details);
            }
            else
            {
                expenseId = run.New(first.ExpenseId!.Value);
                await expenses.AddAsync(vehicleId, values, ct, id: expenseId, tyre: details);
                run.ExpensesMade++;
            }
            foreach (var note in group.Where(l => l.Note is not null).GroupBy(l => l.Note))
            {
                var lines = note.ToList();
                await tyres.UpdateEntryAsync(run.New(lines[0].Id), new TyreEntryInput(first.Kind, setId, first.Day, first.Odometer, note.Key,
                    new TyreFee(values.Amount, values.Currency, values.Category, values.Title, expenseId), Also: [.. lines.Skip(1).Select(l => run.New(l.Id))]), ct);
            }
        }
        else
        {
            if (!single) run.New(first.Id);
            await tyres.AddEntryAsync(vehicleId, new TyreEntryInput(first.Kind, setId, first.Day, first.Odometer, first.Note, Tyres: tyreLines), ct,
                id: single ? null : run.New(first.Id));
        }
        run.Lines += group.Count;
    }

    /// <summary>A tyre's exact corner on a time on the car (Front, Rear or none are not given to a break-up: the places step puts them right).</summary>
    private static TyreCorner? CornerOn(IReadOnlyList<TyrePlace> places, Guid tyreId, Guid? installationId) =>
        places.Where(p => p.TyreId == tyreId && p.InstallationId == installationId).OrderBy(p => p.From).Select(p => p.Corner).FirstOrDefault(c => c is { } corner && corner.IsExact());

    /// <summary>
    /// Each single tyre's times on the car as the backup has them: matched with the replay's by the time on the car and the first day,
    /// put right where they differ (corners first emptied, then set, so two tyres that swapped corners never meet), the missing added, the
    /// extra ones moved to the trash.
    /// </summary>
    private async Task PlacesAsync(Run run, Guid vehicleId, IReadOnlyList<TyrePlace> places, CancellationToken ct)
    {
        if (places.Count == 0) return;
        var now = await tyres.OverviewAsync(vehicleId, ct) ?? throw new InvalidOperationException("The car is gone.");
        var tyreIds = places.Select(p => run.New(p.TyreId)).ToHashSet();
        var have = now.Sets.SelectMany(s => s.Tyres ?? []).Where(t => tyreIds.Contains(t.Tyre.Id)).SelectMany(t => t.Places).Select(p => p.Place).ToList();
        var pairs = new List<(TyrePlace Want, TyrePlace Have)>();
        var unmatched = have.ToList();
        foreach (var want in places.OrderBy(p => p.From))
        {
            var match = unmatched.FirstOrDefault(h => h.TyreId == run.New(want.TyreId) && h.InstallationId == run.New(want.InstallationId) && h.From == want.From)
                ?? unmatched.FirstOrDefault(h => h.TyreId == run.New(want.TyreId) && h.InstallationId == run.New(want.InstallationId));
            if (match is null) continue;
            unmatched.Remove(match);
            pairs.Add((want, match));
        }
        var changing = pairs.Where(p => p.Want.Corner != p.Have.Corner || p.Want.From != p.Have.From || p.Want.Until != p.Have.Until).ToList();
        foreach (var (want, h) in changing.Where(p => p.Want.Corner != p.Have.Corner && p.Have.Corner is not null))
            await tyres.UpdatePlaceAsync(h.Id, new TyrePlaceInput(h.From, h.Until, null), ct);
        foreach (var (want, h) in changing)
            await tyres.UpdatePlaceAsync(h.Id, new TyrePlaceInput(want.From, want.Until, want.Corner), ct);
        foreach (var extra in unmatched) await tyres.TrashPlaceAsync(extra.Id, ct);
        foreach (var missing in places.Where(w => pairs.All(p => p.Want != w)))
            await tyres.AddPlaceAsync(run.New(missing.TyreId), new TyrePlaceInput(missing.From, missing.Until, missing.Corner), ct);
    }

    /// <summary>A fee's expense as the backup has it: a fee makes it with an amount, currency, category and title only.</summary>
    private async Task FeesAsync(Run run, CancellationToken ct)
    {
        foreach (var (id, values) in run.Fees.Where(f => f.Values.Note is not null || f.Values.Discount is not null || f.Values.Odometer is not null))
            await expenses.UpdateAsync(id, values, ct);
    }
}
