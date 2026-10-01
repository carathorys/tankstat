using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tankstat.Application.Users;

namespace Tankstat.Infrastructure.Auth;

/// <summary>Creates the first administrator in Standalone mode (after migrations ran); fails the start if none can be created.</summary>
internal sealed class StandaloneBootstrapper(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthService>().BootstrapAdminAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
