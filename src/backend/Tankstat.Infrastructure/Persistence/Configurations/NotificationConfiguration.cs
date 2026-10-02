using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Notifications;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.HasKey(n => n.Id);
        b.Property(n => n.Id).ValueGeneratedNever();
        b.Property(n => n.Kind).HasConversion<string>().HasMaxLength(32);
        b.Property(n => n.Topic).HasConversion<string>().HasMaxLength(32);
        b.Property(n => n.SubjectType).HasConversion<string>().HasMaxLength(32);
        b.Property(n => n.ContextType).HasConversion<string>().HasMaxLength(32);
        b.Property(n => n.Occurrence).HasMaxLength(Notification.MaxOccurrenceLength).IsRequired();
        b.Property(n => n.Before).HasMaxLength(Notification.MaxValueLength);
        // The display-ready arguments as one JSON object: they are only ever read together, never queried.
        b.Property(n => n.Args)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>(),
                new ValueComparer<IReadOnlyDictionary<string, string>>(
                    (a, c) => SameArgs(a!, c!),
                    v => v.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
                    v => new Dictionary<string, string>(v)))
            .HasMaxLength(4000)
            .IsRequired();
        // Stored as UTC date-times so comparisons translate on every provider (same as the other timestamps).
        b.Property(n => n.CreatedAt).HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        b.Property(n => n.UpdatedAt).HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        b.Property(n => n.ReadAt).HasConversion(
            v => v.HasValue ? v.Value.UtcDateTime : (DateTime?)null,
            v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null);
        b.Ignore(n => n.Subject);
        b.Ignore(n => n.Context);
        b.Ignore(n => n.IsRead);

        // The identity: a derived occurrence can exist only once, however many requests work it out at the same time.
        b.HasIndex(n => new { n.RecipientId, n.Topic, n.SubjectType, n.SubjectId, n.ContextId, n.Occurrence }).IsUnique();
        b.HasIndex(n => new { n.RecipientId, n.ReadAt, n.UpdatedAt });
    }

    private static bool SameArgs(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var value) && value == kv.Value);
}
