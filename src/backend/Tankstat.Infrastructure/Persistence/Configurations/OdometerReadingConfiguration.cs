using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class OdometerReadingConfiguration : IEntityTypeConfiguration<OdometerReading>
{
    public void Configure(EntityTypeBuilder<OdometerReading> b)
    {
        b.ToTable("OdometerReadings");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.HasOne<Vehicle>().WithMany().HasForeignKey(r => r.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => new { r.VehicleId, r.Date });
        b.HasIndex(r => r.OwnerId);

        // Stored as UTC date-time so comparisons translate on every provider (same as vehicles and logs).
        b.Property(r => r.DeletedAt).HasConversion(
            v => v == null ? (DateTime?)null : v.Value.UtcDateTime,
            v => v == null ? null : new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)));

        // A trashed log trashes its reading, so neighbour checks never see it.
        b.HasQueryFilter(r => r.DeletedAt == null);
    }
}
