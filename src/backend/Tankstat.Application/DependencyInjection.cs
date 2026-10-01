using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Health;

namespace Tankstat.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<HealthReporter>();
        return services;
    }
}
