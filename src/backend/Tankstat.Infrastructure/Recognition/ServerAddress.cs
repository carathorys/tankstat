namespace Tankstat.Infrastructure.Recognition;

/// <summary>A model server's address as the log may show it: scheme, host, port and path, without user info or query (either can carry a key).</summary>
internal static class ServerAddress
{
    public static string Of(string? baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}".TrimEnd('/') : "(no usable address)";
}
