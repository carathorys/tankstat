using Microsoft.EntityFrameworkCore;
using Tankstat.Infrastructure.Backups;
using Tankstat.Infrastructure.Backups.Reader;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>The reader's allowlist of tables (#336) knows every table of every migration: a new or renamed one is sorted when it is added.</summary>
public class ScratchTablesTests
{
    [Fact]
    public void EveryTableOfEveryMigration_IsEitherLoadedOrNeverLoaded()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:", o => o.MigrationsAssembly("Tankstat.Migrations.Sqlite")).Options);
        var unsorted = MigrationModels.Ids(db)
            .SelectMany(id => TableDump.Tables(MigrationModels.ModelOf(db, id)).Select(t => t.Name))
            .Concat(TableDump.Tables(db.Model).Select(t => t.Name))
            .Distinct().Where(t => !ScratchTables.IsKnown(t)).Order().ToList();

        Assert.True(unsorted.Count == 0, $"Neither loaded nor left out by the backup reader (ScratchTables): {string.Join(", ", unsorted)}");
        Assert.Empty(ScratchTables.Loaded.Intersect(ScratchTables.Never));
    }
}
