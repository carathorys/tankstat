using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class RecurringExpenseConfiguration : IEntityTypeConfiguration<RecurringExpense>
{
    public void Configure(EntityTypeBuilder<RecurringExpense> b)
    {
        b.ToTable("RecurringExpenses");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Title).HasMaxLength(RecurringExpense.MaxTitleLength).IsRequired();
        b.Property(r => r.Category).HasMaxLength(RecurringExpense.MaxCategoryLength);
        b.Property(r => r.Note).HasMaxLength(RecurringExpense.MaxNoteLength);
        b.Property(r => r.Kind).HasConversion<string>().HasMaxLength(16);
        b.Ignore(r => r.UsesTime);
        b.Ignore(r => r.UsesDistance);
        b.HasOne<Vehicle>().WithMany().HasForeignKey(r => r.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => r.VehicleId);
        b.HasIndex(r => r.OwnerId);
    }
}
