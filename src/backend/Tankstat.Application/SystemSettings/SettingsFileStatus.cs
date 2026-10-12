namespace Tankstat.Application.SystemSettings;

/// <summary>
/// The app's own settings file as this process started: where it is, whether it was there, and whether it was left out because the app could
/// not have started with it (<see cref="Problems"/> name the settings at fault, never their values; the public notice carries none of them).
/// </summary>
public sealed record SettingsFileStatus(string Path, bool Exists, bool Ignored, IReadOnlyList<string> Problems)
{
    public static SettingsFileStatus Used(string path, bool exists) => new(path, exists, false, []);

    public static SettingsFileStatus LeftOut(string path, IReadOnlyList<string> problems) => new(path, true, true, problems);
}
