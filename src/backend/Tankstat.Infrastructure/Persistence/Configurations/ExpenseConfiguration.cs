using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.ToTable("Expenses");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Title).HasMaxLength(Expense.MaxTitleLength).IsRequired();
        b.Property(e => e.Category).HasMaxLength(Expense.MaxCategoryLength);
        b.Property(e => e.Note).HasMaxLength(Expense.MaxNoteLength);
        b.HasOne<Vehicle>().WithMany().HasForeignKey(e => e.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(e => new { e.VehicleId, e.Date });
        b.HasIndex(e => e.OwnerId);

        // The cost (always) and the odometer reading (when noted) are owned by the expense; removing expenses for good removes them in code.
        b.HasOne(e => e.Cost).WithOne().HasForeignKey<Expense>(e => e.CostId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.OdometerReading).WithOne().HasForeignKey<Expense>(e => e.OdometerReadingId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        b.Property(e => e.DeletedAt).HasConversion(
            v => v == null ? (DateTime?)null : v.Value.UtcDateTime,
            v => v == null ? null : new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)));
        b.HasIndex(e => e.DeletedAt);
        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
