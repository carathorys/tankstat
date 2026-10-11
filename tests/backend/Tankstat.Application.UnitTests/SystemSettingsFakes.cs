using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Notifications;
using Tankstat.Application.Sync;
using Tankstat.Application.SystemSettings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Instance;

namespace Tankstat.Application.UnitTests;

/// <summary>Three layers in memory, as the app has them: the shipped defaults, the settings file as it was at start, the environment on top.</summary>
internal sealed class FakeSystemConfiguration : ISystemConfiguration
{
    public Dictionary<string, string?> Shipped { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> FileAtStart { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> Environment { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SettingsFileStatus File { get; set; } = SettingsFileStatus.Used("/data/settings.json", exists: false);

    public IConfiguration Running => Build(FileAtStart);

    public SettingSource SourceOf(string key) =>
        Has(Environment, key) ? SettingSource.Environment : Has(FileAtStart, key) ? SettingSource.File : Has(Shipped, key) ? SettingSource.Shipped : SettingSource.Default;

    public IConfiguration With(IReadOnlyDictionary<string, string?> fileValues) => Build(fileValues);

    /// <summary>The managed settings' kinds and the validators of the Application layer, bound from the configuration as the app binds them.</summary>
    public IReadOnlyList<string> Check(IConfiguration configuration)
    {
        var problems = new List<string>(SettingsCatalog.Problems(key => configuration[key]));
        if (problems.Count > 0) return problems;
        try
        {
            var smtp = configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();
            Add(new AuthOptionsValidator(Options.Create(smtp)).Validate(null, configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new()));
            Add(new NotificationOptionsValidator().Validate(null, configuration.GetSection(NotificationOptions.SectionName).Get<NotificationOptions>() ?? new()));
            Add(new SyncOptionsValidator().Validate(null, configuration.GetSection(SyncOptions.SectionName).Get<SyncOptions>() ?? new()));
            Add(new VehicleDefaultsOptionsValidator().Validate(null, configuration.GetSection(VehicleDefaultsOptions.SectionName).Get<VehicleDefaultsOptions>() ?? new()));
        }
        catch (InvalidOperationException)
        {
            problems.Add("A setting cannot be read as a value of its type.");
        }
        return problems;

        void Add(ValidateOptionsResult result)
        {
            if (result.Failed) problems.AddRange(result.Failures!);
        }
    }

    private static bool Has(Dictionary<string, string?> values, string key) =>
        values.Keys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase) || k.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));

    private IConfigurationRoot Build(IReadOnlyDictionary<string, string?> file) =>
        new ConfigurationBuilder().AddInMemoryCollection(Shipped).AddInMemoryCollection(file).AddInMemoryCollection(Environment).Build();
}

internal sealed class InMemorySettingsFile : ISettingsFileStore
{
    public Dictionary<string, string?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Writes { get; private set; }
    public Exception? FailRead { get; set; }
    public Exception? FailWrite { get; set; }

    public string Path => "/data/settings.json";

    public bool Exists { get; set; }

    public Task<IReadOnlyDictionary<string, string?>> ReadAsync(CancellationToken ct) =>
        FailRead is { } e ? throw e : Task.FromResult<IReadOnlyDictionary<string, string?>>(new Dictionary<string, string?>(Values, StringComparer.OrdinalIgnoreCase));

    public Task WriteAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        if (FailWrite is { } e) throw e;
        Writes++;
        Exists = true;
        Values.Clear();
        foreach (var (key, value) in values) Values[key] = value;
        return Task.CompletedTask;
    }
}

internal sealed class FakeOidcDiscovery : IOidcDiscovery
{
    public OidcDiscoveryResult Answer { get; set; } = OidcDiscoveryResult.Found("https://id.example.com");
    public List<string> Probed { get; } = [];

    public Task<OidcDiscoveryResult> ProbeAsync(string authority, CancellationToken ct)
    {
        Probed.Add(authority);
        return Task.FromResult(Answer);
    }
}

internal sealed class FakeServerRestart : IServerRestart
{
    public int Count { get; private set; }
    public void Restart() => Count++;
}

internal sealed class InMemoryInstanceState : IInstanceStateRepository
{
    public InstanceState? Value { get; set; }
    public int Saves { get; private set; }
    public Task<InstanceState?> GetAsync(CancellationToken ct) => Task.FromResult(Value);
    public Task SaveAsync(InstanceState state, CancellationToken ct)
    {
        Saves++;
        Value = state;
        return Task.CompletedTask;
    }
}
