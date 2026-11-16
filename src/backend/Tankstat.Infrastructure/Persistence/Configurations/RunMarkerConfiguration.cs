using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Runs;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class RunMarkerConfiguration : IEntityTypeConfiguration<RunMarker>
{
    public void Configure(EntityTypeBuilder<RunMarker> b)
    {
        b.ToTable("RunMarkers");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.End).HasConversion<string>().HasMaxLength(20);
        b.Ignore(m => m.IsOpen);
        b.HasMany(m => m.Vehicles).WithOne().HasForeignKey(v => v.RunId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(m => m.Vehicles).HasField("_vehicles").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(m => m.EndedAt); // the gate asks for the open ones
        // Stored as UTC date-times so comparisons translate on every provider (same as the other timestamps).
        b.Property(m => m.StartedAt).HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        foreach (var property in new[] { nameof(RunMarker.EndedAt), nameof(RunMarker.CancelRequestedAt) })
            b.Property<DateTimeOffset?>(property).HasConversion(
                v => v == null ? (DateTime?)null : v.Value.UtcDateTime,
                v => v == null ? null : new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)));
    }
}

internal sealed class RunMarkerVehicleConfiguration : IEntityTypeConfiguration<RunMarkerVehicle>
{
    public void Configure(EntityTypeBuilder<RunMarkerVehicle> b)
    {
        b.ToTable("RunMarkerVehicles");
        b.HasKey(v => new { v.RunId, v.VehicleId });
        b.HasIndex(v => v.VehicleId);
    }
}
