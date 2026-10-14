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
        b.Property(s => s.SetupCompletedVia).HasConversion<string>().HasMaxLength(InstanceState.MaxModeLength);
        b.Ignore(s => s.Setup);
        // Stored as UTC date-times so comparisons translate on every provider (same as the other timestamps).
        foreach (var property in new[]
                 {
                     nameof(InstanceState.LastStartedAt), nameof(InstanceState.RestoredAt), nameof(InstanceState.RestorePoint),
                     nameof(InstanceState.SetupPendingSince), nameof(InstanceState.SetupCompletedAt),
                 })
            b.Property<DateTimeOffset?>(property).HasConversion(
                v => v == null ? (DateTime?)null : v.Value.UtcDateTime,
                v => v == null ? null : new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)));
    }
}
