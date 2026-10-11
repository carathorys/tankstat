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
/// <param name="RestartPending">The value differs from the running one: it takes effect at the next restart.</param>
public sealed record SystemSetting(
    string Key, SettingKind Kind, IReadOnlyList<string> Choices, string? Value, bool IsSet, bool IsSecret, SettingSource Source, bool Editable, bool RestartPending)
{
    public static SystemSetting From(SettingView v) =>
        new(v.Definition.Key, v.Definition.Kind, v.Definition.Choices ?? [], v.Value, v.IsSet, v.Definition.IsSecret, v.Source, v.Editable, v.RestartPending);
}

public sealed record SystemSettingsGroup(SettingsGroup Name, IReadOnlyList<SystemSetting> Settings);

public sealed record SystemSettingsInfo(SettingsFileInfo File, IReadOnlyList<SystemSettingsGroup> Groups, bool RestartPending)
{
    public static SystemSettingsInfo From(SystemSettingsView v) =>
        new(SettingsFileInfo.From(v.File), v.Groups.Select(g => new SystemSettingsGroup(g.Group, g.Settings.Select(SystemSetting.From).ToList())).ToList(), v.RestartPending);
}

/// <summary>A new value for a setting, or the setting cleared. A blank value clears it too, except a secret, which a blank keeps as it is.</summary>
public sealed record SystemSettingChangeInput(string Key, string? Value, bool Clear = false);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class SystemSettingsQueries
{
    /// <summary>Administrators only: every setting the app manages in its own settings file, group by group, with its source.</summary>
    public async Task<SystemSettingsInfo> GetSystemSettings([Service] SystemSettingsService settings, CancellationToken ct) =>
        SystemSettingsInfo.From(await settings.GetAsync(ct));
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class SystemSettingsMutations
{
    /// <summary>Administrators only: writes the changes into the settings file, all or none; they take effect at the next restart.</summary>
    public async Task<SystemSettingsInfo> UpdateSystemSettings(IReadOnlyList<SystemSettingChangeInput> changes, [Service] SystemSettingsService settings, CancellationToken ct) =>
        SystemSettingsInfo.From(await settings.UpdateAsync(changes.Select(c => new SettingChange(c.Key, c.Value, c.Clear)).ToList(), ct));

    /// <summary>Administrators only: restarts the server that answered (in its process); it is back within seconds, and the settings file is read afresh.</summary>
    public async Task<bool> RestartServer([Service] SystemSettingsService settings, CancellationToken ct)
    {
        await settings.RestartAsync(ct);
        return true;
    }
}
