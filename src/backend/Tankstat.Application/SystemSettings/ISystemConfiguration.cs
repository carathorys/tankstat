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
/// whole configuration would be with other file values, and what the next start would hold against them.
/// </summary>
public interface ISystemConfiguration
{
    SettingsFileStatus File { get; }

    /// <summary>The configuration as this process started (nothing reloads).</summary>
    IConfiguration Running { get; }

    /// <summary>Where the running value of <paramref name="key"/> comes from (a list: its items, the highest source of any of them).</summary>
    SettingSource SourceOf(string key);

    /// <summary>
    /// The whole configuration with <paramref name="fileValues"/> in the settings file's place: the other sources as they are. Dispose it
    /// when done (it is disposable): one of the sources it reads chains a configuration that lives as long as the host, and would keep it.
    /// </summary>
    IConfiguration With(IReadOnlyDictionary<string, string?> fileValues);

    /// <summary>
    /// What the next start would hold against the settings file whose values gave <paramref name="configuration"/> (<see cref="With"/>):
    /// what they add to the problems of the sources without a file (the start would leave the file out), or what the host itself would
    /// refuse with them; never a mistake of the environment's own that the app starts with anyway. Empty when the next start would use the
    /// file and start; names settings, never values.
    /// </summary>
    IReadOnlyList<string> Check(IConfiguration configuration);

    /// <summary>
    /// What the next start would make of the sources as they are now, the settings file read afresh as Program.cs reads it at a restart: the
    /// configuration it would run with, or why it would not start for the file's sake. Dispose the answer when done.
    /// </summary>
    NextStart PrepareNextStart();
}

/// <summary>The configuration the next start would run with, or (<see cref="Configuration"/> null) the settings at fault that would stop it.</summary>
public sealed record NextStart(IConfiguration? Configuration, IReadOnlyList<string> Refusal) : IDisposable
{
    public void Dispose() => (Configuration as IDisposable)?.Dispose();
}
