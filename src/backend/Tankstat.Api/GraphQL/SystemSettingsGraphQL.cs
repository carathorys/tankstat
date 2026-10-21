using Microsoft.Extensions.Options;
using Tankstat.Api.Settings;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Images;
using Tankstat.Application.SystemSettings;

namespace Tankstat.Api.GraphQL;

/// <summary>The app's own settings file as this process started.</summary>
/// <param name="Ignored">It was left out at start because the app could not have started with it; <paramref name="Problems"/> name the settings at fault.</param>
public sealed record SettingsFileInfo(string Path, bool Exists, bool Ignored, IReadOnlyList<string> Problems)
{
    public static SettingsFileInfo From(SettingsFileStatus s) => new(s.Path, s.Exists, s.Ignored, s.Problems);
}

/// <summary>One setting the page manages, with its value after the next start and where the running value comes from.</summary>
/// <param name="Value">The value the settings file and the sources below it give; a list as its items, one per line; null for a secret, which is never shown back.</param>
/// <param name="IsSet">Whether there is a value at all: the one thing said about a secret.</param>
/// <param name="Editable">False when an environment variable or a command-line argument holds the setting: it wins over the file.</param>
/// <param name="RestartPending">The value differs from the one it runs with: it takes effect at the next restart. Never for a setting that applies at once, which runs with what was saved.</param>
/// <param name="DefaultValue">What the app runs with when no source sets it (the options class's own value), as the page writes a value; null for a secret, a list, or a setting without one.</param>
/// <param name="AppliesAtOnce">A saved change of it takes effect at once (<see cref="SettingDefinition.AppliesAtOnce"/>); it can differ from its group's (the password minimum in the Sign-in group, which waits for a restart).</param>
public sealed record SystemSetting(
    string Key, SettingKind Kind, IReadOnlyList<string> Choices, string? Value, bool IsSet, bool IsSecret, SettingSource Source, bool Editable, bool RestartPending,
    string? DefaultValue, bool AppliesAtOnce)
{
    public static SystemSetting From(SettingView v) =>
        new(v.Definition.Key, v.Definition.Kind, v.Definition.Choices ?? [], v.Value, v.IsSet, v.Definition.IsSecret, v.Source, v.Editable, v.RestartPending,
            SettingsCatalog.BuiltInDefault(v.Definition.Key), v.Definition.AppliesAtOnce);
}

/// <param name="AppliesAtOnce">A saved change of its settings takes effect at once; otherwise it waits for a restart (<see cref="SettingDefinition.AppliesAtOnce"/>).</param>
public sealed record SystemSettingsGroup(SettingsGroup Name, IReadOnlyList<SystemSetting> Settings, bool AppliesAtOnce);

public sealed record SystemSettingsInfo(SettingsFileInfo File, IReadOnlyList<SystemSettingsGroup> Groups, bool RestartPending)
{
    public static SystemSettingsInfo From(SystemSettingsView v) =>
        new(SettingsFileInfo.From(v.File), v.Groups.Select(g => new SystemSettingsGroup(g.Group, g.Settings.Select(SystemSetting.From).ToList(), SettingsCatalog.LiveGroups.Contains(g.Group))).ToList(), v.RestartPending);
}

/// <summary>A folder (or the settings file) the environment decides, which the settings page leaves out.</summary>
public enum ServerFolderKind
{
    Pictures,
    Photos,
    Backups,
    Keys,
    SettingsFile,
}

/// <summary>Where the server keeps something on disk, in full, and the setting that decides it (an environment variable, usually).</summary>
/// <param name="Setting">The configuration key, such as <c>Storage:Path</c>.</param>
/// <param name="Variable">The environment variable that sets it, such as <c>Storage__Path</c>.</param>
/// <param name="SameAsPictures">The photos of logs have no folder of their own (<c>Storage:PhotosPath</c> unset): they are kept with the pictures.</param>
public sealed record ServerFolder(ServerFolderKind Kind, string Path, string Setting, string Variable, bool SameAsPictures = false)
{
    public static ServerFolder Of(ServerFolderKind kind, string path, string setting, bool sameAsPictures = false) =>
        new(kind, path, setting, setting.Replace(":", "__", StringComparison.Ordinal), sameAsPictures);
}

[ExtendObjectType<SystemSettingsInfo>]
public sealed class SystemSettingsInfoExtensions
{
    /// <summary>
    /// The folders the server runs with, as it started (they are read once, at start), and the settings that decide them: the page manages
    /// none of them, since a folder is where the environment mounts a disk. The database is not among them: its connection string can hold a
    /// password.
    /// </summary>
    public IReadOnlyList<ServerFolder> GetFolders(
        [Parent] SystemSettingsInfo settings, [Service] IOptions<StorageOptions> storage, [Service] IBackupStore backups, [Service] IOptions<KeyRingOptions> keys)
    {
        var s = storage.Value;
        return
        [
            ServerFolder.Of(ServerFolderKind.Pictures, s.PicturesRoot, $"{StorageOptions.SectionName}:{nameof(StorageOptions.Path)}"),
            ServerFolder.Of(ServerFolderKind.Photos, s.PhotosRoot, $"{StorageOptions.SectionName}:{nameof(StorageOptions.PhotosPath)}", s.OneRoot),
            ServerFolder.Of(ServerFolderKind.Backups, backups.Folder, $"{BackupOptions.SectionName}:{nameof(BackupOptions.Path)}"),
            ServerFolder.Of(ServerFolderKind.Keys, keys.Value.Root, $"{KeyRingOptions.SectionName}:{nameof(KeyRingOptions.KeysPath)}"),
            ServerFolder.Of(ServerFolderKind.SettingsFile, settings.File.Path, SettingsFile.PathSetting),
        ];
    }
}

/// <summary>A new value for a setting, or the setting cleared. A blank value clears it too, except a secret, which a blank keeps as it is.</summary>
public sealed record SystemSettingChangeInput(string Key, string? Value, bool Clear = false);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class SystemSettingsQueries
{
    /// <summary>
    /// Administrators only, or the setup page's wizard with the one-time code from the server's log while the setup waits: every setting the
    /// app manages in its own settings file, group by group, with its source.
    /// </summary>
    public async Task<SystemSettingsInfo> GetSystemSettings(string? code, [Service] SystemSettingsService settings, CancellationToken ct) =>
        SystemSettingsInfo.From(await settings.GetAsync(code, ct));
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class SystemSettingsMutations
{
    /// <summary>
    /// Administrators only, or the setup page's wizard with the one-time code while the setup waits: writes the changes into the settings
    /// file, all or none; what applies at once takes effect now, the rest at the next restart.
    /// </summary>
    public async Task<SystemSettingsInfo> UpdateSystemSettings(
        IReadOnlyList<SystemSettingChangeInput> changes, string? code, [Service] SystemSettingsService settings, CancellationToken ct) =>
        SystemSettingsInfo.From(await settings.UpdateAsync(changes.Select(c => new SettingChange(c.Key, c.Value, c.Clear)).ToList(), code, ct));

    /// <summary>
    /// Administrators only, or the setup page with the one-time code while the setup waits: restarts the server that answered (in its
    /// process; the code stays the same); it is back within seconds, and the settings file is read afresh.
    /// </summary>
    public async Task<bool> RestartServer(string? code, [Service] SystemSettingsService settings, CancellationToken ct)
    {
        await settings.RestartAsync(code, ct);
        return true;
    }
}
