using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Recognition;
using Tankstat.Domain.Recognition;

namespace Tankstat.Infrastructure.Recognition;

/// <summary>
/// A vision model behind an OpenAI-compatible chat completions API: a server the operator runs (LM Studio, Ollama, llama.cpp, vLLM) or a
/// paid one. <c>POST {BaseUrl}/chat/completions</c> gets the photo as a data URL with the prompt (<see cref="OpenAiCompatiblePrompt"/>) and
/// answers with JSON that is read tolerantly (servers and models differ); <c>GET {BaseUrl}/models</c> is the health check. The values come
/// back through <see cref="ReadingChecks"/>, because a model's own confidence is not to be believed on its own. Exceptions carry fixed
/// words, a status and at most a short error code: the worker logs them, and a server's message (or the model's) could hold anything.
/// The Debug lines of each photo follow it from the question to the answer (what was kept, what was dropped and why) with ids, sizes,
/// timings, field names and reason codes: never what the model read, the prompt or the key.
/// </summary>
internal sealed partial class OpenAiCompatibleRecognitionProvider(
    IHttpClientFactory http, OpenAiCompatibleRecognitionOptions options, string? systemPrompt, ILogger<OpenAiCompatibleRecognitionProvider> logger)
    : IRecognitionProvider
{
    public const string ClientName = "recognition-openai-compatible";
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(5);

    // An error code is a snake_case token ("invalid_api_key", "invalid_image"); anything else in that place is words, and words are never repeated.
    [GeneratedRegex(@"^[a-z0-9_]{1,32}\z")]
    private static partial Regex SafeToken();

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex Thinking();

    // A base address with a path keeps it: relative URIs resolve below the trailing slash.
    private readonly Uri _base = new(options.BaseUrl!.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/");
    private readonly string _systemPrompt = systemPrompt ?? OpenAiCompatiblePrompt.DefaultSystemPrompt;
    private readonly string _server = ServerAddress.Of(options.BaseUrl);

    public string Name => "openai-compatible";
    public bool IsConfigured => true;

    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HealthTimeout);
        try
        {
            using var message = Message(HttpMethod.Get, "models");
            using var response = await http.CreateClient(ClientName).SendAsync(message, timeout.Token);
            // A key may be allowed to chat but not to list models (OpenAI's restricted keys): then the first read is the judge of it.
            if (response.StatusCode == HttpStatusCode.Forbidden) return true;
            if (!response.IsSuccessStatusCode) throw await ErrorAsync(response, timeout.Token);
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), default, timeout.Token);
            var listed = ModelIds(document.RootElement) ?? throw NotModels();
            if (listed.Count == 0) throw new RecognitionUnavailableException("The model server lists no models.");
            // A server with one model answers with it whatever is asked for (llama.cpp lists the file it was started with); others must know the name.
            if (listed.Count > 1 && !listed.Any(id => Matches(id, options.Model!)))
                throw new RecognitionUnavailableException("The model server does not list the model: Recognition:OpenAiCompatible:Model must be one of the names GET /v1/models shows.");
            return true;
        }
        catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // The reason goes up instead of into a bare "false": it is what the log says when photos start to wait.
            throw new RecognitionUnavailableException(e is OperationCanceledException ? "The model server did not answer in time when asked for its models." : "The model server could not be reached.", e);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            throw NotModels();
        }
    }

    public async Task<RecognitionResult> ReadAsync(RecognitionRequest request, CancellationToken ct)
    {
        logger.LogDebug("Asking {Model} at {Server} to read photo {PhotoId} ({ContentType}, {Kilobytes} KB)",
            options.Model, _server, request.ReadingId, request.ContentType, (request.Image.Length + 1023) / 1024);
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await ChatAsync(request, started, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Why is in the exception, which the worker logs; this line says it was this photo and how long it took.
            logger.LogDebug("{Model} did not read photo {PhotoId}: gave up after {ElapsedMs} ms", options.Model, request.ReadingId, ElapsedMs(started));
            throw;
        }
    }

    private async Task<RecognitionResult> ChatAsync(RecognitionRequest request, long started, CancellationToken ct)
    {
        using var message = Message(HttpMethod.Post, "chat/completions");
        message.Content = new StringContent(Body(request).ToJsonString(), Encoding.UTF8, "application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        HttpResponseMessage response;
        try
        {
            response = await http.CreateClient(ClientName).SendAsync(message, timeout.Token);
        }
        catch (HttpRequestException e)
        {
            throw new RecognitionUnavailableException("The model server cannot be reached.", e);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new RecognitionUnavailableException($"The model did not answer within {options.TimeoutSeconds} seconds.", e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw await ErrorAsync(response, timeout.Token);
            JsonDocument document;
            try
            {
                document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), default, timeout.Token);
            }
            catch (JsonException)
            {
                throw NotAChatCompletion();
            }
            using (document) return Result(document.RootElement, request, response.StatusCode, started);
        }
    }

    private HttpRequestMessage Message(HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, new Uri(_base, path));
        if (!string.IsNullOrWhiteSpace(options.ApiKey)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        return message;
    }

    /// <summary>
    /// The chat completion request: the system prompt, then the photo (as a data URL of its stored type) with the contract beside it. No
    /// token limit is sent (servers name it differently and the timeout bounds a runaway answer); the temperature only when set.
    /// </summary>
    private JsonObject Body(RecognitionRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["stream"] = false,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = _systemPrompt },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray(
                        new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = $"data:{request.ContentType};base64,{Convert.ToBase64String(request.Image.Span)}" } },
                        new JsonObject { ["type"] = "text", ["text"] = OpenAiCompatiblePrompt.Contract(request.Kinds, request.Locale) }),
                }),
        };
        if (options.Temperature is { } temperature) body["temperature"] = temperature;
        switch (options.ResponseFormat)
        {
            case OpenAiResponseFormat.JsonSchema:
                body["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject { ["name"] = "photo_reading", ["strict"] = true, ["schema"] = OpenAiCompatiblePrompt.Schema(request.Kinds) },
                };
                break;
            case OpenAiResponseFormat.JsonObject:
                body["response_format"] = new JsonObject { ["type"] = "json_object" };
                break;
        }
        return body;
    }

    /// <summary>What the model said, as a reading: a refusal is final, a cut-off or unusable answer is worth another try, a kind the photo may not show counts as unknown.</summary>
    private RecognitionResult Result(JsonElement root, RecognitionRequest request, HttpStatusCode status, long started)
    {
        if (root.ValueKind != JsonValueKind.Object) throw NotAChatCompletion();
        var version = StringProperty(root, "model") is { Length: > 0 } reported ? reported : options.Model ?? "";
        if (Property(root, "choices") is not { ValueKind: JsonValueKind.Array } choices || choices.GetArrayLength() == 0 || choices[0].ValueKind != JsonValueKind.Object)
            throw new RecognitionUnavailableException("The model server answered without a choice.");
        var choice = choices[0];
        var finish = StringProperty(choice, "finish_reason");
        var message = Property(choice, "message") ?? default;
        if (finish == "content_filter" || !string.IsNullOrWhiteSpace(StringProperty(message, "refusal"))) throw new RecognitionRejectedException("The model refused to read this picture.");
        if (finish == "length") throw new RecognitionUnavailableException("The model's answer was cut off: the server's limit on the length of an answer is too low for it.");
        var text = Text(message);
        if (string.IsNullOrWhiteSpace(text)) throw new RecognitionUnavailableException("The model answered nothing.");

        using var answer = JsonObjectIn(text);
        var said = Kind(StringProperty(answer.RootElement, "kind"));
        List<RecognizedValue> values = Property(answer.RootElement, "fields") is { ValueKind: JsonValueKind.Array } fields
            ? fields.EnumerateArray().Select(ValueOf).OfType<RecognizedValue>().ToList()
            : [];
        var result = Judge(version, said, values, request);
        logger.LogDebug("{Model} answered photo {PhotoId} in {ElapsedMs} ms (HTTP {Status}, finish {Finish}, {PromptTokens}+{CompletionTokens} tokens): said {Said}, listed {Listed}, kept [{Kept}], issues [{Issues}]",
            options.Model, request.ReadingId, ElapsedMs(started), (int)status, Finish(finish), Tokens(root, "prompt_tokens"), Tokens(root, "completion_tokens"),
            said, values.Count, string.Join(", ", result.Values.Select(v => $"{v.Name}@{v.Confidence.ToString("0.00", CultureInfo.InvariantCulture)}")),
            string.Join(", ", result.Issues.Select(i => i.Field is { } field ? $"{field}:{i.Code}" : i.Code.ToString())));
        return result;
    }

    /// <summary>
    /// What the model's answer comes to: a kind the photo may not show counts as unknown (its values would be for something else), the
    /// values of the others go through <see cref="ReadingChecks"/>, and a photo that gave no value at all says so.
    /// </summary>
    private static RecognitionResult Judge(string version, DocumentKind said, List<RecognizedValue> values, RecognitionRequest request)
    {
        if (said == DocumentKind.Unknown || !request.Kinds.Contains(said))
            return new RecognitionResult(version, DocumentKind.Unknown, []) { Issues = [new ReadingIssue(null, ReadingIssueCode.Unrecognised)] };
        var checks = ReadingChecks.Check(said, values, request);
        return new RecognitionResult(version, said, checks.Kept)
        {
            Issues = checks.Kept.Count == 0 && checks.Issues.Count == 0 ? [new ReadingIssue(null, ReadingIssueCode.NothingLegible)] : checks.Issues,
        };
    }

    /// <summary>Why the model stopped, as a log may say it: a token such as <c>stop</c> or <c>length</c>; whatever else a server writes there is not repeated.</summary>
    private static string Finish(string? reason) => reason is null ? "none" : SafeToken().IsMatch(reason) ? reason : "other";

    private static long ElapsedMs(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    /// <summary>A token count of the answer's <c>usage</c> as text (servers differ in whether they tell); "?" when it is not there.</summary>
    private static string Tokens(JsonElement root, string name) =>
        Property(root, "usage") is { } usage && Property(usage, name) is { ValueKind: JsonValueKind.Number } count && count.TryGetInt64(out var n)
            ? n.ToString(CultureInfo.InvariantCulture)
            : "?";

    /// <summary>The model's text: a string, or (some servers) the text parts of a list.</summary>
    private static string? Text(JsonElement message) => Property(message, "content") switch
    {
        { ValueKind: JsonValueKind.String } content => content.GetString(),
        { ValueKind: JsonValueKind.Array } parts => string.Concat(parts.EnumerateArray().Select(part => StringProperty(part, "text"))),
        _ => null,
    };

    /// <summary>The JSON object in the model's text, whatever is around it (a reasoning block, code fences, a sentence).</summary>
    private static JsonDocument JsonObjectIn(string text)
    {
        var bare = Thinking().Replace(text, "");
        var start = bare.IndexOf('{');
        var end = bare.LastIndexOf('}');
        if (start < 0 || end <= start) throw NotTheJson();
        try
        {
            return JsonDocument.Parse(bare[start..(end + 1)]);
        }
        catch (JsonException)
        {
            throw NotTheJson();
        }
    }

    /// <summary>One field of the answer, or null when it names no value the app knows; a confidence that is not a number is NaN, which <see cref="ReadingChecks"/> counts as doubtful (and says so).</summary>
    private static RecognizedValue? ValueOf(JsonElement field)
    {
        if (Field(StringProperty(field, "name")) is not { } name) return null;
        var value = Property(field, "value") switch
        {
            { ValueKind: JsonValueKind.String } text => text.GetString(),
            { ValueKind: JsonValueKind.Number } number => number.GetRawText(),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(value)) return null;
        var confidence = Property(field, "confidence") is { ValueKind: JsonValueKind.Number } rated && rated.TryGetDouble(out var d) ? d : double.NaN;
        return new RecognizedValue(name, value, confidence, ValueSource.Read);
    }

    // Without a schema a model may spell the names its own way ("Fuel receipt", "FuelReceipt", "unit_price"): the letters decide.
    private static DocumentKind Kind(string? name) =>
        RecognitionNames.Kinds.FirstOrDefault(k => Fold(k.Value) == Fold(name)) is { Value: not null } match ? match.Key : DocumentKind.Unknown;

    private static ReadingFieldName? Field(string? name) =>
        RecognitionNames.Fields.FirstOrDefault(f => Fold(f.Key) == Fold(name)) is { Key: not null } match ? match.Value : null;

    private static string Fold(string? name) => (name ?? "").Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();

    /// <summary>A property of an object; null when the element is no object or lacks it (servers differ, and TryGetProperty throws on anything but an object).</summary>
    private static JsonElement? Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    private static string? StringProperty(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>
    /// Why the server said no, as an exception the worker can act on: a refused key or a wrong address is told so, a picture the server will
    /// not take is final, and anything else (a server or model problem, a limit) is worth another try later.
    /// </summary>
    private static async Task<Exception> ErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var (code, aboutImage) = await ErrorInfoAsync(response, ct);
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => KeyRefused(),
            HttpStatusCode.NotFound => new RecognitionUnavailableException(
                "The model server answered 404: Recognition:OpenAiCompatible:BaseUrl must end with the API's version (/v1), and Recognition:OpenAiCompatible:Model must be a model it serves."),
            HttpStatusCode.RequestEntityTooLarge => new RecognitionRejectedException("The model server, or a proxy in front of it, does not take a request as large as this picture."),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity when aboutImage => new RecognitionRejectedException($"The model server cannot read this picture ({code ?? "image refused"})."),
            _ => new RecognitionUnavailableException($"The model server answered {(int)response.StatusCode} ({code ?? "no reason given"})."),
        };
    }

    /// <summary>
    /// A short code out of an error body, and whether the error is about the picture. Servers differ: OpenAI and Ollama write
    /// <c>{"error":{"code","type","param"}}</c> with strings, llama.cpp a number for the code, LM Studio a bare string, and some put an
    /// array around it all. Only a snake_case token is taken (<see cref="SafeToken"/>), never the message.
    /// </summary>
    private static async Task<(string? Code, bool AboutImage)> ErrorInfoAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), default, ct);
            var root = document.RootElement is { ValueKind: JsonValueKind.Array } list && list.GetArrayLength() > 0 ? list[0] : document.RootElement;
            if (Property(root, "error") is not { ValueKind: JsonValueKind.Object } error) return (null, false);
            var code = StringProperty(error, "code") is { } token && SafeToken().IsMatch(token) ? token : null;
            var aboutImage = new[] { "code", "param" }.Any(p => StringProperty(error, p)?.Contains("image", StringComparison.OrdinalIgnoreCase) == true);
            return (code, aboutImage);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or HttpRequestException)
        {
            return (null, false); // not an error body (a proxy's page, say)
        }
    }

    private static RecognitionUnavailableException KeyRefused() =>
        new("The model server refused the key: Recognition:OpenAiCompatible:ApiKey must be a key it accepts (none is sent while it is empty).");

    private static RecognitionUnavailableException NotTheJson() => new("The model's answer is not the JSON it was asked for.");

    private static RecognitionUnavailableException NotAChatCompletion() => new("The model server's answer is not the JSON expected of a chat completion.");

    private static RecognitionUnavailableException NotModels() =>
        new("The model server's list of models cannot be understood: is Recognition:OpenAiCompatible:BaseUrl the API's address, ending in its version (/v1)?");

    /// <summary>The ids of <c>GET /v1/models</c> (<c>{"data":[{"id":...}]}</c>); null when it is not that at all.</summary>
    private static List<string>? ModelIds(JsonElement root) =>
        Property(root, "data") is { ValueKind: JsonValueKind.Array } models ? models.EnumerateArray().Select(m => StringProperty(m, "id")).OfType<string>().ToList() : null;

    /// <summary>Whether a listed name is the model wanted: case aside, with or without Ollama's ":latest", or the file name llama.cpp lists.</summary>
    private static bool Matches(string listed, string wanted)
    {
        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        var file = listed.Split('/', '\\')[^1];
        if (file.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) file = file[..^5];
        return Same(listed, wanted) || Same(listed, wanted + ":latest") || Same(listed + ":latest", wanted) || Same(file, wanted);
    }
}
