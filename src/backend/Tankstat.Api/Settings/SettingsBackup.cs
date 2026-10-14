using System.Text.Json;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.SystemSettings;

namespace Tankstat.Api.Settings;

/// <summary>
/// The app's own settings file as backups carry it (<see cref="SettingsInBackups"/>): the file on disk for a backup, unless the start left it
/// out and no save took it in since; for a restore, the file it would leave judged as the settings page judges a save, under this server's environment (<see
/// cref="ISystemConfiguration.With"/>, <see cref="ISystemConfiguration.Check"/>), with the sign-in it would run.
/// </summary>
internal sealed class SettingsBackup(ISystemConfiguration configuration, ISettingsFileStore file, ILogger<SettingsBackup> logger) : ISettingsBackup
{
    public async Task<SettingsCopy> CopyAsync(bool encrypted, BackupEnvironmentMode mode, CancellationToken ct)
    {
        var environmentKeys = configuration.EnvironmentKeys();
        var environment = SettingsInBackups.Recorded(mode, environmentKeys);
        var ignored = SettingsCopy.None with { State = BackupSettingsState.Ignored, Environment = environment };
        // A file the start left out, and no save took in since, is not what the server runs with: the backup holds none of it, and says so.
        if (configuration.File.Ignored && !configuration.FileInUse) return ignored;
        IReadOnlyDictionary<string, string?> values = new Dictionary<string, string?>();
        if (file.Exists)
        {
            try
            {
                values = await file.ReadAsync(ct);
            }
            catch (Exception e) when (e is InvalidDataException or FormatException or JsonException or IOException or UnauthorizedAccessException)
            {
                // Edited by hand since the start into a file no start would read, or one the app may no longer read: the backup goes on without it.
                logger.LogWarning(e, "The settings file could not be read, so this backup leaves it out");
                return ignored;
            }
        }
        // Merged, the copy holds what the environment sets too (a server with no file but variables gets a file of them); the live file never.
        if (mode == BackupEnvironmentMode.Merge) values = SettingsInBackups.Merged(values, environmentKeys, configuration.Running, encrypted);
        if (!file.Exists && values.Count == 0) return SettingsCopy.None with { Environment = environment };
        var (carried, omitted) = SettingsInBackups.Copy(values, encrypted);
        return new SettingsCopy(BackupSettingsState.Included, carried, omitted, environment);
    }

    public async Task<RestoredSettings> RestoredAsync(IReadOnlyDictionary<string, string?> fromBackup, bool encrypted, CancellationToken ct)
    {
        IReadOnlyDictionary<string, string?> live;
        try
        {
            live = await file.ReadAsync(ct);
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or JsonException)
        {
            logger.LogWarning(e, "The settings file could not be read, so a restore keeps none of it");
            live = new Dictionary<string, string?>(); // this server's file reads as nothing: none of it is kept
        }
        var values = SettingsInBackups.Restored(fromBackup, live, encrypted);
        var candidate = configuration.With(values);
        try
        {
            var problems = configuration.Check(candidate);
            if (problems.Count > 0) return new RestoredSettings(values, problems, new AuthOptions());
            return new RestoredSettings(values, [], candidate.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions());
        }
        finally
        {
            (candidate as IDisposable)?.Dispose();
        }
    }

    public Task WriteAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct) => file.WriteAsync(values, ct);

    public async Task<IReadOnlyList<string>> MissingAsync(BackupEnvironment recorded, IReadOnlyDictionary<string, string?>? restored, CancellationToken ct)
    {
        IReadOnlyDictionary<string, string?> values;
        try
        {
            values = restored ?? await file.ReadAsync(ct); // a backup without a file leaves this server's own
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or JsonException)
        {
            values = new Dictionary<string, string?>();
        }
        return SettingsInBackups.Missing(recorded, values, key => configuration.SourceOf(key) == SettingSource.Environment);
    }
}
