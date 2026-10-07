using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> b)
    {
        b.ToTable("Vehicles");
        SyncMapping.MapUpdatedAt(b);
        b.HasKey(v => v.Id);
        b.Property(v => v.Id).ValueGeneratedNever();
        b.Property(v => v.Version).HasDefaultValue(1); // rows from before versions were counted start at 1
        b.HasIndex(v => v.OwnerId);

        // The units of the vehicle's distance and volume, stored as two columns of the vehicle row.
        b.OwnsOne(v => v.Units, u =>
        {
            u.Property(x => x.Distance).HasColumnName("OdometerUnit").HasConversion<string>().HasMaxLength(20).HasDefaultValue(DistanceUnit.Kilometers);
            u.Property(x => x.Volume).HasColumnName("VolumeUnit").HasConversion<string>().HasMaxLength(20).HasDefaultValue(VolumeUnit.Liters);
        });
        b.Navigation(v => v.Units).IsRequired();

        // Stored as UTC date-time so comparisons ("deleted before ...") translate on every provider, SQLite included.
        b.Property(v => v.DeletedAt).HasConversion(
            v => v == null ? (DateTime?)null : v.Value.UtcDateTime,
            v => v == null ? null : new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)));
        b.HasIndex(v => v.DeletedAt);
        b.HasIndex(v => v.PictureImageId);

        // Trashed vehicles are invisible unless a query explicitly opts out with IgnoreQueryFilters().
        b.HasQueryFilter(v => v.DeletedAt == null);
        b.Property(v => v.Name).HasMaxLength(100).IsRequired();
        b.Property(v => v.LicensePlate).HasMaxLength(20);
        b.Property(v => v.FuelType).HasConversion<string>().HasMaxLength(20);
    }
}
