using Microsoft.Extensions.Configuration;
using Tankstat.Domain;

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

    /// <summary>
    /// Whether the settings file is among the running sources now: read at start, or taken in by a save since (<see cref="ApplySavedFile"/>,
    /// also over a file the start left out, which <see cref="File"/> still says it did).
    /// </summary>
    bool FileInUse { get; }

    /// <summary>The configuration as it runs now: reloaded after every save (<see cref="ApplySavedFile"/>), so it holds what the settings that apply at once run with.</summary>
    IConfiguration Running { get; }

    /// <summary>
    /// The configuration as this process started, kept as it was: what a setting that waits for a restart still runs with (its readers read
    /// it once), whatever was saved since.
    /// </summary>
    IConfiguration Started { get; }

    /// <summary>
    /// Takes the settings file in afresh after a save, which checked the whole configuration first: the running configuration reloads (the
    /// file joins it if it was not read at start), so the settings that apply at once take effect. A file edited by hand is never taken in
    /// here: that waits for the start, which checks it.
    /// </summary>
    void ApplySavedFile();

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
    /// <summary>The configuration, or <c>settings.restartWouldFail</c> naming the settings at fault: a start that fails ends the process, so no restart is asked for then.</summary>
    public IConfiguration RequireStartable() =>
        Configuration ?? throw new DomainException("settings.restartWouldFail",
            $"The server would not start again with the settings file as it is ({string.Join(" ", Refusal)}), so it is not restarted: fix the file, or set Auth__Mode in the environment.",
            new { Problems = Refusal });

    public void Dispose() => (Configuration as IDisposable)?.Dispose();
}
