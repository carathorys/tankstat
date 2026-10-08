using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Settings;
using Tankstat.Domain.Sync;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Configurations;

/// <summary>The columns and tables behind what devices download for offline use (see <see cref="SyncInterceptor"/>).</summary>
internal static class SyncMapping
{
    /// <summary>What rows saved before <c>UpdatedAt</c> existed carry: a device downloads them in its first, full download anyway.</summary>
    public static readonly DateTimeOffset Epoch = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// <c>UpdatedAt</c> is stored as a UTC date-time, like <c>DeletedAt</c>, so comparisons translate on every provider. <c>Version</c> is a
    /// concurrency token: every update or delete is made only if the row still has the version the entity was loaded with
    /// (<see cref="SyncInterceptor"/> supplies it), else <c>sync.versionMismatch</c> (<see cref="AppDbContext"/>).
    /// </summary>
    public static void MapUpdatedAt<T>(EntityTypeBuilder<T> b) where T : class, ISynced
    {
        b.Property<DateTimeOffset>(nameof(ISynced.UpdatedAt))
            .HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)))
            .HasDefaultValue(Epoch);
        b.Property<int>(nameof(ISynced.Version)).IsConcurrencyToken();
        b.Ignore(nameof(ISynced.SavedVersion));
    }
}

internal sealed class TombstoneConfiguration : IEntityTypeConfiguration<Tombstone>
{
    public void Configure(EntityTypeBuilder<Tombstone> b)
    {
        b.ToTable("Tombstones");
        b.HasKey(t => t.Id);
        b.Property(t => t.EntityType).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.PurgedAt).HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        // No foreign key: the vehicle may be gone too. The feed reads a vehicle's tombstones since a moment; the sweep reads by age.
        b.HasIndex(t => new { t.VehicleId, t.PurgedAt });
        b.HasIndex(t => t.PurgedAt);
    }
}

internal sealed class OfflineSettingsConfiguration : IEntityTypeConfiguration<OfflineSettings>
{
    public void Configure(EntityTypeBuilder<OfflineSettings> b)
    {
        b.ToTable("OfflineSettings");
        b.HasKey(s => s.UserId); // no foreign key to Users: the anonymous user of Auth:Mode=None has no row
        b.Property(s => s.DefaultWindow).HasMaxLength(OfflineWindow.MaxLength);
        b.Property(s => s.UpdatedAt).HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
    }
}

internal sealed class OfflineVehicleSettingConfiguration : IEntityTypeConfiguration<OfflineVehicleSetting>
{
    public void Configure(EntityTypeBuilder<OfflineVehicleSetting> b)
    {
        b.ToTable("OfflineVehicleSettings");
        b.HasKey(s => new { s.UserId, s.VehicleId });
        b.Property(s => s.Window).HasMaxLength(OfflineWindow.MaxLength);
        b.HasOne<Vehicle>().WithMany().HasForeignKey(s => s.VehicleId).OnDelete(DeleteBehavior.Cascade); // a purged vehicle takes its setting along
    }
}

internal sealed class SyncChangeConfiguration : IEntityTypeConfiguration<SyncChange>
{
    public void Configure(EntityTypeBuilder<SyncChange> b)
    {
        b.ToTable("SyncChanges");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever(); // the device's change id
        b.Property(c => c.Kind).HasConversion<string>().HasMaxLength(32);
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(c => c.Payload).HasMaxLength(SyncChange.MaxPayloadLength).IsRequired();
        b.Property(c => c.ReasonKey).HasMaxLength(SyncChange.MaxReasonKeyLength);
        // The reason's arguments as one JSON object, like a notification's: only ever read together.
        b.Property(c => c.ReasonArgs)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>(),
                new ValueComparer<IReadOnlyDictionary<string, string>>(
                    (a, c) => SameArgs(a!, c!),
                    v => v.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
                    v => new Dictionary<string, string>(v)))
            .HasMaxLength(4000)
            .IsRequired();
        b.Property(c => c.ReceivedAt).HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        // A purged vehicle takes its changes along; the user columns have no foreign key, as everywhere.
        b.HasOne<Vehicle>().WithMany().HasForeignKey(c => c.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(c => new { c.VehicleId, c.Status });
        b.HasIndex(c => new { c.SubmittedById, c.Status });
        b.HasIndex(c => new { c.Status, c.ReceivedAt });
        b.HasIndex(c => c.OwnerId);
    }

    private static bool SameArgs(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var value) && value == kv.Value);
}
