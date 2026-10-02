using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Expenses;
using Tankstat.Application.Photos;
using Tankstat.Domain.Photos;
using Tankstat.Application.Images;
using Tankstat.Application.Imports;
using Tankstat.Application.Odometers;
using Tankstat.Application.Sharing;
using Tankstat.Application.Stats;
using Tankstat.Domain.Charts;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Access;
using Tankstat.Domain.Images;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

internal sealed class InMemoryVehicles : IVehicleRepository
{
    public List<Vehicle> Items { get; } = [];

    /// <summary>The last query the service passed down (after normalization).</summary>
    public VehicleQuery? LastQuery { get; private set; }

    // Sorting itself is the database's job and is tested against the real repository; the fake orders by name.
    private IReadOnlyList<Vehicle> Page(IEnumerable<Vehicle> rows, VehicleQuery query)
    {
        LastQuery = query;
        return rows.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).Skip(query.Skip).Take(query.Take).ToList();
    }

    public Task<IReadOnlyList<Vehicle>> ListAsync(OwnerScope scope, VehicleQuery query, CancellationToken ct) =>
        Task.FromResult(Page(Items.Where(v => !v.IsDeleted && scope.Contains(v.OwnerId, v.Id)), query));
    public Task<int> CountAsync(OwnerScope scope, CancellationToken ct) =>
        Task.FromResult(Items.Count(v => !v.IsDeleted && scope.Contains(v.OwnerId, v.Id)));
    public Task<IReadOnlyList<Vehicle>> ListDeletedAsync(OwnerScope scope, VehicleQuery query, CancellationToken ct) =>
        Task.FromResult(Page(Items.Where(v => v.IsDeleted && scope.Contains(v.OwnerId, v.Id)), query));
    public Task<int> CountDeletedAsync(OwnerScope scope, CancellationToken ct) =>
        Task.FromResult(Items.Count(v => v.IsDeleted && scope.Contains(v.OwnerId, v.Id)));
    public Task<Vehicle?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(v => v.Id == id && !v.IsDeleted));
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

    /// <summary>Runs right after a photo was added: lets a test put in other photos "at the same moment".</summary>
    public Action<LogPhoto>? AfterAdd { get; set; }
    public Task<IReadOnlyList<LogPhoto>> ListForLogAsync(LogType logType, Guid logId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LogPhoto>>(Items.Where(p => p.LogType == logType && p.LogId == logId).OrderBy(p => p.CreatedAt).ToList());
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
    public Task<bool> OwnsDataAsync(Guid userId, CancellationToken ct) => Task.FromResult(Owners.Contains(userId));
    public Task<IReadOnlyList<Guid>> DeleteUserAsync(Guid userId, Guid? moveDataTo, CancellationToken ct)
    {
        Deleted.Add((userId, moveDataTo));
        return Task.FromResult<IReadOnlyList<Guid>>(PurgedPictures.ToList());
    }
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
    public InMemorySettings Settings { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakeEmail Email { get; }
    public AuthOptions Options { get; }

    public AccessService Access { get; }
    public VehicleService VehicleService { get; }
    public RefuelingService RefuelingService { get; }
    public ExpenseService ExpenseService { get; }
    public StatsService Stats { get; }
    public ChartService ChartService { get; }
    public ImportService Imports { get; }
    public OdometerService Odometer { get; }
    public ResourceSharingService Sharing { get; }
    public ImageService ImageService { get; }
    public LogPhotoService Photos { get; }
    public AuthService Auth { get; }
    public UserService UserService { get; }
    public AccessAdminService AccessAdmin { get; }

    public World(AuthMode mode = AuthMode.Standalone, bool smtp = false, Action<AuthOptions>? configure = null)
    {
        Email = new FakeEmail(smtp);
        Options = new AuthOptions { Mode = mode, PublicUrl = "https://tank.test" };
        configure?.Invoke(Options);
        var options = Options.Create();

        Access = new AccessService(Current, options, Grants, Settings, ResourceGrants);
        ImportSessions = new ImportSessionStore(Clock);
        Odometer = new OdometerService(new InMemoryReadings(Refuelings, Expenses));
        var resets = new PasswordResetService(Tokens, Users, Email, options, Clock);
        var logPhotoAccess = new LogPhotoAccess(Access, Vehicles, Expenses, Refuelings, LogPhotos);
        ImageService = new ImageService(ImageStore, Images, Users, Vehicles, Access, logPhotoAccess, Clock);
        Photos = new LogPhotoService(logPhotoAccess, LogPhotos, ImageService, Access, Clock);
        VehicleService = new VehicleService(Vehicles, Refuelings, Access, Odometer, ImageService, Clock);
        RefuelingService = new RefuelingService(Vehicles, Refuelings, Access, Odometer, Photos, Clock);
        ExpenseService = new ExpenseService(Vehicles, Expenses, Access, Odometer, Photos, Clock);
        Imports = new ImportService([new FuelioCsvParser()], ImportSessions, Access, VehicleService, RefuelingService, ExpenseService, Refuelings, Expenses, new VehicleDefaultsOptions { Currency = "HUF" }.Create());
        Stats = new StatsService(Vehicles, new InMemoryStats(Refuelings, Expenses), Access, Clock);
        ChartService = new ChartService(Vehicles, Charts, Access, Clock);
        Sharing = new ResourceSharingService(Vehicles, ResourceGrants, Users, Access);
        Auth = new AuthService(Users, new FakeHasher(), resets, Access, options, Clock);
        UserService = new UserService(Access, Users, UserData, resets, new FakeHasher(), ImageService, ImportSessions, options);
        AccessAdmin = new AccessAdminService(Access, Settings, Grants, Users);
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
