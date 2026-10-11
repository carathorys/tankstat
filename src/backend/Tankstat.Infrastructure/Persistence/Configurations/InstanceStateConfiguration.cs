using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Instance;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class InstanceStateConfiguration : IEntityTypeConfiguration<InstanceState>
{
    public void Configure(EntityTypeBuilder<InstanceState> b)
    {
        b.ToTable("InstanceState");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.LastStartMode).HasMaxLength(InstanceState.MaxModeLength);
        // Stored as a UTC date-time so comparisons translate on every provider (same as the other timestamps).
        b.Property(s => s.LastStartedAt).HasConversion(
            v => v == null ? (DateTime?)null : v.Value.UtcDateTime, v => v == null ? null : new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)));
    }
}
