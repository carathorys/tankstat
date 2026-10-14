using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// The settings file as backups see it in a host without one (a test of the backups alone, a tool): there is none, so a backup carries none
/// and a restore of one that does writes nothing. The Api registers the real one first (<see cref="ISettingsBackup"/>).
/// </summary>
internal sealed class NoSettingsBackup(IOptions<AuthOptions> auth) : ISettingsBackup
{
    public Task<SettingsCopy> CopyAsync(bool encrypted, CancellationToken ct) => Task.FromResult(SettingsCopy.None);

    public Task<RestoredSettings> RestoredAsync(IReadOnlyDictionary<string, string?> fromBackup, bool encrypted, CancellationToken ct) =>
        Task.FromResult(new RestoredSettings(fromBackup, [], auth.Value));

    public Task WriteAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct) => Task.CompletedTask;
}

