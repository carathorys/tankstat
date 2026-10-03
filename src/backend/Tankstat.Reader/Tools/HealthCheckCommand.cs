namespace Tankstat.Reader.Tools;

/// <summary>
/// <c>dotnet Tankstat.Reader.dll --healthcheck [url]</c>: exits 0 when the running reader's health endpoint answers OK. The container's
/// HEALTHCHECK uses it because the runtime image has no curl or wget.
/// </summary>
internal static class HealthCheckCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var url = args.FirstOrDefault() ?? DefaultUrl(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"), Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            return (await http.GetAsync(url)).IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }

    /// <summary>The health URL on this machine, from the first address the server listens on (http://+:8081 → http://localhost:8081/v1/health).</summary>
    public static string DefaultUrl(string? urls, string? httpPorts)
    {
        var first = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? $"http://localhost:{httpPorts?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "8080"}";
        var uri = new UriBuilder(first.Replace("://+", "://localhost").Replace("://*", "://localhost").Replace("://0.0.0.0", "://localhost").Replace("://[::]", "://localhost"))
        {
            Path = "/v1/health",
        };
        return uri.Uri.ToString();
    }
}
