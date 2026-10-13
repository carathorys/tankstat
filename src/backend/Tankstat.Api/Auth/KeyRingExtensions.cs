using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;

namespace Tankstat.Api.Auth;

internal static class KeyRingExtensions
{
    public static IServiceCollection AddPersistedKeyRing(this IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName("tankstat");
        // The folder (KeyRingOptions) is bound and validated with the other settings (AddApplication) and read lazily (like the auth mode), so the folder can come from appsettings.json, environment variables or the command line.
        services.AddOptions<KeyManagementOptions>().Configure<IOptions<KeyRingOptions>, ILoggerFactory>((o, ring, loggers) =>
            o.XmlRepository = new FileSystemXmlRepository(Directory.CreateDirectory(System.IO.Path.GetFullPath(ring.Value.KeysPath)), loggers));
        return services;
    }
}
