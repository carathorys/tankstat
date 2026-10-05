using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Images;
using Tankstat.Domain.Recognition;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class PhotoReadingConfiguration : IEntityTypeConfiguration<PhotoReading>
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public void Configure(EntityTypeBuilder<PhotoReading> b)
    {
        b.ToTable("PhotoReadings");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Purpose).HasConversion<string>().HasMaxLength(16);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Locale).HasMaxLength(8).IsRequired();
        b.Property(r => r.Currency).HasMaxLength(3);
        b.Property(r => r.Provider).HasMaxLength(32);
        b.Property(r => r.ModelVersion).HasMaxLength(PhotoReading.MaxModelVersionLength);
        // The values as one JSON array: they are only ever read together with their reading, never queried.
        b.Property(r => r.Values)
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<ReadingValue>>(v, Json) ?? new List<ReadingValue>(),
                new ValueComparer<IReadOnlyList<ReadingValue>>(
                    (a, c) => a!.SequenceEqual(c!),
                    v => v.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)),
                    v => v.ToList()))
            .HasMaxLength(4000)
            .IsRequired();
        // Why the photo gave less than it might have, in the same way: codes only. Readings from before the column hold nothing, which reads as no issues.
        b.Property(r => r.Issues)
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => string.IsNullOrWhiteSpace(v) ? new List<ReadingIssue>() : JsonSerializer.Deserialize<List<ReadingIssue>>(v, Json) ?? new List<ReadingIssue>(),
                new ValueComparer<IReadOnlyList<ReadingIssue>>(
                    (a, c) => a!.SequenceEqual(c!),
                    v => v.Aggregate(0, (hash, issue) => HashCode.Combine(hash, issue)),
                    v => v.ToList()))
            .HasMaxLength(2000)
            .IsRequired();
        // Stored as UTC date-times so comparisons translate on every provider (same as the other timestamps).
        b.Property(r => r.CreatedAt).HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        b.Property(r => r.DueAt).HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        b.Property(r => r.ClaimedAt).HasConversion(
            v => v.HasValue ? v.Value.UtcDateTime : (DateTime?)null,
            v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null);
        b.Property(r => r.ReadAt).HasConversion(
            v => v.HasValue ? v.Value.UtcDateTime : (DateTime?)null,
            v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null);
        b.Ignore(r => r.AllowedKinds);

        // A reading belongs to its picture: whatever removes the picture (a discarded or expired draft, a removed photo, a purged vehicle
        // or user) removes the reading too, in the database. Unlike other pictures' owners it has no file of its own to clean up.
        b.HasOne<StoredImage>().WithOne().HasForeignKey<PhotoReading>(r => r.Id).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => new { r.Status, r.DueAt }); // the worker looks for due readings
    }
}
