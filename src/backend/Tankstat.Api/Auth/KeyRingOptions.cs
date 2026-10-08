using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;

namespace Tankstat.Api.Auth;

/// <summary>
/// Bound from the "DataProtection" section: where the keys that protect the cookies (and the OIDC sign-in state) are kept. With them in a
/// folder that outlives the process (the Docker image puts it next to the database, <c>/data/keys</c>), a restart or a new container does
/// not sign everybody out.
/// </summary>
public sealed class KeyRingOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>Relative to the working directory, like <c>Storage:Path</c>.</summary>
    public string KeysPath { get; set; } = "keys";
}

internal static class KeyRingExtensions
{
    public static IServiceCollection AddPersistedKeyRing(this IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName("tankstat");
        services.AddOptions<KeyRingOptions>().BindConfiguration(KeyRingOptions.SectionName)
            .Validate(o => !string.IsNullOrWhiteSpace(o.KeysPath), "DataProtection:KeysPath must not be empty.")
            .ValidateOnStart();
        // Read lazily (like the auth mode), so the folder can come from appsettings.json, environment variables or the command line.
        services.AddOptions<KeyManagementOptions>().Configure<IOptions<KeyRingOptions>, ILoggerFactory>((o, ring, loggers) =>
            o.XmlRepository = new FileSystemXmlRepository(Directory.CreateDirectory(System.IO.Path.GetFullPath(ring.Value.KeysPath)), loggers));
        return services;
    }
}
