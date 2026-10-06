using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Settings;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Configurations;

// The settings tables are keyed by the user id without a foreign key to Users: the anonymous user (authentication off) has no row there,
// and UserDataRepository deletes them with the user.

internal sealed class UiSettingsConfiguration : IEntityTypeConfiguration<UiSettings>
{
    public void Configure(EntityTypeBuilder<UiSettings> b)
    {
        b.ToTable("UiSettings");
        b.HasKey(s => s.UserId);
        b.Property(s => s.UserId).ValueGeneratedNever();
        b.Property(s => s.Language).HasMaxLength(UiSettings.MaxLanguageLength);
        // Stored as a UTC date-time so comparisons translate on every provider (same as the other timestamps).
        b.Property(s => s.UpdatedAt).HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
    }
}

internal sealed class GridSettingsConfiguration : IEntityTypeConfiguration<GridSettings>
{
    public void Configure(EntityTypeBuilder<GridSettings> b)
    {
        b.ToTable("GridSettings");
        b.HasKey(g => new { g.UserId, g.GridId });
        b.Property(g => g.GridId).HasMaxLength(GridSettings.MaxGridIdLength).IsRequired();
        // The column lists as JSON text, so the same mapping works on all four databases (like Notification.Args).
        ColumnList(b.Property(g => g.Order));
        ColumnList(b.Property(g => g.Hidden));
        b.Property(g => g.SortColumn).HasMaxLength(GridSettings.MaxColumnIdLength).IsRequired();
        b.Property(g => g.SortDirection).HasConversion<string>().HasMaxLength(32);
        b.Property(g => g.UpdatedAt).HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
    }

    private static void ColumnList(PropertyBuilder<IReadOnlyList<string>> property) =>
        property
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>(),
                new ValueComparer<IReadOnlyList<string>>(
                    (a, b) => a != null && b != null && a.SequenceEqual(b),
                    v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s)),
                    v => v.ToList()))
            .HasMaxLength(4000) // 50 ids of 40 characters, quoted and separated
            .IsRequired();
}

internal sealed class VehicleOrderConfiguration : IEntityTypeConfiguration<VehicleOrder>
{
    public void Configure(EntityTypeBuilder<VehicleOrder> b)
    {
        b.ToTable("VehicleOrders");
        b.HasKey(o => new { o.UserId, o.VehicleId });
        b.HasOne<Vehicle>().WithMany().HasForeignKey(o => o.VehicleId).OnDelete(DeleteBehavior.Cascade); // a purged vehicle takes its positions along
        b.HasIndex(o => new { o.UserId, o.Position });
    }
}
