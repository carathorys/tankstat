using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tankstat.Application;
using Tankstat.Application.Recognition;
using Tankstat.Infrastructure.Recognition;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>The Recognition settings as the app binds them: photo reading is optional, so no setting may ever stop the app.</summary>
public class RecognitionSetupTests
{
    private static (ServiceProvider Services, CapturedLog Log) Build(Dictionary<string, string?> settings)
    {
        var all = new Dictionary<string, string?> { ["Database:Provider"] = "Sqlite", ["Database:ConnectionString"] = "Data Source=unused.db" };
        foreach (var (key, value) in settings) all[key] = value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(all).Build();
        var log = new CapturedLog();
        var services = new ServiceCollection().AddLogging(b => b.AddProvider(log)).AddApplication(config).AddInfrastructure(config).BuildServiceProvider();
        return (services, log);
    }

    [Fact]
    public void WithoutSettings_PhotosAreNotRead()
    {
        var (services, _) = Build([]);
        using var _s = services;

        var provider = services.GetRequiredService<IRecognitionProvider>();

        Assert.IsType<NullRecognitionProvider>(provider);
        Assert.False(provider.IsConfigured);
    }

    [Fact]
    public void WithTheReadersSettings_TheReaderReadsThem()
    {
        var (services, _) = Build(new()
        {
            ["Recognition:Provider"] = "Reader",
            ["Recognition:Reader:BaseUrl"] = "http://reader:8081",
            ["Recognition:Reader:ApiKey"] = "secret",
            ["Recognition:MinConfidence"] = "0.7",
        });
        using var _s = services;

        Assert.IsType<ReaderRecognitionProvider>(services.GetRequiredService<IRecognitionProvider>());
        Assert.Equal(0.7, services.GetRequiredService<RecognitionSetup>().Options.MinConfidence);
    }

    [Fact]
    public void TheReadersClient_LeavesTheTimeoutToTheSettings()
    {
        var (services, _) = Build([]);
        using var _s = services;

        // Reader:TimeoutSeconds goes up to 600; the client's own default (100 s) must not cut it short.
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(ReaderRecognitionProvider.ClientName);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Theory]
    [InlineData("Recognition:Reader:ApiKey", "", "ApiKey")] // a missing key
    [InlineData("Recognition:MaxConcurrent", "many", "cannot be read")] // not even a number: binding fails
    public async Task SettingsThatCannotBeUsed_TurnReadingOff_WithAWarning_InsteadOfStoppingTheApp(string key, string value, string said)
    {
        var (services, log) = Build(new()
        {
            ["Recognition:Provider"] = "Reader",
            ["Recognition:Reader:BaseUrl"] = "http://reader:8081",
            ["Recognition:Reader:ApiKey"] = "secret",
            [key] = value,
        });
        await using var _s = services;
        var worker = services.GetServices<IHostedService>().OfType<RecognitionWorker>().Single();

        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10)); // .NET runs it in the background: it must return at once on its own

        Assert.IsType<NullRecognitionProvider>(services.GetRequiredService<IRecognitionProvider>());
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
        var warning = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(said, warning.Message);
    }

    [Fact]
    public async Task WithoutSettings_TheWorkerStopsQuietly()
    {
        var (services, log) = Build([]);
        await using var _s = services;
        var worker = services.GetServices<IHostedService>().OfType<RecognitionWorker>().Single();

        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warning);
    }
}
