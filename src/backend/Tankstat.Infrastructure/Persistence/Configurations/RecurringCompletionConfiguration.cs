using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Recurring;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class RecurringCompletionConfiguration : IEntityTypeConfiguration<RecurringCompletion>
{
    public void Configure(EntityTypeBuilder<RecurringCompletion> b)
    {
        b.ToTable("RecurringCompletions");
        b.HasKey(c => new { c.ExpenseId, c.RecurringExpenseId });
        // Only the schedule is a foreign key (a schedule is hard-deleted and takes its links along). The expense is not: expenses and
        // schedules both cascade from the vehicle, and a second cascading key would give one vehicle delete two paths to these rows, which
        // SQL Server refuses. Purging an expense removes its links in code, like its photos (ExpenseRepository.PurgeAsync).
        b.HasOne<RecurringExpense>().WithMany().HasForeignKey(c => c.RecurringExpenseId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(c => c.RecurringExpenseId);
    }
}
