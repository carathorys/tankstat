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

    /// <summary>The file a save reloads from (the app reads it afresh): the world's settings file.</summary>
    public InMemorySettingsFile? Store { get; set; }

    /// <summary>The file's values the last save took in, or null before any: then the running configuration is the one it started with.</summary>
    public Dictionary<string, string?>? FileApplied { get; private set; }

    public bool FileInUse => FileApplied is not null || File is { Exists: true, Ignored: false };

    public int Reloads { get; private set; }

    /// <summary>What taking the saved file in throws, if anything (a reader of the configuration refusing it): the file is then not taken in.</summary>
    public Exception? ApplyFailure { get; set; }

    public IConfiguration Running => Build(FileApplied ?? FileAtStart);

    public IConfiguration Started => Build(FileAtStart);

    public void ApplySavedFile()
    {
        Reloads++;
        if (ApplyFailure is { } failure) throw failure;
        FileApplied = new Dictionary<string, string?>(Store?.Values ?? FileAtStart, StringComparer.OrdinalIgnoreCase);
    }

    public SettingSource SourceOf(string key) =>
        Has(Environment, key) ? SettingSource.Environment : Has(FileAtStart, key) ? SettingSource.File : Has(Shipped, key) ? SettingSource.Shipped : SettingSource.Default;

    public IConfiguration With(IReadOnlyDictionary<string, string?> fileValues) => Build(fileValues);

    /// <summary>
    /// As the app checks a save: what the file's values add to the sources without a file (the managed settings' kinds, then the validators,
    /// each compared on its own), else what the validators refuse anyway: the environment's own mistakes count only when the host would not
    /// start with them.
    /// </summary>
    public IReadOnlyList<string> Check(IConfiguration configuration)
    {
        var without = Build(new Dictionary<string, string?>());
        var shapes = Shapes(configuration).Except(Shapes(without)).ToList();
        if (shapes.Count > 0) return shapes;
        var rules = Rules(configuration);
        var added = rules.Except(Rules(without)).ToList();
        return added.Count > 0 ? added : rules;
    }

    private static List<string> Shapes(IConfiguration configuration) => [.. SettingsCatalog.Problems(key => configuration[key])];

    /// <summary>The validators of the Application layer, bound from the configuration as the app binds them.</summary>
    private static List<string> Rules(IConfiguration configuration)
    {
        var problems = new List<string>();
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

    /// <summary>The settings file the next start would read (by default the one it read at start), or, when set, its refusal of it.</summary>
    public Dictionary<string, string?>? FileAtNextStart { get; set; }
    public IReadOnlyList<string> NextStartRefusal { get; set; } = [];

    public NextStart PrepareNextStart() =>
        NextStartRefusal.Count > 0 ? new NextStart(null, NextStartRefusal) : new NextStart(Build(FileAtNextStart ?? FileAtStart), []);

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

    /// <summary>When set, a probe answers only once it completes: an authority that takes its time.</summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>Completes when the first probe was asked.</summary>
    public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<OidcDiscoveryResult> ProbeAsync(string authority, CancellationToken ct)
    {
        Probed.Add(authority);
        Asked.TrySetResult();
        if (Hold is { } hold) await hold.Task.WaitAsync(ct);
        return Answer;
    }
}

internal sealed class FakeServerRestart : IServerRestart
{
    public int Count { get; private set; }
    public int NowCount { get; private set; }
    public void Restart() => Count++;
    public void RestartNow() => NowCount++;
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
