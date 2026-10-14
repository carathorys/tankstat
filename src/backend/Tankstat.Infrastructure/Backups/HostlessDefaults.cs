using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.SystemSettings;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// The settings file as backups see it in a host without one (a test of the backups alone, a tool): there is none, so a backup carries none
/// and a restore of one that does writes nothing. The Api registers the real one first (<see cref="ISettingsBackup"/>).
/// </summary>
internal sealed class NoSettingsBackup(IOptions<AuthOptions> auth) : ISettingsBackup
{
    public Task<SettingsCopy> CopyAsync(bool encrypted, BackupEnvironmentMode mode, CancellationToken ct) => Task.FromResult(SettingsCopy.None);

    public Task<RestoredSettings> RestoredAsync(IReadOnlyDictionary<string, string?> fromBackup, bool encrypted, CancellationToken ct) =>
        Task.FromResult(new RestoredSettings(fromBackup, [], auth.Value));

    public Task WriteAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> MissingAsync(BackupEnvironment recorded, IReadOnlyDictionary<string, string?>? restored, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>([]); // nothing to judge against: no configuration here
}

/// <summary>A host without the restart loop of Program.cs (a test of the backups alone, a tool): nothing to restart. The Api registers the real one first.</summary>
internal sealed class NoServerRestart : IServerRestart
{
    public void Restart() { }

    public void RestartNow() { }
}
