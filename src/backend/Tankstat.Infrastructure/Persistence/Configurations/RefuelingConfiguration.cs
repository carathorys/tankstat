using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class RefuelingConfiguration : IEntityTypeConfiguration<Refueling>
{
    public void Configure(EntityTypeBuilder<Refueling> b)
    {
        b.ToTable("Refuelings");
        SyncMapping.MapUpdatedAt(b);
        b.HasIndex(r => new { r.VehicleId, r.UpdatedAt }); // the offline feed reads a vehicle's rows changed since a moment
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Version).HasDefaultValue(1); // rows from before versions were counted start at 1
        b.Property(r => r.Volume).HasPrecision(9, 3);
        b.Property(r => r.Consumption).HasPrecision(9, 3);
        b.HasOne<Vehicle>().WithMany().HasForeignKey(r => r.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => new { r.VehicleId, r.Date });

        // The odometer reading and the cost are owned by the log (one each, none while a photo of the log is still being read).
        // Removing logs for good removes them in code.
        b.HasOne(r => r.Cost).WithOne().HasForeignKey<Refueling>(r => r.CostId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.OdometerReading).WithOne().HasForeignKey<Refueling>(r => r.OdometerReadingId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        b.Property(r => r.ReviewState).HasConversion<string>().HasMaxLength(16).HasDefaultValue(ReviewState.None);
        b.Property(r => r.FilledFromPhoto).HasConversion<int>();
        b.HasIndex(r => r.OwnerId);
        b.Property(r => r.Note).HasMaxLength(Refueling.MaxNoteLength);

        // Stored as UTC date-time so comparisons translate on every provider (same as vehicles).
        b.Property(r => r.DeletedAt).HasConversion(
            v => v == null ? (DateTime?)null : v.Value.UtcDateTime,
            v => v == null ? null : new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)));
        b.HasIndex(r => r.DeletedAt);

        // Trashed logs are invisible unless a query explicitly opts out with IgnoreQueryFilters().
        b.HasQueryFilter(r => r.DeletedAt == null);
    }
}
