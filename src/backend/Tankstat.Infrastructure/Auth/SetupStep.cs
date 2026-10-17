using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tankstat.Application.Setup;

namespace Tankstat.Infrastructure.Auth;

/// <summary>
/// After the restore and the migrations: decides the setup, reopens it for a Standalone instance without an administrator, and fails the
/// start when such an instance has the setup turned off (<see cref="SetupService.StartAsync"/>).
/// </summary>
internal sealed class SetupStep(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SetupService>().StartAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
