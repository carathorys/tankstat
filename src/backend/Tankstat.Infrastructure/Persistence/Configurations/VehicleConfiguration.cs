using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> b)
    {
        b.ToTable("Vehicles");
        b.HasKey(v => v.Id);
        b.Property(v => v.Id).ValueGeneratedNever();
        b.HasIndex(v => v.OwnerId);

        // Stored as UTC date-time so comparisons ("deleted before ...") translate on every provider, SQLite included.
        b.Property(v => v.DeletedAt).HasConversion(
            v => v == null ? (DateTime?)null : v.Value.UtcDateTime,
            v => v == null ? null : new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)));
        b.HasIndex(v => v.DeletedAt);

        // Trashed vehicles are invisible unless a query explicitly opts out with IgnoreQueryFilters().
        b.HasQueryFilter(v => v.DeletedAt == null);
        b.Property(v => v.Name).HasMaxLength(100).IsRequired();
        b.Property(v => v.LicensePlate).HasMaxLength(20);
        b.Property(v => v.FuelType).HasConversion<string>().HasMaxLength(20);
    }
}
