using HotChocolate.Execution;
using Tankstat.Api;
using Tankstat.Api.Auth;
using Tankstat.Api.Backups;
using Tankstat.Api.Media;
using Tankstat.Api.GraphQL;
using Tankstat.Api.Settings;
using Tankstat.Application;
using Tankstat.Application.SystemSettings;
using Tankstat.Infrastructure;
using Tankstat.Infrastructure.Settings;

// Build and run, and again when an administrator restarts the server (ServerRestart): in this process, never by exiting, since Docker's
// default restart policy leaves a container that exited cleanly stopped. Under WebApplicationFactory the first Build() throws
// HostAbortedException out of here, so nothing may catch around it (the using below lets go of the signals on the way out).
var restarts = new RestartRequests();
using var stopSignals = restarts.EndOnStopSignals(); // docker stop during a restart still ends the process
do
{
    restarts.Reset();
    var builder = WebApplication.CreateBuilder(args);
    // The app's own settings file joins the configuration below the environment variables, unless the app could not start with it (see SettingsFile).
    var settingsFile = SettingsFile.Prepare(builder);
    builder.Services.AddSingleton(settingsFile);
    builder.Services.AddSingleton(settingsFile.Status);
    builder.Services.AddSingleton<ISettingsFileStore>(new SettingsFileStore(settingsFile.Status.Path));
    builder.Services.AddSingleton<ISystemConfiguration, SystemConfiguration>();
    builder.Services.AddSingleton<IServerRestart>(sp => new ServerRestart(restarts, sp.GetRequiredService<IHostApplicationLifetime>(), sp.GetRequiredService<ILogger<ServerRestart>>())); // plain Add: a test host replaces it
    builder.Services.AddHostedService<SettingsFileReporter>();
    builder.Services.AddApplication(builder.Configuration);
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddAuthModes();
    builder.Services.AddScoped<SyncChangeServices>();
    builder.Services.AddGraphQLServer()
        .AddQueryType<Query>()
        .AddMutationType<Mutation>()
        .AddTypeExtension<SessionQueries>()
        .AddTypeExtension<AuthMutations>()
        .AddTypeExtension<DeviceSessionQueries>()
        .AddTypeExtension<DeviceSessionMutations>()
        .AddType<VehicleType>()
        .AddType<RefuelingType>()
        .AddTypeExtension<VehicleExtensions>()
        .AddTypeExtension<RefuelingExtensions>()
        .AddTypeExtension<RefuelingQueries>()
        .AddTypeExtension<RefuelingMutations>()
        .AddType<ExpenseType>()
        .AddTypeExtension<ExpenseExtensions>()
        .AddTypeExtension<VehicleExpenseExtensions>()
        .AddDataLoader<ExpensePhotosLoader>()
        .AddDataLoader<LogLevelByVehicleLoader>()
        .AddDataLoader<UserRefLoader>()
        .AddDataLoader<VehicleIncludingDeletedLoader>()
        .AddDataLoader<PhotoReadingsLoader>()
        .AddTypeExtension<LogPhotoReadingExtensions>()
        .AddDataLoader<RefuelingPhotosLoader>()
        .AddTypeExtension<ExpensePhotoExtensions>()
        .AddTypeExtension<RefuelingPhotoExtensions>()
        .AddTypeExtension<ExpenseQueries>()
        .AddTypeExtension<ExpenseMutations>()
        .AddDataLoader<RecurringByVehicleLoader>()
        .AddDataLoader<ExpenseSchedulesLoader>()
        .AddTypeExtension<ExpenseRecurringExtensions>()
        .AddTypeExtension<VehicleRecurringExtensions>()
        .AddTypeExtension<RecurringMutations>()
        .AddTypeExtension<RecurringExpenseInfoExtensions>()
        .AddTypeExtension<NotificationQueries>()
        .AddTypeExtension<RecognitionQueries>()
        .AddTypeExtension<NotificationMutations>()
        .AddTypeExtension<ImportQueries>()
        .AddTypeExtension<ImportMutations>()
        .AddType<VehicleChartType>()
        .AddTypeExtension<VehicleChartExtensions>()
        .AddTypeExtension<VehicleSummaryExtensions>()
        .AddTypeExtension<DashboardQueries>()
        .AddTypeExtension<DashboardMutations>()
        .AddTypeExtension<UiSettingsQueries>()
        .AddTypeExtension<UiSettingsInfoExtensions>()
        .AddTypeExtension<UiSettingsMutations>()
        .AddTypeExtension<OfflineQueries>()
        .AddTypeExtension<OfflineMutations>()
        .AddTypeExtension<SyncMutations>()
        .AddTypeExtension<SyncQueries>()
        .AddTypeExtension<SyncChangeInfoExtensions>()
        .AddTypeExtension<SyncCurrentExtensions>()
        .AddTypeExtension<VehicleSyncExtensions>()
        .AddTypeExtension<SystemSettingsQueries>()
        .AddTypeExtension<SystemSettingsMutations>()
        .AddDataLoader<ParkedChangeCountLoader>()
        .AddDataLoader<SyncCurrentLoader>()
        .AddTypeExtension<VehicleOfflineExtensions>()
        .AddDataLoader<LogCountSinceLoader>()
        .AddErrorFilter<BusinessErrorFilter>()
        // HotChocolate does not log what goes wrong in a request. The listener is built from the schema's own services, so what it needs from
        // the application is handed over.
        .AddApplicationService<ILoggerFactory>()
        .AddApplicationService<IHttpContextAccessor>()
        .AddDiagnosticEventListener<GraphQLLoggingListener>();

    var app = builder.Build();

    app.UseAuthentication();
    app.UseUserLogScope();
    app.MapAuthEndpoints();
    app.MapMediaEndpoints();
    app.MapImportEndpoints();
    app.MapGraphQL(); // POST /graphql (Banana Cake Pop UI on GET in Development)

    // Self-hosting: the built SPA (dist/) is copied into wwwroot and served by Kestrel. Hashed assets may be kept for good, everything with
    // a fixed name (index.html, the service worker, the manifest) is revalidated, so a release reaches every browser on its next start.
    var staticFiles = new StaticFileOptions
    {
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = StaticCaching.HeaderFor(ctx.Context.Request.Path),
    };
    app.UseDefaultFiles();
    app.UseStaticFiles(staticFiles);
    app.MapFallbackToFile("index.html", staticFiles);

    // `dotnet run -- schema export --output schema.graphql` writes the GraphQL schema for client code generation
    // (see `mise run schema:export`) without starting the server.
    if (args is ["schema", "export", ..])
    {
        var output = args is [_, _, "--output", var path, ..] ? path : "schema.graphql";
        var executor = await app.Services.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync();
        await File.WriteAllTextAsync(output, executor.Schema.ToString() + Environment.NewLine);
        return;
    }

    // `dotnet Tankstat.Api.dll backup ...` and `decrypt ...` write and open backups the same way (see BackupCommands and docs/backup-and-restore.md).
    if (BackupCommands.Handles(args))
    {
        Environment.ExitCode = await BackupCommands.RunAsync(args, app.Services, Console.In, Console.Out, Console.Error);
        return;
    }

    app.Run();
} while (restarts.Requested);

// Exposed for WebApplicationFactory<Program> in the integration tests.
public partial class Program;
