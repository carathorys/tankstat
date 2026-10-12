using System.Text.Json;
using Tankstat.Application.SystemSettings;

namespace Tankstat.Infrastructure.Settings;

/// <summary>
/// Fetches <c>{authority}/.well-known/openid-configuration</c> from the address the OIDC handler would (an <c>http://</c> authority is
/// allowed, as the handler allows it for local development), within the client's timeout for the whole exchange, without following a
/// redirect (stricter than the handler, which would follow one), reading a small answer at most, and answers with the document's issuer, or
/// why there was no document. Nothing of the answer but the issuer is kept.
/// </summary>
public sealed class OidcDiscovery(HttpClient http) : IOidcDiscovery
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    public const int MaxBytes = 64 * 1024;

    /// <summary>What the ASP.NET Core OIDC handler's backchannel sends as its User-Agent when it reads the same document.</summary>
    public const string UserAgent = "Microsoft ASP.NET Core OpenIdConnect handler";

    public async Task<OidcDiscoveryResult> ProbeAsync(string authority, CancellationToken ct)
    {
        if (!Uri.TryCreate(authority.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return OidcDiscoveryResult.Failed("not_a_url");
        var address = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
        // The client's timeout ends once the headers are in (ResponseHeadersRead): this one bounds the body too, so a server that stalls
        // after its headers is a timeout, not a wait for the request's own limit.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(http.Timeout);
        try
        {
            using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            if ((int)response.StatusCode is >= 300 and < 400) return OidcDiscoveryResult.Failed("redirected");
            if (!response.IsSuccessStatusCode) return OidcDiscoveryResult.Failed($"status_{(int)response.StatusCode}");
            if (response.Content.Headers.ContentLength > MaxBytes) return OidcDiscoveryResult.Failed("too_large");
            await using var stream = await response.Content.ReadAsStreamAsync(limit.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, limit.Token)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes) return OidcDiscoveryResult.Failed("too_large");
            }
            buffer.Position = 0;
            using var document = JsonDocument.Parse(buffer); // read as a stream, which skips a UTF-8 byte order mark as the handler's read does
            if (document.RootElement.ValueKind != JsonValueKind.Object) return OidcDiscoveryResult.Failed("not_json");
            return document.RootElement.TryGetProperty("issuer", out var issuer) && issuer.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(issuer.GetString())
                ? OidcDiscoveryResult.Found(issuer.GetString()!)
                : OidcDiscoveryResult.Failed("no_issuer");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return OidcDiscoveryResult.Failed("timeout");
        }
        catch (Exception e) when (e is HttpRequestException or IOException) // IOException: the connection broke while the body came (HttpIOException)
        {
            return OidcDiscoveryResult.Failed("unreachable");
        }
        catch (JsonException)
        {
            return OidcDiscoveryResult.Failed("not_json");
        }
    }
}
