using Microsoft.EntityFrameworkCore;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>Fails when the entity model changed but one provider's migration set was not regenerated.</summary>
public class MigrationsTests
{
    public static TheoryData<DatabaseProvider> Providers => new(Enum.GetValues<DatabaseProvider>());

    [Theory]
    [MemberData(nameof(Providers))]
    public void ModelMatchesLatestMigrationSnapshot(DatabaseProvider provider)
    {
        var migrations = $"Tankstat.Migrations.{provider}";
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        _ = provider switch
        {
            DatabaseProvider.Sqlite => builder.UseSqlite("Data Source=x.db", o => o.MigrationsAssembly(migrations)),
            DatabaseProvider.PostgreSql => builder.UseNpgsql("Host=x", o => o.MigrationsAssembly(migrations)),
            DatabaseProvider.SqlServer => builder.UseSqlServer("Server=x", o => o.MigrationsAssembly(migrations)),
            DatabaseProvider.MySql => builder.UseMySQL("Server=x", o => o.MigrationsAssembly(migrations)),
            _ => throw new NotSupportedException(provider.ToString()),
        };
        using var db = new AppDbContext(builder.Options);

        Assert.NotEmpty(db.Database.GetMigrations());
        Assert.False(db.Database.HasPendingModelChanges(), $"{provider}: run `mise run db:migration` to add a migration.");
    }
}
