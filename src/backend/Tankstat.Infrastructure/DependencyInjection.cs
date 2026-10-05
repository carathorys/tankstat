using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Expenses;
using Tankstat.Application.Images;
using Tankstat.Application.Notifications;
using Tankstat.Application.Odometers;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Stats;
using Tankstat.Application.Photos;
using Tankstat.Application.Recognition;
using Tankstat.Application.Users;
using Tankstat.Infrastructure.Auth;
using Tankstat.Infrastructure.Recognition;
using Tankstat.Infrastructure.Storage;
using Tankstat.Application.Vehicles;
using Tankstat.Infrastructure.Persistence;
using Tankstat.Infrastructure.Persistence.Repositories;

namespace Tankstat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .PostConfigure(o =>
            {
                if (string.IsNullOrWhiteSpace(o.ConnectionString) && o.Provider == DatabaseProvider.Sqlite)
                    o.ConnectionString = DatabaseOptions.DefaultSqliteConnectionString;
            })
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                "Database:ConnectionString is required unless Database:Provider is Sqlite.")
            .ValidateOnStart();

        // Pooled factory: usable from scoped code and from GraphQL resolvers (which may run in parallel).
        services.AddPooledDbContextFactory<AppDbContext>((sp, builder) =>
        {
            var db = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            // Each provider keeps its migrations in its own assembly (Tankstat.Migrations.<Provider>).
            var migrations = $"Tankstat.Migrations.{db.Provider}";
            // Refuelings only ever load through their (query-filtered) vehicle, so the filter on the required end is intended.
            builder.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
            switch (db.Provider)
            {
                case DatabaseProvider.Sqlite:
                    builder.UseSqlite(db.ConnectionString!, o => o.MigrationsAssembly(migrations)).AddInterceptors(SqliteUnicodeLower.Instance);
                    break;
                case DatabaseProvider.PostgreSql: builder.UseNpgsql(db.ConnectionString!, o => o.MigrationsAssembly(migrations)); break;
                case DatabaseProvider.SqlServer: builder.UseSqlServer(db.ConnectionString!, o => o.MigrationsAssembly(migrations)); break;
                case DatabaseProvider.MySql: builder.UseMySQL(db.ConnectionString!, o => o.MigrationsAssembly(migrations)); break;
                default: throw new InvalidOperationException($"Unsupported database provider '{db.Provider}'.");
            }
        });

        services.AddSingleton<IDatabaseProbe, EfDatabaseProbe>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<IRefuelingRepository, RefuelingRepository>();
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<IRecurringExpenseRepository, RecurringExpenseRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IStatsRepository, StatsRepository>();
        services.AddScoped<IVehicleChartRepository, VehicleChartRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserDataRepository, UserDataRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IAccessGrantRepository, AccessGrantRepository>();
        services.AddScoped<IAccessSettingsRepository, AccessSettingsRepository>();
        services.AddScoped<IResourceGrantRepository, ResourceGrantRepository>();
        services.AddScoped<IOdometerReadingRepository, OdometerReadingRepository>();
        services.AddScoped<IImageRepository, ImageRepository>();
        services.AddScoped<ILogPhotoRepository, LogPhotoRepository>();
        services.AddScoped<IPhotoDraftRepository, PhotoDraftRepository>();
        services.AddScoped<IPhotoReadingRepository, PhotoReadingRepository>();
        services.AddSingleton<IImageStore, FileSystemImageStore>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        // Photo reading (optional): the model provider the settings choose, or one that never reads (RecognitionSetup explains why).
        // No client-wide timeout (its default of 100 s would cut OpenAiCompatible:TimeoutSeconds short): every request brings its own.
        services.AddHttpClient(OpenAiCompatibleRecognitionProvider.ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddHttpMessageHandler(sp => new OpenAiTrafficHandler(sp.GetRequiredService<RecognitionSetup>().Options.OpenAiCompatible, sp.GetRequiredService<ILogger<OpenAiTrafficHandler>>()));
        services.AddSingleton<IRecognitionProvider>(sp => sp.GetRequiredService<RecognitionSetup>() is { Enabled: true, Kind: RecognitionProviderKind.OpenAiCompatible } setup
            ? new OpenAiCompatibleRecognitionProvider(
                sp.GetRequiredService<IHttpClientFactory>(), setup.Options.OpenAiCompatible, setup.SystemPrompt, sp.GetRequiredService<ILogger<OpenAiCompatibleRecognitionProvider>>())
            : NullRecognitionProvider.Instance);
        services.AddSingleton<RecognitionSignal>();
        services.AddSingleton<IRecognitionSignal>(sp => sp.GetRequiredService<RecognitionSignal>());

        // Order matters: migrate first, then create the initial administrator; the photo reading worker needs the migrated database.
        services.AddHostedService<DatabaseMigrator>();
        services.AddHostedService<StandaloneBootstrapper>();
        services.AddHostedService<RecognitionWorker>();

        return services;
    }
}
