using Tankstat.Api.Auth;
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
    .AddTypeExtension<VehicleExtensions>()
    .AddErrorFilter<BusinessErrorFilter>();

var app = builder.Build();

app.UseAuthentication();
app.MapAuthEndpoints();
app.MapGraphQL(); // POST /graphql (Banana Cake Pop UI on GET in Development)

// Self-hosting: the built SPA (dist/) is copied into wwwroot and served by Kestrel.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

// Exposed for WebApplicationFactory<Program> in the integration tests.
public partial class Program;
