using Tankstat.Reader;
using Tankstat.Reader.Http;
using Tankstat.Reader.Tools;

if (args is ["--healthcheck", ..]) return await HealthCheckCommand.RunAsync(args[1..]);
if (args.Length > 0 && ToolCommands.Handles(args[0])) return await ToolCommands.RunAsync(args, Console.Out, Console.Error);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddReader(builder.Configuration);

var app = builder.Build();
app.MapReaderEndpoints();
await app.RunAsync();
return 0;

// Exposed for WebApplicationFactory<Program> in the integration tests.
public partial class Program;
