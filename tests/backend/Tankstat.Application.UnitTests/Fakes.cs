using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Expenses;
using Tankstat.Application.Photos;
using Tankstat.Domain.Photos;
using Tankstat.Application.Images;
using Tankstat.Application.Imports;
using Tankstat.Application.Notifications;
using Tankstat.Application.Odometers;
using Tankstat.Application.Recognition;
using Tankstat.Application.Sharing;
using Tankstat.Application.Stats;
using Tankstat.Domain.Charts;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Access;
using Tankstat.Domain.Images;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Recognition;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

internal sealed class InMemoryVehicles : IVehicleRepository
{
    public List<Vehicle> Items { get; } = [];

    /// <summary>The last query the service passed down (after normalization).</summary>
    public VehicleQuery? LastQuery { get; private set; }

    // Sorting itself is the database's job and is tested against the real repository; the fake orders by name.
    private static bool Matches(Vehicle v, string? search) =>
        search is null || v.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || (v.LicensePlate?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

    private IReadOnlyList<Vehicle> Page(IEnumerable<Vehicle> rows, VehicleQuery query)
    {
        LastQuery = query;
        return rows.Where(v => Matches(v, query.Search)).OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).Skip(query.Skip).Take(query.Take).ToList();
    }

    public Task<IReadOnlyList<Vehicle>> ListAsync(OwnerScope scope, VehicleQuery query, CancellationToken ct) =>
        Task.FromResult(Page(Items.Where(v => !v.IsDeleted && scope.Contains(v.OwnerId, v.Id)), query));
    public Task<int> CountAsync(OwnerScope scope, string? search, CancellationToken ct) =>
        Task.FromResult(Items.Count(v => !v.IsDeleted && scope.Contains(v.OwnerId, v.Id) && Matches(v, search)));
    public Task<IReadOnlyList<Vehicle>> ListDeletedAsync(OwnerScope scope, VehicleQuery query, CancellationToken ct) =>
        Task.FromResult(Page(Items.Where(v => v.IsDeleted && scope.Contains(v.OwnerId, v.Id)), query));
    public Task<int> CountDeletedAsync(OwnerScope scope, CancellationToken ct) =>
        Task.FromResult(Items.Count(v => v.IsDeleted && scope.Contains(v.OwnerId, v.Id)));
    public Task<Vehicle?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(v => v.Id == id && !v.IsDeleted));
    public Task<IReadOnlyList<Vehicle>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Vehicle>>(Items.Where(v => ids.Contains(v.Id) && !v.IsDeleted).ToList());
    public Task<Vehicle?> FindIncludingDeletedAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(v => v.Id == id));
    public Task AddAsync(Vehicle vehicle, CancellationToken ct) { Items.Add(vehicle); return Task.CompletedTask; }
    public Task UpdateAsync(Vehicle vehicle, CancellationToken ct) => Task.CompletedTask; // entities are shared references
    public Task<Vehicle?> FindByPictureImageAsync(Guid imageId, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(v => v.PictureImageId == imageId && !v.IsDeleted));
    public Task<PurgeResult> PurgeAsync(OwnerScope scope, CancellationToken ct)
    {
        var doomed = Items.Where(v => v.IsDeleted && scope.Contains(v.OwnerId, v.Id)).ToList();
        Items.RemoveAll(doomed.Contains);
        return Task.FromResult(new PurgeResult(doomed.Count, doomed.Where(v => v.PictureImageId is not null).Select(v => v.PictureImageId!.Value).ToList(), doomed.Select(v => v.Id).ToList()));
    }
}

internal sealed class InMemoryRefuelings : IRefuelingRepository
{
    public List<Refueling> Items { get; } = [];

    private static IReadOnlyList<Refueling> Page(IEnumerable<Refueling> rows, RefuelingQuery q) =>
        rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.Odometer).Skip(q.Skip).Take(q.Take).ToList();

    public Task<IReadOnlyList<Refueling>> ListForVehicleAsync(Guid vehicleId, RefuelingQuery query, CancellationToken ct) =>
        Task.FromResult(Page(Items.Where(r => !r.IsDeleted && r.VehicleId == vehicleId), query));
    public Task<int> CountForVehicleAsync(Guid vehicleId, CancellationToken ct) => Task.FromResult(Items.Count(r => !r.IsDeleted && r.VehicleId == vehicleId));
    public Task<bool> AnyForVehicleAsync(Guid vehicleId, CancellationToken ct) => Task.FromResult(Items.Any(r => r.VehicleId == vehicleId));
    public Task<IReadOnlyList<Refueling>> ListAllForVehicleAsync(Guid vehicleId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Refueling>>(Items.Where(r => !r.IsDeleted && r.VehicleId == vehicleId).ToList());
    public Task SaveConsumptionsAsync(IReadOnlyList<Refueling> refuelings, CancellationToken ct) => Task.CompletedTask; // entities are shared references
    public Task<IReadOnlyList<Refueling>> ListDeletedAsync(OwnerScope scope, RefuelingQuery query, CancellationToken ct) =>
        Task.FromResult(Page(Items.Where(r => r.IsDeleted && scope.Contains(r.OwnerId, r.VehicleId)), query));
    public Task<int> CountDeletedAsync(OwnerScope scope, CancellationToken ct) =>
        Task.FromResult(Items.Count(r => r.IsDeleted && scope.Contains(r.OwnerId, r.VehicleId)));
    public Task<Refueling?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(r => r.Id == id && !r.IsDeleted));
    public Task<Refueling?> FindIncludingDeletedAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(r => r.Id == id));
    public Task AddAsync(Refueling refueling, CancellationToken ct) { Items.Add(refueling); return Task.CompletedTask; }
    public Task UpdateAsync(Refueling refueling, CancellationToken ct) => Task.CompletedTask; // entities are shared references
    public Task<PurgedLogs> PurgeAsync(OwnerScope scope, CancellationToken ct)
    {
        var doomed = Items.Where(r => r.IsDeleted && scope.Contains(r.OwnerId, r.VehicleId)).ToList();
        Items.RemoveAll(doomed.Contains);
        return Task.FromResult(new PurgedLogs(doomed.Count, doomed.Select(r => (r.VehicleId, r.Id)).ToList()));
    }
}

internal sealed class InMemoryExpenses : IExpenseRepository
{
    public List<Expense> Items { get; } = [];

    private static IReadOnlyList<Expense> Page(IEnumerable<Expense> rows, ExpenseQuery q) =>
        rows.OrderByDescending(e => e.Date).ThenBy(e => e.Id).Skip(q.Skip).Take(q.Take).ToList();

    public Task<IReadOnlyList<Expense>> ListForVehicleAsync(Guid vehicleId, ExpenseQuery query, CancellationToken ct) =>
        Task.FromResult(Page(Items.Where(e => !e.IsDeleted && e.VehicleId == vehicleId), query));
    public Task<int> CountForVehicleAsync(Guid vehicleId, CancellationToken ct) => Task.FromResult(Items.Count(e => !e.IsDeleted && e.VehicleId == vehicleId));
    public Task<bool> AnyForVehicleAsync(Guid vehicleId, CancellationToken ct) => Task.FromResult(Items.Any(e => e.VehicleId == vehicleId));
    public Task<IReadOnlyList<Expense>> ListAllForVehicleAsync(Guid vehicleId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Expense>>(Items.Where(e => !e.IsDeleted && e.VehicleId == vehicleId).ToList());
    public Task<IReadOnlyList<Expense>> ListDeletedAsync(OwnerScope scope, ExpenseQuery query, CancellationToken ct) =>
        Task.FromResult(Page(Items.Where(e => e.IsDeleted && scope.Contains(e.OwnerId, e.VehicleId)), query));
    public Task<int> CountDeletedAsync(OwnerScope scope, CancellationToken ct) =>
        Task.FromResult(Items.Count(e => e.IsDeleted && scope.Contains(e.OwnerId, e.VehicleId)));
    public Task<IReadOnlyList<string>> CategoriesAsync(Guid vehicleId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Items.Where(e => e.VehicleId == vehicleId && e.Category is not null).Select(e => e.Category!).Distinct().Order().ToList());
    public Task<Expense?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(e => e.Id == id && !e.IsDeleted));
    public Task<Expense?> FindIncludingDeletedAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(e => e.Id == id));
    public Task AddAsync(Expense expense, CancellationToken ct) { Items.Add(expense); return Task.CompletedTask; }
    public Task UpdateAsync(Expense expense, OdometerReading? newReading, OdometerReading? removedReading, CancellationToken ct) => Task.CompletedTask; // shared references
    public Task<PurgedLogs> PurgeAsync(OwnerScope scope, CancellationToken ct)
    {
        var doomed = Items.Where(e => e.IsDeleted && scope.Contains(e.OwnerId, e.VehicleId)).ToList();
        Items.RemoveAll(doomed.Contains);
        return Task.FromResult(new PurgedLogs(doomed.Count, doomed.Select(e => (e.VehicleId, e.Id)).ToList()));
    }
}

internal sealed class InMemoryRecurring(InMemoryVehicles vehicles) : IRecurringExpenseRepository
{
    public List<RecurringExpense> Items { get; } = [];
    public Task<IReadOnlyList<RecurringExpense>> ListForVehicleAsync(Guid vehicleId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RecurringExpense>>(Items.Where(i => i.VehicleId == vehicleId).ToList());
    public int ListForVehiclesCalls { get; private set; }
    public Task<IReadOnlyList<RecurringExpense>> ListForVehiclesAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct)
    {
        ListForVehiclesCalls++;
        return Task.FromResult<IReadOnlyList<RecurringExpense>>(Items.Where(i => vehicleIds.Contains(i.VehicleId)).ToList());
    }
    public Task<IReadOnlyList<Guid>> ListVehicleIdsAsync(OwnerScope scope, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>(Items
            .Where(i => scope.Contains(i.OwnerId, i.VehicleId) && vehicles.Items.Any(v => v.Id == i.VehicleId && !v.IsDeleted))
            .Select(i => i.VehicleId).Distinct().ToList());
    public Task<RecurringExpense?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(i => i.Id == id));
    public Task AddAsync(RecurringExpense item, CancellationToken ct) { Items.Add(item); return Task.CompletedTask; }
    /// <summary>When set, <see cref="UpdateAsync"/> throws it (a failing database).</summary>
    public Exception? FailUpdateWith { get; set; }
    public Task UpdateAsync(RecurringExpense item, CancellationToken ct) => FailUpdateWith is { } e ? Task.FromException(e) : Task.CompletedTask; // shared references
    public Task RemoveAsync(RecurringExpense item, CancellationToken ct) { Items.Remove(item); return Task.CompletedTask; }
}

internal sealed class InMemoryNotifications : INotificationRepository
{
    public List<Notification> Items { get; } = [];

    private IEnumerable<Notification> Of(Guid recipient, bool unreadOnly) => Items.Where(n => n.RecipientId == recipient && (!unreadOnly || !n.IsRead));

    public Task<IReadOnlyList<Notification>> ListAsync(Guid recipientId, bool unreadOnly, int skip, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Notification>>(Of(recipientId, unreadOnly).OrderByDescending(n => n.UpdatedAt).ThenBy(n => n.Id).Skip(skip).Take(take).ToList());
    public Task<int> CountAsync(Guid recipientId, bool unreadOnly, CancellationToken ct) => Task.FromResult(Of(recipientId, unreadOnly).Count());
    public Task<Notification?> FindOpenAsync(Guid recipientId, NotificationTopic topic, NotificationRef subject, NotificationRef? context, CancellationToken ct) =>
        Task.FromResult(Of(recipientId, true).OrderByDescending(n => n.UpdatedAt).FirstOrDefault(n => n.Topic == topic && n.Subject == subject && n.Context == context));
    public Task<IReadOnlyList<Notification>> ListForSubjectsAsync(Guid recipientId, NotificationTopic topic, IReadOnlyCollection<Guid> subjectIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Notification>>(Of(recipientId, false).Where(n => n.Topic == topic && subjectIds.Contains(n.SubjectId)).ToList());
    public Task<int> CountCreatedSinceAsync(Guid recipientId, DateTimeOffset since, IReadOnlyCollection<NotificationTopic> topics, CancellationToken ct) =>
        Task.FromResult(Of(recipientId, false).Count(n => n.CreatedAt > since && topics.Contains(n.Topic)));
    public Task<bool> AddAsync(Notification n, CancellationToken ct)
    {
        if (Items.Any(x => x.RecipientId == n.RecipientId && x.Topic == n.Topic && x.Subject == n.Subject && x.ContextId == n.ContextId && x.Occurrence == n.Occurrence))
            return Task.FromResult(false);
        Items.Add(n);
        return Task.FromResult(true);
    }
    public Task UpdateAsync(Notification n, CancellationToken ct) => Task.CompletedTask; // shared references
    public Task RemoveAsync(Notification n, CancellationToken ct) { Items.Remove(n); return Task.CompletedTask; }
    public Task<IReadOnlyList<Notification>> MarkReadAsync(Guid recipientId, IReadOnlyCollection<Guid>? ids, DateTimeOffset at, CancellationToken ct)
    {
        var unread = Of(recipientId, true).Where(n => ids is null || ids.Contains(n.Id)).ToList();
        foreach (var n in unread) n.MarkRead(at);
        return Task.FromResult<IReadOnlyList<Notification>>(unread);
    }
    public Task<int> PurgeReadAsync(Guid recipientId, DateTimeOffset readBefore, IReadOnlyCollection<Guid> keepSubjectIds, CancellationToken ct) =>
        Task.FromResult(Items.RemoveAll(n => n.RecipientId == recipientId && n.ReadAt < readBefore && !keepSubjectIds.Contains(n.SubjectId)));
}

internal sealed class InMemoryStats(InMemoryRefuelings refuelings, InMemoryExpenses expenses) : IStatsRepository
{
    public Task<StatsData> LoadAsync(Guid vehicleId, CancellationToken ct)
    {
        var fuel = refuelings.Items.Where(r => !r.IsDeleted && r.VehicleId == vehicleId).ToList();
        var costs = expenses.Items.Where(e => !e.IsDeleted && e.VehicleId == vehicleId).ToList();
        return Task.FromResult(new StatsData(
            fuel.Select(r => new FuelPoint(r.Date, r.Volume, r.TotalCost, r.Currency, r.Odometer, r.IsFullTank, r.Consumption)).ToList(),
            costs.Select(e => new ExpensePoint(e.Date, e.Category, e.Amount, e.Currency)).ToList(),
            fuel.Select(r => new OdometerPoint(r.Date, r.Odometer)).Concat(costs.Where(e => e.Odometer is not null).Select(e => new OdometerPoint(e.Date, e.Odometer!.Value))).ToList()));
    }
}

internal sealed class InMemoryCharts : IVehicleChartRepository
{
    public List<VehicleChart> Items { get; } = [];
    public Task<IReadOnlyList<VehicleChart>> ListVisibleAsync(Guid vehicleId, Guid userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VehicleChart>>(Items.Where(c => c.VehicleId == vehicleId && (c.IsShared || c.CreatedById == userId)).OrderBy(c => c.CreatedAt).ToList());
    public Task<int> CountByUserAsync(Guid vehicleId, Guid userId, CancellationToken ct) => Task.FromResult(Items.Count(c => c.VehicleId == vehicleId && c.CreatedById == userId));
    public Task<VehicleChart?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(c => c.Id == id));
    public Task AddAsync(VehicleChart chart, CancellationToken ct) { Items.Add(chart); return Task.CompletedTask; }
    public Task UpdateAsync(VehicleChart chart, CancellationToken ct) => Task.CompletedTask; // shared references
    public Task RemoveAsync(VehicleChart chart, CancellationToken ct) { Items.Remove(chart); return Task.CompletedTask; }
}

/// <summary>Readings are the ones owned by the in-memory logs and expenses (live ones only, like the real query filter).</summary>
internal sealed class InMemoryReadings(InMemoryRefuelings refuelings, InMemoryExpenses expenses) : IOdometerReadingRepository
{
    private IEnumerable<OdometerReading> Live(Guid vehicleId) =>
        refuelings.Items.Where(r => !r.IsDeleted && r.VehicleId == vehicleId).Select(r => r.OdometerReading)
            .Concat(expenses.Items.Where(e => !e.IsDeleted && e.VehicleId == vehicleId && e.OdometerReading is not null).Select(e => e.OdometerReading!));

    public Task<OdometerReading?> PreviousAsync(Guid vehicleId, DateOnly date, Guid? except, CancellationToken ct) =>
        Task.FromResult(Live(vehicleId).Where(r => r.Date < date && r.Id != except).OrderByDescending(r => r.Date).ThenByDescending(r => r.Value).FirstOrDefault());
    public Task<OdometerReading?> NextAsync(Guid vehicleId, DateOnly date, Guid? except, CancellationToken ct) =>
        Task.FromResult(Live(vehicleId).Where(r => r.Date > date && r.Id != except).OrderBy(r => r.Date).ThenBy(r => r.Value).FirstOrDefault());
    public Task<OdometerReading?> LatestAsync(Guid vehicleId, CancellationToken ct) =>
        Task.FromResult(Live(vehicleId).OrderByDescending(r => r.Date).ThenByDescending(r => r.Value).FirstOrDefault());
    public Task<IReadOnlyDictionary<Guid, OdometerReading>> LatestForVehiclesAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, OdometerReading>>(vehicleIds
            .Select(id => Live(id).OrderByDescending(r => r.Date).ThenByDescending(r => r.Value).FirstOrDefault())
            .OfType<OdometerReading>().ToDictionary(r => r.VehicleId));
    public Task<bool> AnyAsync(Guid vehicleId, CancellationToken ct) =>
        Task.FromResult(refuelings.Items.Any(r => r.VehicleId == vehicleId) || expenses.Items.Any(e => e.VehicleId == vehicleId && e.OdometerReading is not null));
}

internal sealed class InMemoryResourceGrants : IResourceGrantRepository
{
    public List<ResourceGrant> Items { get; } = [];
    public Task<IReadOnlyList<ResourceGrant>> ListForResourceAsync(ResourceType t, Guid id, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ResourceGrant>>(Items.Where(g => g.ResourceType == t && g.ResourceId == id).ToList());
    public Task<IReadOnlyList<ResourceGrant>> ListForGranteeAsync(Guid grantee, ResourceType t, GrantedFeature f, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ResourceGrant>>(Items.Where(g => g.GranteeId == grantee && g.ResourceType == t && g.Feature == f).ToList());
    public Task<ResourceGrant?> FindAsync(ResourceType t, Guid id, Guid grantee, GrantedFeature f, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(g => g.ResourceType == t && g.ResourceId == id && g.GranteeId == grantee && g.Feature == f));
    public Task AddAsync(ResourceGrant g, CancellationToken ct) { Items.Add(g); return Task.CompletedTask; }
    public Task UpdateAsync(ResourceGrant g, CancellationToken ct) => Task.CompletedTask;
    public Task RemoveAsync(ResourceGrant g, CancellationToken ct) { Items.Remove(g); return Task.CompletedTask; }
}

internal sealed class InMemoryUsers : IUserRepository
{
    public List<User> Items { get; } = [];
    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(u => u.Id == id));
    public Task<User?> FindLocalByEmailAsync(string e, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(u => u.Provider == UserProvider.Local && u.Subject == e));
    public Task<User?> FindExternalAsync(UserProvider p, string s, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(u => u.Provider == p && u.Subject == s));
    public Task<User?> FindByAvatarImageAsync(Guid imageId, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(u => u.AvatarImageId == imageId));
    public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<User>>(Items.ToList());
    public Task<bool> AnyLocalAdminAsync(CancellationToken ct) =>
        Task.FromResult(Items.Any(u => u.Provider == UserProvider.Local && u.IsAdmin && !u.IsDisabled));
    public Task AddAsync(User user, CancellationToken ct) { Items.Add(user); return Task.CompletedTask; }
    public Task UpdateAsync(User user, CancellationToken ct) => Task.CompletedTask; // entities are shared references
}

internal sealed class InMemoryLogPhotos : ILogPhotoRepository
{
    public List<LogPhoto> Items { get; } = [];
    public bool FailAdds { get; set; }
    public bool FailLists { get; set; }

    /// <summary>Runs right after a photo was added: lets a test put in other photos "at the same moment".</summary>
    public Action<LogPhoto>? AfterAdd { get; set; }
    public Task<IReadOnlyList<LogPhoto>> ListForLogAsync(LogType logType, Guid logId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (FailLists) throw new InvalidOperationException("database down");
        return Task.FromResult<IReadOnlyList<LogPhoto>>(Items.Where(p => p.LogType == logType && p.LogId == logId).OrderBy(p => p.CreatedAt).ToList());
    }
    public Task<IReadOnlyList<LogPhoto>> ListForLogsAsync(LogType logType, IReadOnlyCollection<Guid> logIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LogPhoto>>(Items.Where(p => p.LogType == logType && logIds.Contains(p.LogId)).OrderBy(p => p.CreatedAt).ToList());
    public Task<int> CountForLogAsync(LogType logType, Guid logId, CancellationToken ct) =>
        Task.FromResult(Items.Count(p => p.LogType == logType && p.LogId == logId));
    public Task<LogPhoto?> FindByImageAsync(Guid imageId, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(p => p.ImageId == imageId));
    public Task AddAsync(LogPhoto photo, CancellationToken ct)
    {
        if (FailAdds) throw new InvalidOperationException("database down");
        Items.Add(photo);
        AfterAdd?.Invoke(photo);
        return Task.CompletedTask;
    }
    public Task RemoveAsync(LogPhoto photo, CancellationToken ct) { Items.Remove(photo); return Task.CompletedTask; }
}

/// <summary>Records what a delete asked for; the real moving and purging is covered against SQLite in the infrastructure tests.</summary>
internal sealed class FakeUserData : IUserDataRepository
{
    public HashSet<Guid> Owners { get; } = [];
    public List<(Guid UserId, Guid? MoveTo)> Deleted { get; } = [];
    public List<Guid> PurgedPictures { get; } = [];
    public List<Guid> PurgedVehicles { get; } = [];
    public Task<bool> OwnsDataAsync(Guid userId, CancellationToken ct) => Task.FromResult(Owners.Contains(userId));
    public Task<PurgedUserData> DeleteUserAsync(Guid userId, Guid? moveDataTo, CancellationToken ct)
    {
        Deleted.Add((userId, moveDataTo));
        return Task.FromResult(new PurgedUserData(PurgedVehicles.ToList(), PurgedPictures.ToList()));
    }
}

internal sealed class InMemoryPhotoDrafts : IPhotoDraftRepository
{
    public List<PhotoDraft> Items { get; } = [];
    public Task<PhotoDraft?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(d => d.Id == id));
    public Task<IReadOnlyList<PhotoDraft>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PhotoDraft>>(Items.Where(d => ids.Contains(d.Id)).ToList());
    public Task<int> CountAsync(Guid vehicleId, Guid createdById, CancellationToken ct) => Task.FromResult(Items.Count(d => d.VehicleId == vehicleId && d.CreatedById == createdById));
    public Task<IReadOnlyList<PhotoDraft>> ListCreatedBeforeAsync(DateTimeOffset before, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PhotoDraft>>(Items.Where(d => d.CreatedAt < before).ToList());
    public Task AddAsync(PhotoDraft draft, CancellationToken ct) { Items.Add(draft); return Task.CompletedTask; }
    public Task RemoveAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) { Items.RemoveAll(d => ids.Contains(d.Id)); return Task.CompletedTask; }
}

internal sealed class InMemoryPhotoReadings : IPhotoReadingRepository
{
    public List<PhotoReading> Items { get; } = [];
    public Task AddAsync(PhotoReading reading, CancellationToken ct) { Items.Add(reading); return Task.CompletedTask; }
    public Task<IReadOnlyList<PhotoReading>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PhotoReading>>(Items.Where(r => ids.Contains(r.Id)).ToList());
    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset now, int max, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>(Items.Where(r => r.IsDue(now)).OrderBy(r => r.DueAt).ThenBy(r => r.Id).Take(max).Select(r => r.Id).ToList());
    public Task<PhotoReading?> ClaimAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var reading = Items.FirstOrDefault(r => r.Id == id && r.IsDue(now));
        reading?.Claim(now);
        return Task.FromResult(reading);
    }
    public Task SaveAsync(PhotoReading reading, CancellationToken ct) => Task.CompletedTask; // the same instance is kept
    public Task<IReadOnlyList<PhotoReading>> ListStaleAsync(DateTimeOffset claimedBefore, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PhotoReading>>(Items.Where(r => r.Status == ReadingStatus.Reading && r.ClaimedAt < claimedBefore).ToList());
    public Task RemoveAsync(Guid id, CancellationToken ct) { Items.RemoveAll(r => r.Id == id); return Task.CompletedTask; }
}

/// <summary>A recognition provider that answers what the test says (by default: nothing recognised).</summary>
internal sealed class FakeRecognitionProvider : IRecognitionProvider
{
    public bool Configured { get; set; } = true;
    public bool Healthy { get; set; } = true;
    public int HealthChecks { get; private set; }
    public Func<RecognitionRequest, RecognitionResult> Answer { get; set; } = _ => new RecognitionResult("fake-1", DocumentKind.Unknown, []);
    public List<RecognitionRequest> Requests { get; } = [];
    public string Name => "fake";
    public bool IsConfigured => Configured;
    public Task<bool> IsHealthyAsync(CancellationToken ct) { HealthChecks++; return Task.FromResult(Healthy); }
    public Task<RecognitionResult> ReadAsync(RecognitionRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(Answer(request));
    }
}

internal sealed class FakeRecognitionSignal : IRecognitionSignal
{
    public int Wakes { get; private set; }
    public void Wake() => Wakes++;
}

internal sealed class InMemoryImageStore : IImageStore
{
    public Dictionary<Guid, byte[]> Files { get; } = [];

    /// <summary>The folder each saved file went into (null: the data folder itself).</summary>
    public Dictionary<Guid, string?> Folders { get; } = [];
    public bool FailSaves { get; set; }
    public Task SaveAsync(StoredImage image, ReadOnlyMemory<byte> data, CancellationToken ct) { Files[image.Id] = data.ToArray(); Folders[image.Id] = image.Folder; return Task.CompletedTask; }
    public Task<Stream?> OpenReadAsync(StoredImage image, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(image.Id, out var d) ? new MemoryStream(d) : null);
    public Task DeleteAsync(StoredImage image, CancellationToken ct) { Files.Remove(image.Id); Folders.Remove(image.Id); return Task.CompletedTask; }
    public bool FailMoves { get; set; }
    public Task MoveAsync(StoredImage image, string folder, CancellationToken ct)
    {
        if (FailMoves) throw new IOException("disk full");
        Folders[image.Id] = folder;
        return Task.CompletedTask;
    }
    public Task DeleteFolderAsync(string folder, CancellationToken ct)
    {
        foreach (var id in Folders.Where(f => f.Value == folder || (f.Value?.StartsWith(folder + "/") ?? false)).Select(f => f.Key).ToList())
        {
            Files.Remove(id);
            Folders.Remove(id);
        }
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryImages : IImageRepository
{
    public Dictionary<Guid, StoredImage> Items { get; } = [];
    public bool FailAdds { get; set; }
    public Task AddAsync(StoredImage image, CancellationToken ct)
    {
        if (FailAdds) throw new InvalidOperationException("database down");
        Items[image.Id] = image;
        return Task.CompletedTask;
    }
    public Task<StoredImage?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(id));
    public Task RemoveAsync(Guid id, CancellationToken ct) { Items.Remove(id); return Task.CompletedTask; }
    public Task UpdateFolderAsync(StoredImage image, CancellationToken ct) { Items[image.Id] = image; return Task.CompletedTask; }
    public Task RemoveFolderAsync(string folder, CancellationToken ct)
    {
        foreach (var id in Items.Where(i => i.Value.Folder == folder || (i.Value.Folder?.StartsWith(folder + "/") ?? false)).Select(i => i.Key).ToList()) Items.Remove(id);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryTokens : IPasswordResetTokenRepository
{
    public List<PasswordResetToken> Items { get; } = [];
    public Task<PasswordResetToken?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(t => t.Id == id));
    public Task AddAsync(PasswordResetToken t, CancellationToken ct) { Items.Add(t); return Task.CompletedTask; }
    public Task UpdateAsync(PasswordResetToken t, CancellationToken ct) => Task.CompletedTask;
    public Task RemoveForUserAsync(Guid userId, CancellationToken ct) { Items.RemoveAll(t => t.UserId == userId); return Task.CompletedTask; }
}

internal sealed class InMemoryGrants : IAccessGrantRepository
{
    public List<AccessGrant> Items { get; } = [];
    public Task<IReadOnlyList<AccessGrant>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AccessGrant>>(Items.ToList());
    public Task<IReadOnlyList<AccessGrant>> ListForGranteeAsync(Guid g, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AccessGrant>>(Items.Where(x => x.GranteeId == g).ToList());
    public Task<AccessGrant?> FindAsync(Guid o, Guid g, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(x => x.OwnerId == o && x.GranteeId == g));
    public Task AddAsync(AccessGrant g, CancellationToken ct) { Items.Add(g); return Task.CompletedTask; }
    public Task UpdateAsync(AccessGrant g, CancellationToken ct) => Task.CompletedTask;
    public Task RemoveAsync(AccessGrant g, CancellationToken ct) { Items.Remove(g); return Task.CompletedTask; }
}

internal sealed class InMemorySettings : IAccessSettingsRepository
{
    public AccessSettings Value { get; } = AccessSettings.Default();
    public Task<AccessSettings> GetAsync(CancellationToken ct) => Task.FromResult(Value);
    public Task SaveAsync(AccessSettings s, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public Principal? Principal { get; set; }
    public Task<Principal?> GetPrincipalAsync(CancellationToken ct) => Task.FromResult(Principal);
    public void SignInAs(User user) => Principal = new Principal(user.Id, user.DisplayName, user.Email, user.IsAdmin);
}

internal sealed class FakeHasher : IPasswordHasher
{
    public string Hash(string password) => "hash:" + password;
    public bool Verify(string hash, string password) => hash == "hash:" + password;
}

internal sealed class FakeEmail(bool configured = false) : IEmailSender
{
    public bool IsConfigured { get; } = configured;
    public List<(string To, string Subject, string Body)> Sent { get; } = [];
    public Task SendAsync(string to, string subject, string body, CancellationToken ct) { Sent.Add((to, subject, body)); return Task.CompletedTask; }
}

/// <summary>Wires the real application services to in-memory ports.</summary>
internal sealed class World
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    public InMemoryVehicles Vehicles { get; } = new();
    public InMemoryRefuelings Refuelings { get; } = new();
    public InMemoryExpenses Expenses { get; } = new();
    public InMemoryCharts Charts { get; } = new();
    public InMemoryUsers Users { get; } = new();
    public FakeUserData UserData { get; } = new();
    public ImportSessionStore ImportSessions { get; }
    public InMemoryTokens Tokens { get; } = new();
    public InMemoryGrants Grants { get; } = new();
    public InMemoryResourceGrants ResourceGrants { get; } = new();
    public InMemoryImageStore ImageStore { get; } = new();
    public InMemoryImages Images { get; } = new();
    public InMemoryLogPhotos LogPhotos { get; } = new();
    public InMemoryPhotoDrafts PhotoDrafts { get; } = new();
    public InMemorySettings Settings { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakeEmail Email { get; }
    public AuthOptions Options { get; }
    public VehicleDefaultsOptions Defaults { get; } = new() { Currency = "HUF" };

    public AccessService Access { get; }
    public LogAccessGuard LogGuard { get; }
    public VehicleService VehicleService { get; }
    public RefuelingService RefuelingService { get; }
    public ExpenseService ExpenseService { get; }
    public StatsService Stats { get; }
    public InMemoryRecurring Recurring { get; }
    public InMemoryNotifications Notifications { get; } = new();
    public NotificationOptions NotificationOptions { get; } = new();
    public RecurringExpenseService RecurringService { get; }
    public ChartService ChartService { get; }
    public ImportService Imports { get; }
    public OdometerService Odometer { get; }
    public ResourceSharingService Sharing { get; }
    public ImageService ImageService { get; }
    public LogPhotoService Photos { get; }
    public PhotoDraftService Drafts { get; }
    public AuthService Auth { get; }
    public UserService UserService { get; }
    public AccessAdminService AccessAdmin { get; }
    public Notifier Notifier { get; }
    public RecognitionOptions RecognitionOptions { get; } = new() { Provider = "Reader", Reader = { BaseUrl = "http://reader:8081", ApiKey = "key" } };
    public FakeRecognitionProvider Recognizer { get; } = new();
    public InMemoryPhotoReadings Readings { get; } = new();
    public FakeRecognitionSignal Signal { get; } = new();
    public RecognitionAvailability Availability { get; }
    public RecognitionService Recognition => new(Availability, new RecognitionSetup(RecognitionOptions.Create()), Readings, PhotoDrafts, RefuelingService, Defaults.Create(), Signal, Access, Clock);
    public PhotoReadingProcessor Processor => new(Recognizer, Availability, new RecognitionSetup(RecognitionOptions.Create()), Readings, Images, ImageStore, Clock);
    public NotificationService NotificationService => new(Access, Notifications, new RecurringNotificationSync(Access, Recurring, Vehicles, RecurringService, Notifier), NotificationOptions.Create(), Clock); // a new one per use, like one per request (it syncs once)

    public World(AuthMode mode = AuthMode.Standalone, bool smtp = false, Action<AuthOptions>? configure = null)
    {
        Email = new FakeEmail(smtp);
        Options = new AuthOptions { Mode = mode, PublicUrl = "https://tank.test" };
        configure?.Invoke(Options);
        var options = Options.Create();

        Recurring = new InMemoryRecurring(Vehicles);
        Access = new AccessService(Current, options, Grants, Settings, ResourceGrants);
        Notifier = new Notifier(Notifications, NotificationOptions.Create(), Clock);
        LogGuard = new LogAccessGuard(Vehicles, Access);
        ImportSessions = new ImportSessionStore(Clock);
        Odometer = new OdometerService(new InMemoryReadings(Refuelings, Expenses));
        var resets = new PasswordResetService(Tokens, Users, Email, options, Clock);
        var logPhotoAccess = new LogPhotoAccess(LogGuard, Expenses, Refuelings, LogPhotos);
        ImageService = new ImageService(ImageStore, Images, Users, Vehicles, Access, logPhotoAccess, PhotoDrafts, Clock);
        Drafts = new PhotoDraftService(LogGuard, PhotoDrafts, ImageService, Access, Clock);
        Photos = new LogPhotoService(logPhotoAccess, LogPhotos, ImageService, Drafts, Access, Clock);
        VehicleService = new VehicleService(Vehicles, Refuelings, Access, Odometer, ImageService, Clock);
        RefuelingService = new RefuelingService(Vehicles, LogGuard, Refuelings, Access, Odometer, Photos, Clock);
        ExpenseService = new ExpenseService(LogGuard, Expenses, Access, Odometer, Photos, Clock);
        RecurringService = new RecurringExpenseService(LogGuard, Recurring, Access, Odometer, ExpenseService, Defaults.Create(), Clock);
        Imports = new ImportService([new FuelioCsvParser()], ImportSessions, Access, VehicleService, RefuelingService, ExpenseService, RecurringService, Refuelings, Expenses, Defaults.Create());
        Stats = new StatsService(Vehicles, new InMemoryStats(Refuelings, Expenses), Access, Clock);
        ChartService = new ChartService(Vehicles, Charts, Access, Clock);
        Sharing = new ResourceSharingService(Vehicles, ResourceGrants, Users, Access, Notifier);
        Auth = new AuthService(Users, new FakeHasher(), resets, Access, options, Clock);
        UserService = new UserService(Access, Users, UserData, resets, new FakeHasher(), ImageService, ImportSessions, options);
        AccessAdmin = new AccessAdminService(Access, Settings, Grants, Users, Notifier);
        Availability = new RecognitionAvailability(Recognizer, Clock);
    }

    public User AddUser(string email, bool admin = false, string password = "password-123456")
    {
        var user = User.CreateLocal(email, null, admin);
        user.SetPasswordHash(new FakeHasher().Hash(password));
        Users.Items.Add(user);
        return user;
    }
}

internal static class OptionsExtensions
{
    public static IOptions<T> Create<T>(this T value) where T : class => Options.Create(value);
}

/// <summary>Short forms so tests state only what they are about.</summary>
internal static class ServiceExtensions
{
    public static Task<Vehicle> AddAsync(this VehicleService s, string name, string? plate, FuelType fuel, CancellationToken ct) =>
        s.AddAsync(name, plate, fuel, MeasurementUnits.Metric, ct);

    public static Task<Vehicle> UpdateAsync(this VehicleService s, Guid id, string name, string? plate, FuelType fuel, CancellationToken ct) =>
        s.UpdateAsync(id, name, plate, fuel, null, ct);

    public static Task<Refueling> LogAsync(
        this RefuelingService s, Guid vehicleId, DateOnly date, decimal volume, decimal cost, long odometer, bool full, CancellationToken ct) =>
        s.LogAsync(vehicleId, new RefuelingInput(date, volume, cost, "EUR", odometer, full, null), ct);
}
