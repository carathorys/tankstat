using Tankstat.Domain.Access;
using Tankstat.Domain.Sync;

namespace Tankstat.Domain.Tyres;

/// <summary>The season a tyre set is made for.</summary>
public enum TyreSeason
{
    Summer,
    Winter,
    AllSeason,
}

/// <summary>
/// A set of tyres of a vehicle (#287). It is tracked as one thing: its time on the car is a list of <see cref="TyreInstallation"/>es, and its
/// distance is worked out from them and the vehicle's odometer readings (<see cref="TyreCalculator"/>), never stored. A used set starts
/// with a rough figure for what it ran before tracking (<see cref="RoughDistance"/>, in the vehicle's distance unit), which stands for the
/// time before <see cref="RoughUntil"/>. Trashing it hides its installations through it; retiring it ends its life on the car and keeps everything.
/// </summary>
public sealed class TyreSet : IOwned, ISoftDeletable, ISynced
{
    public const int MaxNameLength = 80;
    public const int MaxSizeLength = 40;

    private TyreSet() { } // EF Core

    public Guid Id { get; private set; }

    /// <summary>Always the owner of the vehicle, denormalized so access filtering works on this table directly.</summary>
    public Guid OwnerId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid CreatedById { get; private set; }

    public string Name { get; private set; } = "";
    public TyreSeason Season { get; private set; }

    /// <summary>The size as printed on the sidewall (205/55 R16 91V), free text.</summary>
    public string? Size { get; private set; }

    /// <summary>When the tyres were first used (a used set: as far as the owner knows; a new one: the day they were bought).</summary>
    public DateOnly Since { get; private set; }

    /// <summary>The week of manufacture from the DOT code (1..53), with <see cref="MadeYear"/>; null when not given.</summary>
    public int? MadeWeek { get; private set; }

    /// <summary>The year of manufacture from the DOT code (2000..2099).</summary>
    public int? MadeYear { get; private set; }

    /// <summary>What the set ran before it was tracked, roughly, in the vehicle's distance unit (0 for a new set).</summary>
    public long RoughDistance { get; private set; }

    /// <summary>The first day <see cref="RoughDistance"/> does not cover: from it on, the installations measure the distance. Null without a rough figure.</summary>
    public DateOnly? RoughUntil { get; private set; }

    /// <summary>
    /// The months it is usually on the car (#291), when they differ from its season's; null means the season's (<see cref="Months"/>), so a
    /// set that never chose its own follows a change of season. What the estimate of the rough figure counts.
    /// </summary>
    public TyreMonths? UsualMonths { get; private set; }

    /// <summary>The months it is usually on the car: its own, else its season's.</summary>
    public TyreMonths Months => UsualMonths ?? TyreMonths.DefaultFor(Season);

    /// <summary>
    /// The month it is reminded to be fitted from (#292), when that differs from its season's: 1 to 12, or 0 for no reminder; null means
    /// the season's (<see cref="TyreReminder.DefaultMonth"/>), so a set that never chose follows a change of season.
    /// </summary>
    public int? ReminderMonth { get; private set; }

    /// <summary>The month the reminder starts from, its own or its season's; null: no reminder.</summary>
    public int? EffectiveReminderMonth => ReminderMonth switch
    {
        null => TyreReminder.DefaultMonth(Season),
        0 => null,
        var month => month,
    };

    /// <summary>
    /// The day the reminder was set: the set added, or its month (its own, or its season's with the season) last changed. A season that
    /// began before that day's month does not count (decision 78). Null: no such limit.
    /// </summary>
    public DateOnly? ReminderSince { get; private set; }

    /// <summary>The season (its first day) <em>Not this season</em> was tapped for; it ends by itself when the next season begins.</summary>
    public DateOnly? ReminderDismissedFor { get; private set; }

    /// <summary>The day it was retired; a retired set takes no swap.</summary>
    public DateOnly? RetiredOn { get; private set; }

    /// <summary>
    /// The day it was broken up into single tyres (#289), never undone: from then on its tyres (<see cref="Tyre"/>) have their own times on the
    /// car, inside the set's installations, and a swap places each tyre. Its installations record no pairs from then on.
    /// </summary>
    public DateOnly? BrokenUpOn { get; private set; }

    public bool IsBrokenUp => BrokenUpOn is not null;

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;
    public bool IsRetired => RetiredOn is not null;

    /// <summary>Counts the saves of what a client can edit, from 1 (see <see cref="VersionCheck"/>).</summary>
    public int Version { get; private set; }

    /// <inheritdoc cref="ISynced.UpdatedAt"/>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <inheritdoc cref="ISynced.ChangedAt"/>
    public DateTimeOffset? ChangedAt { get; private set; }

    /// <inheritdoc cref="ISynced.ChangedById"/>
    public Guid? ChangedById { get; private set; }

    /// <inheritdoc cref="ISynced.LastChange"/>
    public EntityChange? LastChange { get; private set; }

    /// <inheritdoc cref="ISynced.SavedVersion"/>
    public int SavedVersion => _loadedVersion ?? Version;

    private int? _loadedVersion;

    void ISynced.Saved() => _loadedVersion = null;

    Guid ISynced.SyncVehicleId => VehicleId;
    OfflineEntityType ISynced.SyncType => OfflineEntityType.TyreSet;

    private void Bump(EntityChange what, Guid? by)
    {
        _loadedVersion ??= Version;
        Version++;
        (LastChange, ChangedById) = (what, by);
    }

    /// <param name="roughUntil">The first day the rough figure does not cover (the day tracking starts); needed with a rough figure.</param>
    /// <param name="id">The id the client chose beforehand, if any (see <see cref="EntityId"/>).</param>
    /// <param name="usualMonths">The months it is usually on the car (1 = January); null: its season's.</param>
    /// <param name="reminderMonth">The month it is reminded from (0: no reminder); null: its season's.</param>
    /// <param name="today">The day it is added: the reminder counts from its month.</param>
    public static TyreSet Create(
        Guid ownerId, Guid createdById, Guid vehicleId, string name, TyreSeason season, string? size, DateOnly since, int? madeWeek, int? madeYear,
        long roughDistance, DateOnly? roughUntil, Guid? id = null, IReadOnlyList<int>? usualMonths = null, int? reminderMonth = null, DateOnly? today = null)
    {
        var set = new TyreSet
        {
            Id = EntityId.OrNew(id), Version = 1, OwnerId = ownerId, CreatedById = createdById, VehicleId = vehicleId,
            LastChange = EntityChange.Created, ChangedById = createdById,
        };
        set.Apply(name, season, size, since, madeWeek, madeYear);
        set.ApplyMonths(usualMonths);
        set.ReminderMonth = OwnReminderMonth(season, reminderMonth);
        set.ReminderSince = today;
        set.ApplyRough(roughDistance, roughUntil);
        return set;
    }

    /// <param name="usualMonths">The months it is usually on the car; null (a client from before them) leaves them as they are.</param>
    /// <param name="reminderMonth">The month it is reminded from (0: no reminder); null (a client from before it) leaves it as it is.</param>
    /// <param name="today">The day of the edit: a month that changes with it (its own, or its season's with the season) counts from then.</param>
    public void Update(
        string name, TyreSeason season, string? size, DateOnly since, int? madeWeek, int? madeYear, Guid? by = null, IReadOnlyList<int>? usualMonths = null,
        int? reminderMonth = null, DateOnly? today = null)
    {
        RequireLive();
        var before = EffectiveReminderMonth;
        Apply(name, season, size, since, madeWeek, madeYear);
        if (usualMonths is not null) ApplyMonths(usualMonths);
        if (reminderMonth is not null) ReminderMonth = OwnReminderMonth(season, reminderMonth);
        if (EffectiveReminderMonth != before) ReminderSince = today ?? ReminderSince;
        Bump(EntityChange.Edited, by);
    }

    /// <summary>
    /// <em>Not this season</em> (#292): the reminder is off for <paramref name="season"/>, for everyone. Only the set's season of today can
    /// be dismissed; any other (an older one, sent late from a device) changes nothing, and is no refusal.
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    public bool DismissReminder(DateOnly season, DateOnly today, Guid? by = null)
    {
        RequireLive();
        if (EffectiveReminderMonth is not { } month || TyreReminder.SeasonOf(month, today) != season || ReminderDismissedFor == season) return false;
        ReminderDismissedFor = season;
        Bump(EntityChange.Edited, by);
        return true;
    }

    /// <summary>Takes <em>Not this season</em> back (the toast's Undo); nothing to take back changes nothing.</summary>
    public bool RestoreReminder(Guid? by = null)
    {
        RequireLive();
        if (ReminderDismissedFor is null) return false;
        ReminderDismissedFor = null;
        Bump(EntityChange.Edited, by);
        return true;
    }

    /// <summary>Sets the rough figure and the first day it does not cover (the caller picks that day: never in the future, by default before the set's first installation).</summary>
    /// <param name="usualMonths">The months the figure was estimated over, kept with the set (#291, the owner's decision 55); null leaves them as they are.</param>
    public void SetRough(long roughDistance, DateOnly? roughUntil, Guid? by = null, IReadOnlyList<int>? usualMonths = null)
    {
        RequireLive();
        ApplyRough(roughDistance, roughUntil ?? RoughUntil);
        if (usualMonths is not null) ApplyMonths(usualMonths);
        Bump(EntityChange.Edited, by);
    }

    /// <summary>Retires the set on <paramref name="day"/>: the caller makes sure it is not on the car.</summary>
    public void Retire(DateOnly day, Guid? by = null)
    {
        RequireLive();
        if (IsRetired) throw new DomainException("tyres.alreadyRetired", "This tyre set is retired already.");
        RetiredOn = day;
        Bump(EntityChange.Edited, by);
    }

    /// <summary>Tracks the set tyre by tyre from <paramref name="day"/> (#289); refused on a set already broken up.</summary>
    public void BreakUp(DateOnly day, Guid? by = null)
    {
        RequireLive();
        if (IsBrokenUp) throw new DomainException("tyres.alreadyBrokenUp", "This tyre set is tracked tyre by tyre already.", new { Set = Name });
        BrokenUpOn = day;
        Bump(EntityChange.Edited, by);
    }

    /// <summary>Takes a retirement back (the toast's Undo).</summary>
    public void Unretire(Guid? by = null)
    {
        RequireLive();
        if (!IsRetired) throw new DomainException("tyres.notRetired", "This tyre set is not retired.");
        RetiredOn = null;
        Bump(EntityChange.Edited, by);
    }

    public void MarkDeleted(DateTimeOffset now, Guid? by = null)
    {
        if (IsDeleted) throw new DomainException("tyres.alreadyTrashed", "This tyre set is already in the trash.");
        DeletedAt = now;
        Bump(EntityChange.Trashed, by);
    }

    public void Restore(Guid? by = null)
    {
        if (!IsDeleted) throw new DomainException("tyres.notTrashed", "This tyre set is not in the trash.");
        DeletedAt = null;
        Bump(EntityChange.Restored, by);
    }

    private void RequireLive()
    {
        if (IsDeleted) throw new DomainException("tyres.trashedCannotEdit", "A tyre set in the trash cannot be changed; restore it first.");
    }

    private void Apply(string name, TyreSeason season, string? size, DateOnly since, int? madeWeek, int? madeYear)
    {
        var trimmedName = name?.Trim() ?? "";
        if (trimmedName.Length == 0) throw new DomainException("tyres.nameRequired", "The tyre set needs a name.");
        if (trimmedName.Length > MaxNameLength)
            throw new DomainException("tyres.nameTooLong", $"The name can be at most {MaxNameLength} characters.", new { Max = MaxNameLength });
        if (!Enum.IsDefined(season)) throw new DomainException("tyres.unknownSeason", $"Unknown season {(int)season}.", new { Value = ((int)season).ToString() });
        var trimmedSize = string.IsNullOrWhiteSpace(size) ? null : size.Trim();
        if (trimmedSize is { Length: > MaxSizeLength })
            throw new DomainException("tyres.sizeTooLong", $"The size can be at most {MaxSizeLength} characters.", new { Max = MaxSizeLength });
        if ((madeWeek is null) != (madeYear is null) || madeWeek is < 1 or > 53 || madeYear is < 2000 or > 2099)
            throw new DomainException("tyres.madeInvalid", "The made date is a week (1 to 53) and a year.");
        Name = trimmedName;
        Season = season;
        Size = trimmedSize;
        Since = since;
        (MadeWeek, MadeYear) = (madeWeek, madeYear);
    }

    /// <summary>A month of its own only when it differs from its season's (0 for none); null given is the season's.</summary>
    private static int? OwnReminderMonth(TyreSeason season, int? given)
    {
        if (given is null) return null;
        if (given is < 0 or > 12) throw new DomainException("tyres.reminderMonthInvalid", "The month to be reminded from is a number from 1 to 12, or 0 for no reminder.");
        return given == (TyreReminder.DefaultMonth(season) ?? 0) ? null : given;
    }

    /// <summary>Months of its own only when they differ from its season's: the season's are kept as none, so they follow the season.</summary>
    private void ApplyMonths(IReadOnlyList<int>? usualMonths)
    {
        if (usualMonths is null)
        {
            UsualMonths = null;
            return;
        }
        var months = TyreMonths.Of(usualMonths);
        UsualMonths = months == TyreMonths.DefaultFor(Season) ? null : months;
    }

    private void ApplyRough(long roughDistance, DateOnly? roughUntil)
    {
        if (roughDistance < 0) throw new DomainException("tyres.roughNegative", "The distance cannot be negative.");
        if (roughDistance > 0 && roughUntil is null) throw new DomainException("tyres.roughUntilRequired", "A rough figure needs the day tracking starts.");
        RoughDistance = roughDistance;
        RoughUntil = roughDistance > 0 ? roughUntil : null;
    }
}
