using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tankstat.Application.SystemSettings;

namespace Tankstat.Infrastructure.Auth;

/// <summary>
/// Records this start and signs every device out when the authentication mode changed since the last one: after the migrations and the setup
/// step (a start that fails before it records nothing), before the server listens.
/// </summary>
internal sealed class ModeChangeSignOut(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InstanceStartService>().RecordStartAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
