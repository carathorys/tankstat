using Tankstat.Api.GraphQL;
using Tankstat.Application;
using Tankstat.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddGraphQLServer().AddQueryType<Query>();

var app = builder.Build();

app.MapGraphQL(); // POST /graphql (Banana Cake Pop UI on GET in Development)

// Self-hosting: the built SPA (dist/) is copied into wwwroot and served by Kestrel.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

// Exposed for WebApplicationFactory<Program> in the integration tests.
public partial class Program;
