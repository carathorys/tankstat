using Microsoft.Extensions.Configuration;

namespace Tankstat.Application.SystemSettings;

/// <summary>Where the value of a setting that runs now comes from.</summary>
public enum SettingSource
{
    /// <summary>An environment variable or the command line: above the settings file, so the page cannot change it.</summary>
    Environment,
    /// <summary>The app's own settings file.</summary>
    File,
    /// <summary>The shipped <c>appsettings</c> files: a default the file may override.</summary>
    Shipped,
    /// <summary>Nowhere: the options class's own default.</summary>
    Default,
}

/// <summary>
/// The configuration this process runs with and the settings file's place in it: what runs now, where each value comes from, what the
/// whole configuration would be with other file values, and whether the app could start with that.
/// </summary>
public interface ISystemConfiguration
{
    SettingsFileStatus File { get; }

    /// <summary>The configuration as this process started (nothing reloads).</summary>
    IConfiguration Running { get; }

    /// <summary>Where the running value of <paramref name="key"/> comes from (a list: its items, the highest source of any of them).</summary>
    SettingSource SourceOf(string key);

    /// <summary>The whole configuration with <paramref name="fileValues"/> in the settings file's place: the other sources as they are.</summary>
    IConfiguration With(IReadOnlyDictionary<string, string?> fileValues);

    /// <summary>Why the app could not start with <paramref name="configuration"/> (empty when it could); names settings, never values.</summary>
    IReadOnlyList<string> Check(IConfiguration configuration);
}
