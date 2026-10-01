using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Migrations.MySql;

/// <summary>Used only by <c>dotnet ef</c> to generate migrations; never connects to a database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL("Server=localhost;Database=design", o => o.MigrationsAssembly("Tankstat.Migrations.MySql"))
            .Options);
}
