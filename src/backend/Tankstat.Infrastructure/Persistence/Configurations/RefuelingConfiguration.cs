using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class RefuelingConfiguration : IEntityTypeConfiguration<Refueling>
{
    public void Configure(EntityTypeBuilder<Refueling> b)
    {
        b.ToTable("Refuelings");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Volume).HasPrecision(9, 3);
        b.HasOne<Vehicle>().WithMany().HasForeignKey(r => r.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => new { r.VehicleId, r.Date });

        // The odometer reading and the cost are owned by the log (one each). Removing logs for good removes them in code.
        b.HasOne(r => r.Cost).WithOne().HasForeignKey<Refueling>(r => r.CostId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.OdometerReading).WithOne().HasForeignKey<Refueling>(r => r.OdometerReadingId).IsRequired().OnDelete(DeleteBehavior.Restrict);
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
