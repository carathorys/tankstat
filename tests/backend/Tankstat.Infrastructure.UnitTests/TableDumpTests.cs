using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Infrastructure.Backups;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

public class TableDumpTests
{
    [Fact]
    public async Task ADateColumn_ThatAConnectorHandsBackAsADateTime_IsReadAsTheDate()
    {
        // Oracle's MySQL connector (and SQL Server's, when asked for a plain value) returns a `date` as a DateTime, and the first one
        // throws when EF asks it for a DateOnly: found by backing up a real MySQL server.
        await using var db = new TestDatabase();
        await using var context = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var date = TableDump.Tables(context.Model).Single(t => t.Name == "Refuelings").Columns.Single(c => c.Name == "Date");
        var rows = new DataTable();
        rows.Columns.Add("Date", typeof(DateTime));
        rows.Rows.Add(new DateTime(2026, 9, 1));
        using var reader = rows.CreateDataReader();
        reader.Read();

        Assert.Equal(new DateOnly(2026, 9, 1), date.Read(reader, 0));
    }
}
