using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Backups;
using Tankstat.Infrastructure.Backups;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>The migrations of the release are found by name, and an older one's model gives the tables as that migration left them.</summary>
public sealed class MigrationModelsTests
{
    [Fact]
    public async Task AnOlderMigrationsModel_GivesItsTablesAndColumns_WithTheProvidersMappings()
    {
        await using var database = new TestDatabase();
        await using var db = await database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var ids = MigrationModels.Ids(db);
        var before = ids[ids.ToList().FindIndex(id => MigrationNames.Of(id) == "AddUiFormats") - 1];

        var tables = TableDump.Tables(MigrationModels.ModelOf(db, before));

        var ui = tables.Single(t => t.Name == "UiSettings");
        Assert.DoesNotContain(ui.Columns, c => c.Name is "TimeFormat" or "DateFormat");
        Assert.Contains(ui.Columns, c => c.Name == "Surface");
        Assert.All(tables.Where(t => t.Name != TableDump.MigrationsHistory).SelectMany(t => t.Columns), c => Assert.NotNull(c.Mapping));
        Assert.Equal(before, MigrationModels.IdOf(db, "20990101000000_" + MigrationNames.Of(before))); // another provider's timestamp, the same name
        Assert.Null(MigrationModels.IdOf(db, "20990101000000_AddSomethingNewer"));
    }
}
