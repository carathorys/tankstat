using Microsoft.EntityFrameworkCore;
using Tankstat.Domain.Access;
using Tankstat.Domain.Charts;
using Tankstat.Domain.Images;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Recognition;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Refueling> Refuelings => Set<Refueling>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();
    public DbSet<VehicleChart> VehicleCharts => Set<VehicleChart>();
    public DbSet<OdometerReading> OdometerReadings => Set<OdometerReading>();
    public DbSet<Cost> Costs => Set<Cost>();
    public DbSet<StoredImage> Images => Set<StoredImage>();
    public DbSet<User> Users => Set<User>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<AccessGrant> AccessGrants => Set<AccessGrant>();
    public DbSet<AccessSettings> AccessSettings => Set<AccessSettings>();
    public DbSet<ResourceGrant> ResourceGrants => Set<ResourceGrant>();
    public DbSet<LogPhoto> LogPhotos => Set<LogPhoto>();
    public DbSet<PhotoDraft> PhotoDrafts => Set<PhotoDraft>();
    public DbSet<PhotoReading> PhotoReadings => Set<PhotoReading>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
