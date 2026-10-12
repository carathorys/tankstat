using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Tankstat.Application.Auth;
using Tankstat.Application.SystemSettings;
using Tankstat.Infrastructure.Settings;

namespace Tankstat.Api.Settings;

/// <summary>Helpers over the configuration sources: where the settings file sits among them, and a configuration with other values in its place.</summary>
public static class ConfigurationSources
{
    /// <summary>Right before the last environment variables source without a prefix (the one every <c>Section__Key</c> variable comes through).</summary>
    public static int IndexOfEnvironment(IList<IConfigurationSource> sources)
    {
        for (var i = sources.Count - 1; i >= 0; i--)
            if (sources[i] is EnvironmentVariablesConfigurationSource { Prefix: null or "" }) return i;
        return sources.Count;
    }

    /// <summary>
    /// A configuration built from <paramref name="original"/>'s sources with <paramref name="replacement"/> at <paramref name="at"/>, in place of the
    /// source there (<paramref name="replacing"/>) or inserted before it. The builder's properties come along: the content root's file provider,
    /// which the <c>appsettings</c> sources resolve against. It is checked and thrown away, so its JSON files are read once: a copy that
    /// reloaded on change would watch the content root for as long as the host runs.
    /// </summary>
    public static IConfigurationRoot With(IConfigurationBuilder original, IConfigurationSource replacement, int at, bool replacing)
    {
        var builder = new ConfigurationBuilder();
        foreach (var (key, value) in original.Properties) builder.Properties[key] = value;
        for (var i = 0; i < original.Sources.Count; i++)
        {
            if (i == at) builder.Add(replacement);
            if (i != at || !replacing) builder.Add(ReadOnce(original.Sources[i]));
        }
        if (at >= original.Sources.Count) builder.Add(replacement);
        return builder.Build();
    }

    /// <summary>Whether a source from <paramref name="at"/> on (the environment, the command line) holds <paramref name="key"/>: then the settings file never decides it.</summary>
    public static bool HoldsFrom(IConfigurationBuilder original, int at, string key)
    {
        var builder = new ConfigurationBuilder();
        foreach (var (name, value) in original.Properties) builder.Properties[name] = value;
        for (var i = at; i < original.Sources.Count; i++) builder.Add(ReadOnce(original.Sources[i]));
        var root = builder.Build();
        try
        {
            return root[key] is not null;
        }
        finally
        {
            (root as IDisposable)?.Dispose();
        }
    }

    /// <summary>A JSON source that reloads on change as a copy that reads its file once; any other source as it is.</summary>
    public static IConfigurationSource ReadOnce(IConfigurationSource source) => source is JsonConfigurationSource { ReloadOnChange: true, Path: { } path } json
        ? new JsonConfigurationSource { FileProvider = json.FileProvider, Path = path, Optional = json.Optional, OnLoadException = json.OnLoadException, ReloadOnChange = false }
        : source;
}

/// <summary>The settings file as this process started: its status and the source that is (or would have been) its place among the sources.</summary>
public sealed record SettingsFile(SettingsFileStatus Status, JsonConfigurationSource Source)
{
    public const string PathSetting = "Settings:Path";
    public const string DefaultPath = "settings.json";

    /// <summary>
    /// Puts the app's own settings file (<c>Settings:Path</c>, default <c>settings.json</c> in the working directory) among the sources of
    /// <paramref name="builder"/>, right below the environment variables, unless the app could not start with it: a file that cannot be read,
    /// is not valid JSON, or brings problems the sources without it do not have is left out, and the status says so (an Error line and a public
    /// notice follow once the host runs). What the environment gets wrong on its own is not the file's doing: it neither keeps a broken file
    /// in nor leaves a good one out. The one exception: an environment the host would not start with at all (<see cref="SettingsCheck.Starts"/>)
    /// fails the start whatever becomes of the file, so a readable file stays in and the host names its mistakes too, the file's possibly
    /// first. A file left out never takes the authentication with it: when the app would then run in mode None although the file asks for a
    /// sign-in mode, the start stops instead (<see cref="SettingsFileRefusedException"/>). The path comes from the sources already there (the
    /// environment, the command line, the appsettings files), never from the file itself.
    /// </summary>
    public static SettingsFile Prepare(WebApplicationBuilder builder) => Prepare(builder.Configuration);

    /// <inheritdoc cref="Prepare(WebApplicationBuilder)"/>
    public static SettingsFile Prepare(ConfigurationManager configuration)
    {
        var path = System.IO.Path.GetFullPath(configuration[PathSetting] is { } configured && !string.IsNullOrWhiteSpace(configured) ? configured : DefaultPath);
        var builder = (IConfigurationBuilder)configuration;
        var at = ConfigurationSources.IndexOfEnvironment(builder.Sources);
        var file = SettingsFileStore.SourceFor(path); // not watched: a save on the settings page reads it afresh, a hand edit waits for the next start

        if (!File.Exists(path)) return new(SettingsFileStatus.Used(path, exists: false), file);
        var problems = Problems(builder, file, at, configuration);
        if (problems.Count == 0)
        {
            builder.Sources.Insert(at, file);
            return new(SettingsFileStatus.Used(path, exists: true), file);
        }
        if (TurnsAuthenticationOff(builder, at, configuration, path)) throw new SettingsFileRefusedException(path, problems);
        return new(SettingsFileStatus.LeftOut(path, problems), file);
    }

    private const string ModeSetting = "Auth:Mode";

    /// <summary>
    /// Whether leaving the file out would turn authentication off although the file turns it on: without the file the mode is None (the
    /// shipped default: every visitor is the anonymous owner of all data), no source above the file holds the mode (so the file decides it),
    /// and the file names a sign-in mode, or cannot be read, so it may.
    /// </summary>
    private static bool TurnsAuthenticationOff(IConfigurationBuilder sources, int at, IConfiguration without, string path)
    {
        if (SignsIn(without[ModeSetting]) || ConfigurationSources.HoldsFrom(sources, at, ModeSetting)) return false;
        try
        {
            return SignsIn(SettingsFileStore.Read(path).GetValueOrDefault(ModeSetting));
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return true; // what it holds cannot be told: it may name a mode
        }
    }

    /// <summary>A mode in which the app asks who is there: anything but None, a value it cannot read too (it was meant to be a mode).</summary>
    private static bool SignsIn(string? mode) => !string.IsNullOrWhiteSpace(mode) && !mode.Trim().Equals(nameof(AuthMode.None), StringComparison.OrdinalIgnoreCase);

    /// <summary>What the configuration with <paramref name="file"/> gets wrong that the one <paramref name="without"/> it does not.</summary>
    private static IReadOnlyList<string> Problems(IConfigurationBuilder sources, JsonConfigurationSource file, int at, IConfiguration without)
    {
        IConfigurationRoot with;
        try
        {
            with = ConfigurationSources.With(sources, file, at, replacing: false);
        }
        catch (Exception e) when (e is FormatException or InvalidDataException)
        {
            return ["The file is not valid JSON."]; // the parser's text would quote the file
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ["The file cannot be read."]; // the app's user may not read it, say: opening it is not the parser's business
        }
        try
        {
            var added = SettingsCheck.Check(with).Count == 0 ? [] : SettingsCheck.Added(with, without);
            // An environment the host would not start with on its own fails the start whatever becomes of the file, so the file goes in: the
            // host then names what is wrong with it in place, the file's own mistakes too (the environment may lean on it: an OIDC mode
            // whose authority only the file holds, say).
            return added.Count > 0 && !SettingsCheck.Starts(without) ? [] : added;
        }
        finally
        {
            (with as IDisposable)?.Dispose();
        }
    }
}

/// <summary>
/// The settings file could not be used, and leaving it out would run the app without authentication although the file asks for a sign-in
/// mode: the start stops instead, as it does for a wrong environment. The message names the file and the settings at fault, never a value.
/// </summary>
public sealed class SettingsFileRefusedException(string path, IReadOnlyList<string> problems) : InvalidOperationException(
    $"The settings file {path} could not be used ({string.Join(" ", problems)}), and the app does not start without it: Auth:Mode would then be None, "
    + "which lets every visitor in as the owner of all data, while the file asks for a sign-in mode. Fix the file, or set Auth__Mode in the environment "
    + "(it wins over the file).")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
