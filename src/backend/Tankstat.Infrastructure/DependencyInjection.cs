using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Pooled factory: usable from scoped code and from GraphQL resolvers (which may run in parallel).
        services.AddPooledDbContextFactory<AppDbContext>((sp, builder) =>
        {
            var db = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            switch (db.Provider)
            {
                case DatabaseProvider.Sqlite: builder.UseSqlite(db.ConnectionString); break;
                case DatabaseProvider.PostgreSql: builder.UseNpgsql(db.ConnectionString); break;
                case DatabaseProvider.SqlServer: builder.UseSqlServer(db.ConnectionString); break;
                case DatabaseProvider.MySql: builder.UseMySQL(db.ConnectionString); break;
                default: throw new InvalidOperationException($"Unsupported database provider '{db.Provider}'.");
            }
        });

        services.AddSingleton<IDatabaseProbe, EfDatabaseProbe>();

        return services;
    }
}
