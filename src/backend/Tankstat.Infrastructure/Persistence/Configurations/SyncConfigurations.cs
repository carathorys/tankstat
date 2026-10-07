using Microsoft.EntityFrameworkCore;
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

    /// <summary>Stored as a UTC date-time, like <c>DeletedAt</c>, so comparisons translate on every provider.</summary>
    public static void MapUpdatedAt<T>(EntityTypeBuilder<T> b) where T : class, ISynced =>
        b.Property<DateTimeOffset>(nameof(ISynced.UpdatedAt))
            .HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)))
            .HasDefaultValue(Epoch);
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
