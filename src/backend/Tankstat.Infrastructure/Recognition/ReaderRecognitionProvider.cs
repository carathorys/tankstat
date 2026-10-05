using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Tankstat.Application.Recognition;
using Tankstat.Domain.Recognition;

namespace Tankstat.Infrastructure.Recognition;

/// <summary>
/// The optional photo reader (its own container, <c>Dockerfile.reader</c>) over its HTTP contract v1: <c>GET /v1/health</c> and
/// <c>POST /v1/read</c> with the picture as the body and the hints in the query (examples in <c>tests/backend/contracts/reader</c>).
/// </summary>
internal sealed class ReaderRecognitionProvider(IHttpClientFactory http, ReaderRecognitionOptions options) : IRecognitionProvider
{
    public const string ClientName = "recognition-reader";
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // A base address with a path (behind a proxy) keeps it: relative URIs resolve below the trailing slash.
    private readonly Uri _base = new(options.BaseUrl!.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/");

    public string Name => "reader";
    public bool IsConfigured => true;

    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HealthTimeout);
        try
        {
            using var response = await http.CreateClient(ClientName).GetAsync(new Uri(_base, "v1/health"), timeout.Token);
            if (!response.IsSuccessStatusCode) throw new RecognitionUnavailableException($"The reader answered {(int)response.StatusCode} to its health check.");
            if ((await response.Content.ReadFromJsonAsync<HealthBody>(Json, timeout.Token))?.Status != "ok") throw new RecognitionUnavailableException("The reader says it is not ready.");
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or NotSupportedException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // The reason goes up instead of into a bare "false": it is what the log says when photos start to wait.
            throw new RecognitionUnavailableException(e is OperationCanceledException ? "The reader did not answer its health check in time." : "The reader could not be reached.", e);
        }
    }

    public async Task<RecognitionResult> ReadAsync(RecognitionRequest request, CancellationToken ct)
    {
        var query = new List<string>
        {
            "kinds=" + string.Join(',', request.Kinds.Where(RecognitionNames.Kinds.ContainsKey).Select(k => RecognitionNames.Kinds[k]).Order(StringComparer.Ordinal)),
            "locale=" + Uri.EscapeDataString(request.Locale),
            "today=" + request.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        if (request.LastOdometer is { } last) query.Add("lastOdometer=" + last.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(request.Currency)) query.Add("currency=" + Uri.EscapeDataString(request.Currency));

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_base, "v1/read?" + string.Join('&', query)))
        {
            Content = new ReadOnlyMemoryContent(request.Image),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);
        message.Headers.Add("X-Api-Key", options.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        HttpResponseMessage response;
        try
        {
            response = await http.CreateClient(ClientName).SendAsync(message, timeout.Token);
        }
        catch (HttpRequestException e)
        {
            throw new RecognitionUnavailableException("The reader cannot be reached.", e);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new RecognitionUnavailableException($"The reader did not answer within {options.TimeoutSeconds} seconds.", e);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var body = await response.Content.ReadFromJsonAsync<ReadBody>(Json, timeout.Token) ?? throw new JsonException("empty answer");
                    return new RecognitionResult(body.ModelVersion ?? "", RecognitionNames.Kind(body.Kind), Values(body.Fields));
                }
                catch (JsonException e)
                {
                    throw new RecognitionUnavailableException("The reader's answer cannot be understood.", e);
                }
            }

            var code = await ErrorCodeAsync(response, timeout.Token);
            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new RecognitionUnavailableException(
                    "The reader refused the key: Recognition:Reader:ApiKey must be the key the reader was started with (Reader__ApiKey)."),
                HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType when code is "bad_image" or "too_large" or "unsupported_type" =>
                    new RecognitionRejectedException($"The reader cannot read this picture ({code})."),
                _ => new RecognitionUnavailableException($"The reader answered {(int)response.StatusCode} ({code ?? "no reason given"})."),
            };
        }
    }

    private static List<RecognizedValue> Values(IEnumerable<FieldBody>? fields) =>
        (fields ?? []).Where(f => f.Name is not null && f.Value is not null && RecognitionNames.Fields.ContainsKey(f.Name))
            .Select(f => new RecognizedValue(RecognitionNames.Fields[f.Name!], f.Value!, f.Confidence, f.Source switch
            {
                "derived" => ValueSource.Derived,
                "hint" => ValueSource.Hint,
                _ => ValueSource.Read,
            }))
            .ToList();

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return (await response.Content.ReadFromJsonAsync<ErrorBody>(Json, ct))?.Code;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or HttpRequestException)
        {
            return null; // not the reader's error body (a proxy's error page, say)
        }
    }

    private sealed record HealthBody(string? Status);
    private sealed record ReadBody(string? ModelVersion, string? Kind, List<FieldBody>? Fields);
    private sealed record FieldBody(string? Name, string? Value, double Confidence, string? Source);
    private sealed record ErrorBody(string? Code, string? Message);
}

/// <summary>No provider is set up (or its settings cannot be used): photos are not read, and nothing ever asks it to.</summary>
internal sealed class NullRecognitionProvider : IRecognitionProvider
{
    public static readonly NullRecognitionProvider Instance = new();

    public string Name => "none";
    public bool IsConfigured => false;
    public Task<bool> IsHealthyAsync(CancellationToken ct) => Task.FromResult(false);
    public Task<RecognitionResult> ReadAsync(RecognitionRequest request, CancellationToken ct) =>
        throw new RecognitionUnavailableException("Photo reading is not set up.");
}
