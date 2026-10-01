using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Images;
using Tankstat.Application.Odometers;
using Tankstat.Application.Sharing;
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
        return Task.FromResult(new PurgeResult(doomed.Count, doomed.Where(v => v.PictureImageId is not null).Select(v => v.PictureImageId!.Value).ToList()));
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
    public Task<IReadOnlyList<Refueling>> ListDeletedAsync(OwnerScope scope, RefuelingQuery query, CancellationToken ct) =>
        Task.FromResult(Page(Items.Where(r => r.IsDeleted && scope.Contains(r.OwnerId, r.VehicleId)), query));
    public Task<int> CountDeletedAsync(OwnerScope scope, CancellationToken ct) =>
        Task.FromResult(Items.Count(r => r.IsDeleted && scope.Contains(r.OwnerId, r.VehicleId)));
    public Task<Refueling?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(r => r.Id == id && !r.IsDeleted));
    public Task<Refueling?> FindIncludingDeletedAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(r => r.Id == id));
    public Task AddAsync(Refueling refueling, CancellationToken ct) { Items.Add(refueling); return Task.CompletedTask; }
    public Task UpdateAsync(Refueling refueling, CancellationToken ct) => Task.CompletedTask; // entities are shared references
    public Task<int> PurgeAsync(OwnerScope scope, CancellationToken ct) =>
        Task.FromResult(Items.RemoveAll(r => r.IsDeleted && scope.Contains(r.OwnerId, r.VehicleId)));
}

/// <summary>Readings are the ones owned by the in-memory logs (live ones only, like the real query filter).</summary>
internal sealed class InMemoryReadings(InMemoryRefuelings refuelings) : IOdometerReadingRepository
{
    private IEnumerable<OdometerReading> Live(Guid vehicleId) => refuelings.Items.Where(r => !r.IsDeleted && r.VehicleId == vehicleId).Select(r => r.OdometerReading);

    public Task<OdometerReading?> PreviousAsync(Guid vehicleId, DateOnly date, Guid? except, CancellationToken ct) =>
        Task.FromResult(Live(vehicleId).Where(r => r.Date < date && r.Id != except).OrderByDescending(r => r.Date).ThenByDescending(r => r.Value).FirstOrDefault());
    public Task<OdometerReading?> NextAsync(Guid vehicleId, DateOnly date, Guid? except, CancellationToken ct) =>
        Task.FromResult(Live(vehicleId).Where(r => r.Date > date && r.Id != except).OrderBy(r => r.Date).ThenBy(r => r.Value).FirstOrDefault());
    public Task<OdometerReading?> LatestAsync(Guid vehicleId, CancellationToken ct) =>
        Task.FromResult(Live(vehicleId).OrderByDescending(r => r.Date).ThenByDescending(r => r.Value).FirstOrDefault());
    public Task<bool> AnyAsync(Guid vehicleId, CancellationToken ct) => Task.FromResult(refuelings.Items.Any(r => r.VehicleId == vehicleId));
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

internal sealed class InMemoryImageStore : IImageStore
{
    public Dictionary<Guid, byte[]> Files { get; } = [];
    public bool FailSaves { get; set; }
    public Task SaveAsync(Guid id, ReadOnlyMemory<byte> data, CancellationToken ct) { Files[id] = data.ToArray(); return Task.CompletedTask; }
    public Task<Stream?> OpenReadAsync(Guid id, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(id, out var d) ? new MemoryStream(d) : null);
    public Task DeleteAsync(Guid id, CancellationToken ct) { Files.Remove(id); return Task.CompletedTask; }
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
}

internal sealed class InMemoryTokens : IPasswordResetTokenRepository
{
    public List<PasswordResetToken> Items { get; } = [];
    public Task<PasswordResetToken?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(t => t.Id == id));
    public Task AddAsync(PasswordResetToken t, CancellationToken ct) { Items.Add(t); return Task.CompletedTask; }
    public Task UpdateAsync(PasswordResetToken t, CancellationToken ct) => Task.CompletedTask;
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
    public InMemoryUsers Users { get; } = new();
    public InMemoryTokens Tokens { get; } = new();
    public InMemoryGrants Grants { get; } = new();
    public InMemoryResourceGrants ResourceGrants { get; } = new();
    public InMemoryImageStore ImageStore { get; } = new();
    public InMemoryImages Images { get; } = new();
    public InMemorySettings Settings { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakeEmail Email { get; }
    public AuthOptions Options { get; }

    public AccessService Access { get; }
    public VehicleService VehicleService { get; }
    public RefuelingService RefuelingService { get; }
    public OdometerService Odometer { get; }
    public ResourceSharingService Sharing { get; }
    public ImageService ImageService { get; }
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
        Odometer = new OdometerService(new InMemoryReadings(Refuelings));
        var resets = new PasswordResetService(Tokens, Users, Email, options, Clock);
        VehicleService = new VehicleService(Vehicles, Refuelings, Access, Odometer, ImageStore, Images, Clock);
        ImageService = new ImageService(ImageStore, Images, Users, Vehicles, Access, Clock);
        RefuelingService = new RefuelingService(Vehicles, Refuelings, Access, Odometer, Clock);
        Sharing = new ResourceSharingService(Vehicles, ResourceGrants, Users, Access);
        Auth = new AuthService(Users, new FakeHasher(), resets, Access, options, Clock);
        UserService = new UserService(Access, Users, resets, options);
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
