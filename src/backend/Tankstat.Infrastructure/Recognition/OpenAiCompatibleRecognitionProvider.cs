using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tankstat.Application.Recognition;
using Tankstat.Domain.Recognition;

namespace Tankstat.Infrastructure.Recognition;

/// <summary>
/// A vision model behind an OpenAI-compatible chat completions API: a server the operator runs (LM Studio, Ollama, llama.cpp, vLLM) or a
/// paid one. <c>POST {BaseUrl}/chat/completions</c> gets the photo as a data URL with the prompt (<see cref="OpenAiCompatiblePrompt"/>) and
/// answers with JSON that is read tolerantly (servers and models differ); <c>GET {BaseUrl}/models</c> is the health check. The values come
/// back through <see cref="ReadingChecks"/>, because a model's own confidence is not to be believed on its own. Exceptions carry fixed
/// words, a status and at most a short error code: the worker logs them, and a server's message (or the model's) could hold anything.
/// </summary>
internal sealed partial class OpenAiCompatibleRecognitionProvider(IHttpClientFactory http, OpenAiCompatibleRecognitionOptions options, string? systemPrompt)
    : IRecognitionProvider
{
    public const string ClientName = "recognition-openai-compatible";
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(5);

    /// <summary>What the database keeps of a model's name (<c>PhotoReading.ModelVersion</c>).</summary>
    private const int MaxModelVersionLength = 64;

    // An error code is a snake_case token ("invalid_api_key", "invalid_image"); anything else in that place is words, and words are never repeated.
    [GeneratedRegex(@"^[a-z0-9_]{1,32}$")]
    private static partial Regex SafeToken();

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex Thinking();

    // A base address with a path keeps it: relative URIs resolve below the trailing slash.
    private readonly Uri _base = new(options.BaseUrl!.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/");
    private readonly string _systemPrompt = systemPrompt ?? OpenAiCompatiblePrompt.DefaultSystemPrompt;

    public string Name => "openai-compatible";
    public bool IsConfigured => true;

    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HealthTimeout);
        try
        {
            using var request = Request(HttpMethod.Get, "models");
            using var response = await http.CreateClient(ClientName).SendAsync(request, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw Refused();
            if (!response.IsSuccessStatusCode) throw new RecognitionUnavailableException($"The model server answered {(int)response.StatusCode} when asked for its models.");
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), default, timeout.Token);
            var listed = ModelIds(document.RootElement) ?? throw new JsonException("not a list of models");
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
            throw new RecognitionUnavailableException("The model server's list of models cannot be understood: is Recognition:OpenAiCompatible:BaseUrl the API's address, ending in its version (/v1)?");
        }
    }

    public async Task<RecognitionResult> ReadAsync(RecognitionRequest request, CancellationToken ct)
    {
        using var message = Request(HttpMethod.Post, "chat/completions");
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
                throw new RecognitionUnavailableException("The model server's answer is not the JSON expected of a chat completion.");
            }
            using (document) return Result(document.RootElement, request);
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
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
    private RecognitionResult Result(JsonElement root, RecognitionRequest request)
    {
        var version = Version(root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String ? model.GetString() : null);
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new RecognitionUnavailableException("The model server answered without a choice.");
        var choice = choices[0];
        var finish = choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;
        var message = choice.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
        if (finish == "content_filter" || Refusal(message)) throw new RecognitionRejectedException("The model refused to read this picture.");
        if (finish == "length") throw new RecognitionUnavailableException("The model's answer was cut off: the server's limit on the length of an answer is too low for it.");
        var text = Text(message);
        if (string.IsNullOrWhiteSpace(text)) throw new RecognitionUnavailableException("The model answered nothing.");

        var (kind, values) = Reading(text);
        return kind != DocumentKind.Unknown && request.Kinds.Contains(kind)
            ? new RecognitionResult(version, kind, ReadingChecks.Apply(kind, values, request))
            : new RecognitionResult(version, DocumentKind.Unknown, []);
    }

    private string Version(string? reported)
    {
        var name = string.IsNullOrWhiteSpace(reported) ? options.Model ?? "" : reported;
        return name.Length <= MaxModelVersionLength ? name : name[..MaxModelVersionLength];
    }

    private static bool Refusal(JsonElement message) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(refusal.GetString());

    /// <summary>The model's text: a string, or (some servers) the text parts of a list.</summary>
    private static string? Text(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("content", out var content)) return null;
        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => string.Concat(content.EnumerateArray()
                .Where(part => part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                .Select(part => part.GetProperty("text").GetString())),
            _ => null,
        };
    }

    /// <summary>The JSON object in the model's text, whatever is around it (a reasoning block, code fences, a sentence), as a kind and its values.</summary>
    private static (DocumentKind Kind, List<RecognizedValue> Values) Reading(string text)
    {
        var bare = Thinking().Replace(text, "");
        var start = bare.IndexOf('{');
        var end = bare.LastIndexOf('}');
        if (start < 0 || end <= start) throw NotTheJson();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bare[start..(end + 1)]);
        }
        catch (JsonException)
        {
            throw NotTheJson();
        }
        using (document)
        {
            var root = document.RootElement;
            var kind = Kind(root.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null);
            var values = new List<RecognizedValue>();
            if (root.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
                foreach (var field in fields.EnumerateArray())
                {
                    if (field.ValueKind != JsonValueKind.Object) continue;
                    if (Field(field.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null) is not { } name) continue;
                    var value = field.TryGetProperty("value", out var v) ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null } : null;
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    var confidence = field.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetDouble(out var d) ? d : ReadingChecks.Doubtful;
                    values.Add(new RecognizedValue(name, value, confidence, ValueSource.Read));
                }
            return (kind, values);
        }
    }

    private static RecognitionUnavailableException NotTheJson() => new("The model's answer is not the JSON it was asked for.");

    // Without a schema a model may spell the names its own way ("Fuel receipt", "unit_price"): the letters decide.
    private static DocumentKind Kind(string? name) => RecognitionNames.Kind(name?.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-'));

    private static ReadingFieldName? Field(string? name)
    {
        var folded = Fold(name);
        return RecognitionNames.Fields.FirstOrDefault(f => Fold(f.Key) == folded) is { Key: not null } match ? match.Value : null;
    }

    private static string Fold(string? name) => (name ?? "").Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();

    /// <summary>
    /// Why the server said no, as an exception the worker can act on: a refused key or a wrong address is told so, a picture the server will
    /// not take is final, and anything else (a server or model problem, a limit) is worth another try later.
    /// </summary>
    private static async Task<Exception> ErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var (code, aboutImage) = await ErrorInfoAsync(response, ct);
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Refused(),
            HttpStatusCode.NotFound => new RecognitionUnavailableException(
                "The model server answered 404: Recognition:OpenAiCompatible:BaseUrl must end with the API's version (/v1), and Recognition:OpenAiCompatible:Model must be a model it serves."),
            HttpStatusCode.RequestEntityTooLarge => new RecognitionRejectedException("The model server, or a proxy in front of it, does not take a request as large as this picture."),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity when aboutImage => new RecognitionRejectedException($"The model server cannot read this picture ({code ?? "image refused"})."),
            _ => new RecognitionUnavailableException($"The model server answered {(int)response.StatusCode} ({code ?? "no reason given"})."),
        };
    }

    private static RecognitionUnavailableException Refused() =>
        new("The model server refused the key: Recognition:OpenAiCompatible:ApiKey must be a key it accepts (none is sent while it is empty).");

    /// <summary>
    /// A short code out of an error body, and whether the error is about the picture. Servers differ: OpenAI and Ollama write
    /// <c>{"error":{"code","type","param"}}</c> with strings, llama.cpp a number for the code, LM Studio a bare string. Only a snake_case
    /// token is taken (<see cref="SafeToken"/>), never the message.
    /// </summary>
    private static async Task<(string? Code, bool AboutImage)> ErrorInfoAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), default, ct);
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return (null, false);
            string[] told = ["code", "type", "param"];
            var strings = told.Select(p => error.TryGetProperty(p, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null).ToList();
            var code = strings.Take(2).FirstOrDefault(s => s is not null && SafeToken().IsMatch(s));
            return (code, strings.Any(s => s?.Contains("image", StringComparison.OrdinalIgnoreCase) == true));
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or HttpRequestException)
        {
            return (null, false); // not an error body (a proxy's page, say)
        }
    }

    /// <summary>The ids of <c>GET /v1/models</c> (<c>{"data":[{"id":...}]}</c>, or a bare list); null when it is not that at all.</summary>
    private static List<string>? ModelIds(JsonElement root)
    {
        var list = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) ? data : root;
        if (list.ValueKind != JsonValueKind.Array) return null;
        return list.EnumerateArray()
            .Select(model => model.ValueKind == JsonValueKind.Object && model.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null)
            .OfType<string>()
            .ToList();
    }

    /// <summary>Whether a listed name is the model wanted: case aside, with or without Ollama's ":latest", or the file name llama.cpp lists.</summary>
    private static bool Matches(string listed, string wanted)
    {
        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        var file = listed.Split('/', '\\')[^1];
        if (file.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) file = file[..^5];
        return Same(listed, wanted) || Same(listed, wanted + ":latest") || Same(listed + ":latest", wanted) || Same(file, wanted);
    }
}
