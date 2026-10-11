using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Backups;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class InstanceConfiguration : IEntityTypeConfiguration<Instance>
{
    public void Configure(EntityTypeBuilder<Instance> b)
    {
        b.ToTable("Instance");
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).ValueGeneratedNever();
        // Stored as UTC date-times like the other timestamps.
        foreach (var property in new[] { nameof(Instance.RestoredAt), nameof(Instance.RestorePoint) })
            b.Property<DateTimeOffset?>(property).HasConversion(
                v => v == null ? (DateTime?)null : v.Value.UtcDateTime,
                v => v == null ? null : new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)));
    }
}
