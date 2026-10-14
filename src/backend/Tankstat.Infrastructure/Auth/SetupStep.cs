using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tankstat.Application.Setup;

namespace Tankstat.Infrastructure.Auth;

/// <summary>
/// After the restore and the migrations: decides the setup, creates the bootstrap administrator in Standalone, and fails the start when a
/// set-up Standalone instance has no administrator and nothing to create one from (<see cref="SetupService.StartAsync"/>).
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
