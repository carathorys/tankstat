using Tankstat.Application.Auth;
using Tankstat.Application.SystemSettings;

namespace Tankstat.Application.Backups;

/// <summary>
/// Which keys of the instance's settings file a backup carries, and what a restore makes of them. A backup carries the instance's settings,
/// never the machine's: the settings the Server settings page manages (<see cref="SettingsCatalog.All"/>, a list with its items) and the
/// <c>Logging</c> section; never what describes the machine it ran on (the database, the storage and key folders, the settings file's own
/// path, the backups folder, the bootstrap administrator, the prompt file: none of them is in the catalog), which restored onto another
/// machine would point it at a database or folders it lacks. A plain backup carries no secret (<see cref="SettingDefinition.IsSecret"/>),
/// like the Data Protection keys, which only an encrypted one holds.
/// </summary>
public static class SettingsInBackups
{
    private const string LoggingSection = "Logging";

    /// <summary>Whether a backup may carry <paramref name="key"/> at all (a secret only in an encrypted one, <see cref="Carries"/>).</summary>
    public static bool IsInstanceSetting(string key) =>
        Under(key, LoggingSection) || SettingsCatalog.All.Any(d => d.Key.Equals(key, StringComparison.OrdinalIgnoreCase) || d.IsList && Under(key, d.Key));

    /// <summary>Whether a backup, encrypted or not, carries <paramref name="key"/>.</summary>
    public static bool Carries(string key, bool encrypted) => IsInstanceSetting(key) && (encrypted || !IsSecret(key));

    private static bool IsSecret(string key) => SettingsCatalog.Find(key)?.IsSecret == true;

    /// <summary>
    /// The copy of the file a backup takes: the keys it carries, and the names of those it leaves out (a list once, by its key), sorted.
    /// Never a value of what is left out.
    /// </summary>
    public static (IReadOnlyDictionary<string, string?> Carried, IReadOnlyList<string> Omitted) Copy(IReadOnlyDictionary<string, string?> file, bool encrypted)
    {
        var carried = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var omitted = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in file)
        {
            if (Carries(key, encrypted)) carried[key] = value;
            else omitted.Add(ListKeyOf(key));
        }
        return (carried, [.. omitted]);
    }

    /// <summary>
    /// The settings file a restore leaves: the backup's, as it carried it, and of this server's own file what no backup of that kind would
    /// carry (the machine's own keys, and in a plain backup the secrets, which it never holds), so a restore keeps this machine pointed at
    /// its own database and folders and keeps the passwords a plain backup could not bring.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Restored(
        IReadOnlyDictionary<string, string?> fromBackup, IReadOnlyDictionary<string, string?> live, bool encrypted)
    {
        var restored = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in live)
            if (!Carries(key, encrypted)) restored[key] = value;
        foreach (var (key, value) in fromBackup)
            if (Carries(key, encrypted)) restored[key] = value; // a key the backup should not have carried (a file edited by hand) is not taken
        return restored;
    }

    /// <summary>A list item's list (<c>Auth:AdminEmails:0</c> is <c>Auth:AdminEmails</c>); any other key as it is.</summary>
    private static string ListKeyOf(string key) =>
        SettingsCatalog.All.FirstOrDefault(d => d.IsList && Under(key, d.Key))?.Key ?? key;

    private static bool Under(string key, string section) =>
        key.Length > section.Length && key[section.Length] == ':' && key.StartsWith(section, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A copy of the settings file for a backup: whether there is one (<see cref="State"/>), the keys it carries, and the names it leaves out.</summary>
public sealed record SettingsCopy(BackupSettingsState State, IReadOnlyDictionary<string, string?> Values, IReadOnlyList<string> Omitted)
{
    public static readonly SettingsCopy None = new(BackupSettingsState.None, new Dictionary<string, string?>(), []);
}

/// <summary>
/// The settings file a restore would leave (<see cref="SettingsInBackups.Restored"/>), and what this server's next start would make of it
/// under its own environment: the problems it would refuse or leave the file out for (names only), and the sign-in it would run.
/// </summary>
public sealed record RestoredSettings(IReadOnlyDictionary<string, string?> Values, IReadOnlyList<string> Problems, AuthOptions Auth);

/// <summary>
/// The instance's settings file as backups carry it (the app's own <c>Settings:Path</c>): the copy a backup takes, what a restore of one
/// would leave and whether this server would start with it, and writing it back. The Api implements it over the running configuration and
/// the file; a host without them (a test of the backups alone) has no file.
/// </summary>
public interface ISettingsBackup
{
    /// <summary>The copy a backup takes now: the file on disk (what the next start reads), unless the start left it out.</summary>
    Task<SettingsCopy> CopyAsync(bool encrypted, CancellationToken ct);

    /// <summary>What a restore of <paramref name="fromBackup"/> (a backup's carried keys) would leave, judged under this server's environment.</summary>
    Task<RestoredSettings> RestoredAsync(IReadOnlyDictionary<string, string?> fromBackup, bool encrypted, CancellationToken ct);

    /// <summary>Writes the restored file (the previous one kept as <c>.bak</c>).</summary>
    Task WriteAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct);
}
