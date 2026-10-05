using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Recognition;

namespace Tankstat.Infrastructure.Recognition;

/// <summary>
/// With <c>Recognition:OpenAiCompatible:LogTraffic</c> on, writes every request to the model server and every answer to the log
/// (Information, one entry each, pretty-printed): the one place the app logs what a model is sent and says, prompts and what it read
/// included, for an operator who has to see why a model answers as it does. Even then the photo is only its size and the key is masked
/// (the header, and the key itself wherever a server echoes it). Off, every call passes through untouched. A line break in an entry comes
/// from its layout only: whatever a server wrote is escaped, so a log collector that reads one line per entry cannot be fooled.
/// </summary>
internal sealed class OpenAiTrafficHandler(OpenAiCompatibleRecognitionOptions options, ILogger<OpenAiTrafficHandler> logger) : DelegatingHandler
{
    /// <summary>The photo a chat call is for, set by the provider so the entries can say so; the health check (a GET) has none.</summary>
    public static readonly HttpRequestOptionsKey<Guid?> Photo = new("Tankstat.Photo");

    /// <summary>A body is cut at this many characters: a model that rambles must not fill the log.</summary>
    internal const int MaxBodyCharacters = 16_000;

    private const string Hidden = "***";
    private const string Base64 = ";base64,";

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly string[] SecretHeaders = ["Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "X-Api-Key", "Api-Key"];

    // One number for the whole process, so a request and its answer are told apart from the others whatever handler instance carried them.
    private static long _sequence;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!options.LogTraffic) return await base.SendAsync(request, ct);

        var seq = Interlocked.Increment(ref _sequence);
        var purpose = request.Options.TryGetValue(Photo, out var photo) && photo is { } id ? $"photo {id}" : request.Method == HttpMethod.Get ? "health check" : "read";
        if (request.Content is { } sent) await sent.LoadIntoBufferAsync(ct);
        logger.LogInformation("Model request {Seq} ({Purpose}): {Method} {Url}\n{Headers}\n{Body}",
            seq, purpose, request.Method.Method, ServerAddress.Of(request.RequestUri?.ToString()), Headers(request.Headers, request.Content?.Headers), await BodyAsync(request.Content, ct));

        var started = Stopwatch.GetTimestamp();
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, ct);
            await response.Content.LoadIntoBufferAsync(ct); // read here, and still readable for the provider afterwards
        }
        catch (Exception e)
        {
            logger.LogInformation("Model request {Seq} failed after {ElapsedMs} ms: {Error}", seq, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, Mask($"{e.GetType().Name}: {Flat(e.Message)}"));
            throw;
        }
        logger.LogInformation("Model answer {Seq} ({Purpose}): HTTP {Status} after {ElapsedMs} ms\n{Headers}\n{Body}",
            seq, purpose, (int)response.StatusCode, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, Headers(response.Headers, response.Content.Headers), await BodyAsync(response.Content, ct));
        return response;
    }

    private string Headers(params HttpHeaders?[] sets) => Mask(string.Join("\n", sets.OfType<HttpHeaders>().SelectMany(h => h).Select(h =>
        $"{h.Key}: {(SecretHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase) ? Secret(h.Key, h.Value) : Flat(string.Join(", ", h.Value)))}")));

    /// <summary>A credential is hidden; of an <c>Authorization</c> header the scheme stays (<c>Bearer ***</c>), which says what kind of key was sent.</summary>
    private static string Secret(string header, IEnumerable<string> values) =>
        header.EndsWith("Authorization", StringComparison.OrdinalIgnoreCase) && values.FirstOrDefault()?.Split(' ', 2) is [var scheme, _] ? $"{Flat(scheme)} {Hidden}" : Hidden;

    private async Task<string> BodyAsync(HttpContent? content, CancellationToken ct)
    {
        if (content is null) return "(no body)";
        var text = await content.ReadAsStringAsync(ct);
        return text.Length == 0 ? "(empty body)" : Cut(Mask(Layout(text)));
    }

    /// <summary>JSON as indented JSON, the photo only as its size; anything else (a proxy's web page) as one escaped line.</summary>
    private static string Layout(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is { } json)
            {
                HidePhotos(json);
                return Escape(json.ToJsonString(Pretty));
            }
        }
        catch (JsonException)
        {
            // not JSON: shown as it is, on one line
        }
        return Flat(text);
    }

    /// <summary><c>data:image/jpeg;base64,/9j/4AAQ…</c> becomes <c>data:image/jpeg;base64,[231 KB]</c>.</summary>
    private static void HidePhotos(JsonNode? node)
    {
        static string? Shortened(JsonNode? value)
        {
            if (value is not JsonValue v || !v.TryGetValue<string>(out var text) || !text.StartsWith("data:", StringComparison.Ordinal)) return null;
            var at = text.IndexOf(Base64, StringComparison.Ordinal);
            if (at < 0) return null;
            var start = at + Base64.Length;
            return $"{text[..start]}[{((text.Length - start) * 3L / 4 + 1023) / 1024} KB]";
        }

        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToList())
                {
                    if (Shortened(o[key]) is { } shown) o[key] = shown;
                    else HidePhotos(o[key]);
                }
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                {
                    if (Shortened(a[i]) is { } shown) a[i] = shown;
                    else HidePhotos(a[i]);
                }
                break;
        }
    }

    /// <summary>The key, wherever it shows up: a server may echo it in an error.</summary>
    private string Mask(string text) => string.IsNullOrWhiteSpace(options.ApiKey) ? text : text.Replace(options.ApiKey, Hidden, StringComparison.Ordinal);

    private static string Cut(string text)
    {
        if (text.Length <= MaxBodyCharacters) return text;
        var keep = char.IsHighSurrogate(text[MaxBodyCharacters - 1]) ? MaxBodyCharacters - 1 : MaxBodyCharacters;
        return $"{text[..keep]}\n… [{text.Length - keep} more characters]";
    }

    /// <summary>Indented JSON keeps its own line breaks (the writer escapes any inside a string); any other control character, and the Unicode line separators an encoder may leave raw, are written out.</summary>
    private static string Escape(string layout) => Rewrite(layout, keepLineBreaks: true);

    /// <summary>One line: every control character and line separator written out.</summary>
    private static string Flat(string text) => Rewrite(text, keepLineBreaks: false);

    private static string Rewrite(string text, bool keepLineBreaks)
    {
        var result = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (keepLineBreaks && c is '\n' or '\r') result.Append(c);
            else if (c == '\n') result.Append("\\n");
            else if (c == '\r') result.Append("\\r");
            else if (c == '\t') result.Append("\\t");
            else if (char.IsControl(c) || c is '\u2028' or '\u2029') result.Append($"\\u{(int)c:x4}");
            else result.Append(c);
        }
        return result.ToString();
    }
}
