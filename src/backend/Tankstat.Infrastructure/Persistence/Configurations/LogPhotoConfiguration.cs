using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class LogPhotoConfiguration : IEntityTypeConfiguration<LogPhoto>
{
    public void Configure(EntityTypeBuilder<LogPhoto> b)
    {
        b.ToTable("LogPhotos");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.LogType).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.CreatedAt).HasConversion(
            v => v.UtcDateTime,
            v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

        // The photos belong to the vehicle (deleting a vehicle deletes them); the log can be either table, so it has no foreign key
        // and its photo rows are removed with it in code. The image is not a foreign key either: its file has to be removed in code anyway.
        b.HasOne<Vehicle>().WithMany().HasForeignKey(p => p.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(p => new { p.LogType, p.LogId });
        b.HasIndex(p => p.ImageId).IsUnique();
        b.HasIndex(p => p.OwnerId);
    }
}
