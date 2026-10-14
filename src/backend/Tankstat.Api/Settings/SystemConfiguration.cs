using Microsoft.Extensions.Configuration.Memory;
using Tankstat.Application.SystemSettings;
using Tankstat.Infrastructure.Settings;

namespace Tankstat.Api.Settings;

/// <summary>
/// The running configuration (the <c>ConfigurationManager</c> the host was built with, whose sources are in order) and the settings file's
/// place in it: the file's own source when it was read at start, else where it would be. A provider above that place is the environment
/// (environment variables, the command line, whatever a test host adds on top), one below it the shipped defaults.
/// </summary>
internal sealed class SystemConfiguration(SettingsFile file, IConfiguration running) : ISystemConfiguration
{
    /// <summary>Held while a save changes the sources, and while they are read here, so a view or a restart never meets them half changed.</summary>
    private readonly Lock _applying = new();

    /// <summary>The settings file's source among the running ones (or where it would go): replaced by a fresh one at every save.</summary>
    private IConfigurationSource _source = file.Source;

    public SettingsFileStatus File => file.Status;

    public bool FileInUse
    {
        get
        {
            lock (_applying) return FileIndex >= 0;
        }
    }

    public IConfiguration Running => running;

    /// <summary>
    /// A copy of the running configuration taken when this (a singleton) is first asked for, which is before any save reloads it: the values
    /// the settings that wait for a restart still run with, since their readers read them once.
    /// </summary>
    public IConfiguration Started { get; } = new ConfigurationBuilder().AddInMemoryCollection(running.AsEnumerable()).Build();

    public void ApplySavedFile()
    {
        lock (_applying)
        {
            // A fresh source for the file in its place (the start's may not see it: its folder may have been made since), or, when it was not
            // read at start (there was none yet, or the start left it out), right below the environment. Changing the sources reloads the
            // configuration and says so: what reads it afresh (IOptionsSnapshot in the next request, the logging framework's filters at
            // once) sees the saved values.
            var fresh = SettingsFileStore.SourceFor(file.Status.Path);
            var at = FileIndex;
            // The file's source from here on, before the sources change: the list holds it even when the reload throws (a reader's change
            // callback runs inside it), and every later save must find it in its place.
            _source = fresh;
            if (at >= 0) Builder.Sources[at] = fresh;
            else Builder.Sources.Insert(ConfigurationSources.IndexOfEnvironment(Builder.Sources), fresh);
        }
    }

    private IConfigurationBuilder Builder => (IConfigurationBuilder)running;

    /// <summary>The index of the file's source, or -1 when it was not read at start (absent or left out).</summary>
    private int FileIndex => Builder.Sources.IndexOf(_source);

    public SettingSource SourceOf(string key)
    {
        if (running is not IConfigurationRoot root) return SettingSource.Default;
        lock (_applying)
        {
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
    }

    public IReadOnlyList<string> EnvironmentKeys()
    {
        if (running is not IConfigurationRoot root) return [];
        lock (_applying)
        {
            var providers = root.Providers.ToList(); // one per source, in the sources' order
            var fileAt = FileIndex;
            var first = fileAt >= 0 ? fileAt + 1 : ConfigurationSources.IndexOfEnvironment(Builder.Sources);
            var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = first; i < providers.Count; i++) keys.UnionWith(Leaves(providers[i], null));
            return [.. keys];
        }
    }

    /// <summary>The keys a provider holds a value for, below <paramref name="parent"/> (all of them for null).</summary>
    private static IEnumerable<string> Leaves(IConfigurationProvider provider, string? parent)
    {
        foreach (var child in provider.GetChildKeys([], parent).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = parent is null ? child : ConfigurationPath.Combine(parent, child);
            if (provider.TryGet(path, out var value) && value is not null) yield return path; // a null held (a test host's) is no variable
            foreach (var below in Leaves(provider, path)) yield return below;
        }
    }

    public IConfiguration With(IReadOnlyDictionary<string, string?> fileValues)
    {
        var replacement = new MemoryConfigurationSource { InitialData = fileValues };
        lock (_applying)
        {
            var fileAt = FileIndex;
            return fileAt >= 0
                ? ConfigurationSources.With(Builder, replacement, fileAt, replacing: true)
                : ConfigurationSources.With(Builder, replacement, ConfigurationSources.IndexOfEnvironment(Builder.Sources), replacing: false);
        }
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
        lock (_applying)
        {
            foreach (var source in Builder.Sources)
                if (!ReferenceEquals(source, _source)) builder.Add(ConfigurationSources.ReadOnce(source));
        }
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
