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

    public IReadOnlyList<string> Check(IConfiguration configuration) => SettingsCheck.Check(configuration);
}
