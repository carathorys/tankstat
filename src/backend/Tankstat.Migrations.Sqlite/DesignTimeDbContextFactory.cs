using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Migrations.Sqlite;

/// <summary>Used only by <c>dotnet ef</c> to generate migrations; never connects to a database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=design-time.db", o => o.MigrationsAssembly("Tankstat.Migrations.Sqlite"))
            .Options);
}
