using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class PhotoDraftConfiguration : IEntityTypeConfiguration<PhotoDraft>
{
    public void Configure(EntityTypeBuilder<PhotoDraft> b)
    {
        b.ToTable("PhotoDrafts");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.CreatedAt).HasConversion(
            v => v.UtcDateTime,
            v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

        // Drafts belong to the vehicle (deleting it deletes them, and its folder holds their files); the picture row is removed in code.
        b.HasOne<Vehicle>().WithMany().HasForeignKey(d => d.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(d => new { d.VehicleId, d.CreatedById });
        b.HasIndex(d => d.OwnerId);
        b.HasIndex(d => d.CreatedAt); // expired drafts are looked up across all vehicles
    }
}
