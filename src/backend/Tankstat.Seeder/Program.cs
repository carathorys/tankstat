using Microsoft.Extensions.Configuration;
using Tankstat.Seeder;

// Standalone tool: recreates the database and fills it with test data. Does nothing else. See --help.
var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
return await SeederApp.RunAsync(args, config, Console.Out, Console.In, TimeProvider.System);
