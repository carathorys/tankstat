using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Imports;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class ImportRunConfiguration : IEntityTypeConfiguration<ImportRun>
{
    public void Configure(EntityTypeBuilder<ImportRun> b)
    {
        b.ToTable("ImportRuns");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.State).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Failure).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Server).HasMaxLength(ImportRun.MaxServerLength);
        b.Property(r => r.Plan).HasMaxLength(ImportRun.MaxJsonLength);
        b.Property(r => r.Report).HasMaxLength(ImportRun.MaxJsonLength);
        b.Ignore(r => r.IsActive);
        b.HasIndex(r => new { r.State, r.QueuedAt });
        b.HasIndex(r => r.StartedById);
        // Stored as UTC date-times so comparisons translate on every provider (same as the other timestamps).
        foreach (var property in new[] { nameof(ImportRun.QueuedAt), nameof(ImportRun.SeenAt) })
            b.Property<DateTimeOffset>(property).HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        foreach (var property in new[] { nameof(ImportRun.StartedAt), nameof(ImportRun.EndedAt) })
            b.Property<DateTimeOffset?>(property).HasConversion(
                v => v == null ? (DateTime?)null : v.Value.UtcDateTime,
                v => v == null ? null : new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)));
    }
}
