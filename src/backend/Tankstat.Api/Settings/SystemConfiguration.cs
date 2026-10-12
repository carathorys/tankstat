using Microsoft.Extensions.Configuration.Memory;
using Tankstat.Application.SystemSettings;

namespace Tankstat.Api.Settings;

/// <summary>
/// The running configuration (the <c>ConfigurationManager</c> the host was built with, whose sources are in order) and the settings file's
/// place in it: the file's own source when it was read at start, else where it would be. A provider above that place is the environment
/// (environment variables, the command line, whatever a test host adds on top), one below it the shipped defaults.
/// </summary>
internal sealed class SystemConfiguration(SettingsFile file, IConfiguration running) : ISystemConfiguration
{
    public SettingsFileStatus File => file.Status;

    public IConfiguration Running => running;

    private IConfigurationBuilder Builder => (IConfigurationBuilder)running;

    /// <summary>The index of the file's source, or -1 when it was not read at start (absent or left out).</summary>
    private int FileIndex => Builder.Sources.IndexOf(file.Source);

    public SettingSource SourceOf(string key)
    {
        if (running is not IConfigurationRoot root) return SettingSource.Default;
        var providers = root.Providers.ToList(); // one per source, in the sources' order
        var fileAt = FileIndex;
        var place = fileAt >= 0 ? fileAt : ConfigurationSources.IndexOfEnvironment(Builder.Sources);
        for (var i = providers.Count - 1; i >= 0; i--)
        {
            if (!providers[i].TryGet(key, out _) && !providers[i].GetChildKeys([], key).Any()) continue;
            if (i == fileAt) return SettingSource.File;
            return i >= place ? SettingSource.Environment : SettingSource.Shipped;
        }
        return SettingSource.Default;
    }

    public IConfiguration With(IReadOnlyDictionary<string, string?> fileValues)
    {
        var fileAt = FileIndex;
        var replacement = new MemoryConfigurationSource { InitialData = fileValues };
        return fileAt >= 0
            ? ConfigurationSources.With(Builder, replacement, fileAt, replacing: true)
            : ConfigurationSources.With(Builder, replacement, ConfigurationSources.IndexOfEnvironment(Builder.Sources), replacing: false);
    }

    public IReadOnlyList<string> Check(IConfiguration configuration)
    {
        if (SettingsCheck.Check(configuration).Count == 0) return [];
        var without = With(new Dictionary<string, string?>()); // the same sources with no file, which the start compares with
        try
        {
            return SettingsCheck.AgainstFile(configuration, without);
        }
        finally
        {
            (without as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// The running sources without the file's (the same environment, command line and appsettings files, read afresh), prepared by
    /// <see cref="SettingsFile.Prepare(ConfigurationManager)"/> as the next start will prepare them.
    /// </summary>
    public NextStart PrepareNextStart()
    {
        var next = new ConfigurationManager();
        IConfigurationBuilder builder = next;
        foreach (var (key, value) in Builder.Properties) builder.Properties[key] = value;
        foreach (var source in Builder.Sources)
            if (!ReferenceEquals(source, file.Source)) builder.Add(ConfigurationSources.ReadOnce(source));
        try
        {
            SettingsFile.Prepare(next);
            return new NextStart(next, []);
        }
        catch (SettingsFileRefusedException e)
        {
            next.Dispose();
            return new NextStart(null, e.Problems);
        }
    }
}
