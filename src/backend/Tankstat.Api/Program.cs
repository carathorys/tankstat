using HotChocolate.Execution;
using Tankstat.Api.Auth;
using Tankstat.Api.Media;
using Tankstat.Api.GraphQL;
using Tankstat.Application;
using Tankstat.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuthModes();
builder.Services.AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddTypeExtension<SessionQueries>()
    .AddTypeExtension<AuthMutations>()
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
    .AddDataLoader<RefuelingPhotosLoader>()
    .AddTypeExtension<ExpensePhotoExtensions>()
    .AddTypeExtension<RefuelingPhotoExtensions>()
    .AddTypeExtension<ExpenseQueries>()
    .AddTypeExtension<ExpenseMutations>()
    .AddTypeExtension<ImportQueries>()
    .AddTypeExtension<ImportMutations>()
    .AddType<VehicleChartType>()
    .AddTypeExtension<VehicleChartExtensions>()
    .AddTypeExtension<VehicleSummaryExtensions>()
    .AddTypeExtension<DashboardQueries>()
    .AddTypeExtension<DashboardMutations>()
    .AddErrorFilter<BusinessErrorFilter>();

var app = builder.Build();

app.UseAuthentication();
app.MapAuthEndpoints();
app.MapMediaEndpoints();
app.MapImportEndpoints();
app.MapGraphQL(); // POST /graphql (Banana Cake Pop UI on GET in Development)

// Self-hosting: the built SPA (dist/) is copied into wwwroot and served by Kestrel.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

// `dotnet run -- schema export --output schema.graphql` writes the GraphQL schema for client code generation
// (see `mise run schema:export`) without starting the server.
if (args is ["schema", "export", ..])
{
    var output = args is [_, _, "--output", var path, ..] ? path : "schema.graphql";
    var executor = await app.Services.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync();
    await File.WriteAllTextAsync(output, executor.Schema.ToString() + Environment.NewLine);
    return;
}

app.Run();

// Exposed for WebApplicationFactory<Program> in the integration tests.
public partial class Program;
