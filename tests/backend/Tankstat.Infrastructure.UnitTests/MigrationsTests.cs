using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Backups;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>Fails when the entity model changed but one provider's migration set was not regenerated.</summary>
public class MigrationsTests
{
    public static TheoryData<DatabaseProvider> Providers => new(Enum.GetValues<DatabaseProvider>());

    private static AppDbContext Context(DatabaseProvider provider)
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
        return new AppDbContext(builder.Options);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ModelMatchesLatestMigrationSnapshot(DatabaseProvider provider)
    {
        using var db = Context(provider);

        Assert.NotEmpty(db.Database.GetMigrations());
        Assert.False(db.Database.HasPendingModelChanges(), $"{provider}: run `mise run db:migration` to add a migration.");
    }

    [Fact]
    public void EveryProviderHasTheSameMigrationsByNameAndOrder_ThoughNotWithTheSameTimestamps()
    {
        // A backup is compared with the database by the name of its last migration (MigrationNames), which is what lets it move from one
        // provider to another: the four sets must be the same list of names. Their timestamps are not (they are made one after the other).
        string[] Ids(DatabaseProvider provider)
        {
            using var db = Context(provider);
            return [.. db.Database.GetMigrations()];
        }
        var sqlite = Ids(DatabaseProvider.Sqlite);

        foreach (var provider in Enum.GetValues<DatabaseProvider>())
        {
            var ids = Ids(provider);
            Assert.True(
                sqlite.Select(MigrationNames.Of).SequenceEqual(ids.Select(MigrationNames.Of)),
                $"{provider}: its migrations are not the same, by name and in order, as SQLite's: add every migration with `mise run db:migration`.");
            Assert.Equal(ids.Length, ids.Select(MigrationNames.Of).Distinct().Count()); // a name tells one migration
        }
        Assert.NotEqual(sqlite, Ids(DatabaseProvider.PostgreSql)); // the ids themselves do differ, which is why the comparison is by name
    }
}
