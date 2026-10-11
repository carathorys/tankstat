using System.Text.Json;
using Tankstat.Application.SystemSettings;

namespace Tankstat.Infrastructure.Settings;

/// <summary>
/// Fetches <c>{authority}/.well-known/openid-configuration</c> the way the OIDC handler would (an <c>http://</c> authority is allowed, as the
/// handler allows it for local development), within a short wait, without following a redirect, reading a small answer at most, and
/// answers with the document's issuer, or why there was no document. Nothing of the answer but the issuer is kept.
/// </summary>
public sealed class OidcDiscovery(HttpClient http) : IOidcDiscovery
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    public const int MaxBytes = 64 * 1024;

    public async Task<OidcDiscoveryResult> ProbeAsync(string authority, CancellationToken ct)
    {
        if (!Uri.TryCreate(authority.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return OidcDiscoveryResult.Failed("not_a_url");
        var address = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
        try
        {
            using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400) return OidcDiscoveryResult.Failed("redirected");
            if (!response.IsSuccessStatusCode) return OidcDiscoveryResult.Failed($"status_{(int)response.StatusCode}");
            if (response.Content.Headers.ContentLength > MaxBytes) return OidcDiscoveryResult.Failed("too_large");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes) return OidcDiscoveryResult.Failed("too_large");
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object) return OidcDiscoveryResult.Failed("not_json");
            return document.RootElement.TryGetProperty("issuer", out var issuer) && issuer.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(issuer.GetString())
                ? OidcDiscoveryResult.Found(issuer.GetString()!)
                : OidcDiscoveryResult.Failed("no_issuer");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return OidcDiscoveryResult.Failed("timeout");
        }
        catch (HttpRequestException)
        {
            return OidcDiscoveryResult.Failed("unreachable");
        }
        catch (JsonException)
        {
            return OidcDiscoveryResult.Failed("not_json");
        }
    }
}
