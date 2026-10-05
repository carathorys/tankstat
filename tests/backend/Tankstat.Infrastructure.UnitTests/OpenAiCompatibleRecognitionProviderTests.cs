using System.Net;
using System.Text;
using System.Text.Json;
using Tankstat.Application.Recognition;
using Tankstat.Domain.Recognition;
using Tankstat.Infrastructure.Recognition;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>The adapter to an OpenAI-compatible chat completions API, against answers as OpenAI, LM Studio, Ollama and llama.cpp write them.</summary>
public class OpenAiCompatibleRecognitionProviderTests
{
    private static readonly byte[] Picture = [0x52, 0x49, 0x46, 0x46, 1, 2, 3];

    private static string Contract(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contracts", "openai-compatible", name));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>An answer in OpenAI's envelope around what the model said.</summary>
    private static string Said(string content, string finish = "stop", string model = "m-1") =>
        JsonSerializer.Serialize(new { model, choices = new[] { new { message = new { role = "assistant", content }, finish_reason = finish } } });

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return await respond(request, ct);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (OpenAiCompatibleRecognitionProvider Provider, Handler Handler) Model(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, string url = "http://model:1234/v1", string? key = null, string? prompt = null,
        OpenAiResponseFormat format = OpenAiResponseFormat.JsonSchema, double? temperature = null, int timeoutSeconds = 30, string model = "qwen2.5-vl")
    {
        var handler = new Handler(respond);
        var options = new OpenAiCompatibleRecognitionOptions { BaseUrl = url, ApiKey = key, Model = model, ResponseFormat = format, Temperature = temperature, TimeoutSeconds = timeoutSeconds };
        return (new OpenAiCompatibleRecognitionProvider(new Factory(handler), options, prompt), handler);
    }

    private static RecognitionRequest Request(long? lastOdometer = 123_456, string? currency = "HUF", DocumentKind receipt = DocumentKind.FuelReceipt) => new(
        Picture, "image/webp", new HashSet<DocumentKind> { DocumentKind.Odometer, receipt }, "hu", lastOdometer, currency, new DateOnly(2026, 10, 1));

    private static JsonElement Body(Handler handler) => JsonDocument.Parse(handler.Seen.Single().Body).RootElement;

    private static string Shown(RecognitionResult result) => string.Join(' ', result.Values.Select(v => $"{v.Name}={v.Value}@{v.Confidence}"));

    [Fact]
    public async Task Reading_SendsThePhotoAsADataUrl_WithThePromptAndTheContract_AndAsksForTheShapeOfTheAnswer()
    {
        var (model, handler) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Contract("chat.fuel-receipt.json"))));

        var result = await model.ReadAsync(Request(), default);

        var (request, _) = Assert.Single(handler.Seen);
        Assert.Equal((HttpMethod.Post, "http://model:1234/v1/chat/completions", false), (request.Method, request.RequestUri!.ToString(), request.Headers.Contains("Authorization")));
        var body = Body(handler);
        Assert.Equal(("qwen2.5-vl", false), (body.GetProperty("model").GetString(), body.GetProperty("stream").GetBoolean()));
        Assert.False(body.TryGetProperty("temperature", out _));
        var messages = body.GetProperty("messages");
        Assert.Equal(("system", OpenAiCompatiblePrompt.DefaultSystemPrompt), (messages[0].GetProperty("role").GetString(), messages[0].GetProperty("content").GetString()));
        var user = messages[1].GetProperty("content");
        Assert.Equal("data:image/webp;base64," + Convert.ToBase64String(Picture), user[0].GetProperty("image_url").GetProperty("url").GetString());
        var contract = user[1].GetProperty("text").GetString()!;
        Assert.Contains("\"fuel-receipt\"", contract);
        Assert.Contains("yyyy-MM-dd", contract);
        Assert.Contains("Hungarian", contract);
        Assert.DoesNotContain("123456", contract); // the hints stay with the app: a model given a number tends to answer with it
        var format = body.GetProperty("response_format");
        Assert.Equal(("json_schema", true), (format.GetProperty("type").GetString(), format.GetProperty("json_schema").GetProperty("strict").GetBoolean()));
        var schema = format.GetProperty("json_schema").GetProperty("schema");
        Assert.Equal(["odometer", "fuel-receipt", "unknown"], schema.GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["odometer", "total", "volume", "unitPrice", "currency", "date"],
            schema.GetProperty("properties").GetProperty("fields").GetProperty("items").GetProperty("properties").GetProperty("name").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(("gpt-vision-example", DocumentKind.FuelReceipt), (result.ModelVersion, result.Kind));
        Assert.Equal("Total=24687@0.95 Volume=38.52@0.9 UnitPrice=640.9@0.9 Currency=HUF@0.9 Date=2026-09-17@0.85", Shown(result));
        Assert.All(result.Values, v => Assert.Equal(ValueSource.Read, v.Source));
    }

    [Fact]
    public async Task AKey_GoesAsABearerToken_AndTheOperatorsPrompt_ReplacesTheBuiltInOne_ButNeverTheContract()
    {
        var (model, handler) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Contract("chat.unknown.json"))), url: "https://api.example/v1/", key: "secret", prompt: "Read it your way.");

        var result = await model.ReadAsync(Request(), default);

        var (request, _) = handler.Seen.Single();
        Assert.Equal(("https://api.example/v1/chat/completions", "Bearer secret"), (request.RequestUri!.ToString(), request.Headers.Authorization!.ToString()));
        var messages = Body(handler).GetProperty("messages");
        Assert.Equal("Read it your way.", messages[0].GetProperty("content").GetString());
        Assert.Contains("\"kind\"", messages[1].GetProperty("content")[1].GetProperty("text").GetString());
        Assert.Equal((DocumentKind.Unknown, 0), (result.Kind, result.Values.Count));
    }

    [Theory]
    [InlineData(OpenAiResponseFormat.JsonSchema, "json_schema")]
    [InlineData(OpenAiResponseFormat.JsonObject, "json_object")]
    [InlineData(OpenAiResponseFormat.None, null)]
    public async Task TheShapeOfTheAnswer_IsAskedForTheWayTheSettingSays_AndSoIsTheTemperature(OpenAiResponseFormat format, string? type)
    {
        var (model, handler) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Contract("chat.unknown.json"))), format: format, temperature: 0);

        await model.ReadAsync(Request(), default);

        var body = Body(handler);
        Assert.Equal(type, body.TryGetProperty("response_format", out var asked) ? asked.GetProperty("type").GetString() : null);
        Assert.Equal(0, body.GetProperty("temperature").GetDouble());
    }

    [Theory]
    [InlineData("chat.odometer.json", DocumentKind.FuelReceipt, DocumentKind.Odometer, "Odometer=123789")]
    [InlineData("chat.fuel-receipt.json", DocumentKind.FuelReceipt, DocumentKind.FuelReceipt, "Total=24687 Volume=38.52 UnitPrice=640.9 Currency=HUF Date=2026-09-17")]
    [InlineData("chat.expense-receipt.json", DocumentKind.ExpenseReceipt, DocumentKind.ExpenseReceipt, "Total=12.5 Currency=EUR Date=2026-09-30 Title=Shell")]
    [InlineData("chat.unknown.json", DocumentKind.FuelReceipt, DocumentKind.Unknown, "")]
    [InlineData("chat.fenced.json", DocumentKind.FuelReceipt, DocumentKind.Odometer, "Odometer=123789")] // a reasoning block, code fences, a number for the value
    [InlineData("chat.text-parts.json", DocumentKind.FuelReceipt, DocumentKind.Odometer, "Odometer=123789")]
    public async Task EveryShapeOfAnswerTheServersWrite_IsUnderstood(string example, DocumentKind receipt, DocumentKind kind, string values)
    {
        var (model, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Contract(example))));

        var result = await model.ReadAsync(Request(receipt: receipt), default);

        Assert.Equal((kind, values), (result.Kind, string.Join(' ', result.Values.Select(v => $"{v.Name}={v.Value}"))));
    }

    [Fact]
    public async Task AKindThePhotoMayNotShow_CountsAsUnknown_AndNamesAreReadWhateverTheirSpelling()
    {
        var (receipt, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Contract("chat.expense-receipt.json"))));
        var (spelled, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Said("""{"kind": "Fuel receipt", "fields": [{"name": "unit_price", "value": "640.9", "confidence": 0.9}, {"name": "Total", "value": "24687", "confidence": 0.9}]}"""))));

        var unknown = await receipt.ReadAsync(Request(receipt: DocumentKind.FuelReceipt), default); // an expense receipt was not on the cards
        var read = await spelled.ReadAsync(Request(), default);

        Assert.Equal((DocumentKind.Unknown, 0), (unknown.Kind, unknown.Values.Count));
        Assert.Equal((DocumentKind.FuelReceipt, "UnitPrice=640.9@0.9 Total=24687@0.9"), (read.Kind, Shown(read)));
    }

    [Fact]
    public async Task TheChecksApply_ToWhatAModelReads()
    {
        var (far, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Said("""{"kind":"odometer","fields":[{"name":"odometer","value":"999999","confidence":0.99}]}"""))));
        var (misfit, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Said("""{"kind":"fuel-receipt","fields":[{"name":"total","value":"30000","confidence":0.9},{"name":"volume","value":"38.52","confidence":0.9},{"name":"unitPrice","value":"640.9","confidence":0.9}]}"""))));

        var dropped = await far.ReadAsync(Request(lastOdometer: 123_456), default); // far more than a hundred thousand since the last log
        var doubted = await misfit.ReadAsync(Request(), default);

        Assert.Equal((DocumentKind.Odometer, 0), (dropped.Kind, dropped.Values.Count));
        Assert.Equal("Total=30000@0.9 Volume=38.52@0.5 UnitPrice=640.9@0.5", Shown(doubted));
    }

    [Fact]
    public async Task ARefusal_IsFinal_WhileACutOffOrUnusableAnswer_IsWorthAnotherTry()
    {
        var (refused, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Contract("chat.refusal.json"))));
        var (filtered, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Said("", finish: "content_filter"))));
        var (cutOff, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Contract("chat.cut-off.json"))));
        var (prose, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Said("I cannot see an odometer here, the number 987654 is the trip."))));
        var (silent, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Said(""))));
        var (noChoice, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{ "choices": [] }""")));
        var (nonsense, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "not json")));

        await Assert.ThrowsAsync<RecognitionRejectedException>(() => refused.ReadAsync(Request(), default));
        await Assert.ThrowsAsync<RecognitionRejectedException>(() => filtered.ReadAsync(Request(), default));
        await Assert.ThrowsAsync<RecognitionUnavailableException>(() => cutOff.ReadAsync(Request(), default));
        var talked = await Assert.ThrowsAsync<RecognitionUnavailableException>(() => prose.ReadAsync(Request(), default));
        await Assert.ThrowsAsync<RecognitionUnavailableException>(() => silent.ReadAsync(Request(), default));
        await Assert.ThrowsAsync<RecognitionUnavailableException>(() => noChoice.ReadAsync(Request(), default));
        await Assert.ThrowsAsync<RecognitionUnavailableException>(() => nonsense.ReadAsync(Request(), default));
        Assert.DoesNotContain("987654", talked.Message); // what the model said is never repeated: it could be anything
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, null, false, "Recognition:OpenAiCompatible:ApiKey")]
    [InlineData(HttpStatusCode.Forbidden, null, false, "Recognition:OpenAiCompatible:ApiKey")]
    [InlineData(HttpStatusCode.NotFound, null, false, "/v1")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, null, true, "large")]
    [InlineData(HttpStatusCode.BadRequest, "error.openai.json", true, "invalid_image")]
    [InlineData(HttpStatusCode.BadRequest, "error.llamacpp.json", false, "400")] // a number for a code, nothing about the picture in a token: our request, so later
    [InlineData(HttpStatusCode.BadRequest, "error.lmstudio.json", false, "400")]
    [InlineData(HttpStatusCode.TooManyRequests, null, false, "429")]
    [InlineData(HttpStatusCode.UnsupportedMediaType, null, false, "415")]
    [InlineData(HttpStatusCode.InternalServerError, null, false, "500")]
    public async Task Errors_AreToldApart_ByWhetherTryingAgainCanHelp_WithoutRepeatingTheServersWords(HttpStatusCode status, string? example, bool rejected, string said)
    {
        var body = example is null ? new StringContent("<html>proxy error</html>") : new StringContent(Contract(example), Encoding.UTF8, "application/json");
        var (model, _) = Model((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = body }));

        var error = await Assert.ThrowsAnyAsync<Exception>(() => model.ReadAsync(Request(), default));

        Assert.IsType(rejected ? typeof(RecognitionRejectedException) : typeof(RecognitionUnavailableException), error);
        Assert.Contains(said, error.Message);
        Assert.DoesNotContain("Invalid image.", error.Message);
        Assert.DoesNotContain("not loaded", error.Message);
        Assert.DoesNotContain("not supported", error.Message);
    }

    [Fact]
    public async Task AServerThatCannotBeReached_OrIsTooSlow_IsUnavailable()
    {
        var (unreachable, _) = Model((_, _) => throw new HttpRequestException("connection refused"));
        var (slow, _) = Model(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(10), ct); return Json(HttpStatusCode.OK, "{}"); }, timeoutSeconds: 1);

        var down = await Assert.ThrowsAsync<RecognitionUnavailableException>(() => unreachable.ReadAsync(Request(), default));
        var timedOut = await Assert.ThrowsAsync<RecognitionUnavailableException>(() => slow.ReadAsync(Request(), default));

        Assert.IsType<HttpRequestException>(down.InnerException);
        Assert.Contains("1 seconds", timedOut.Message);
    }

    [Fact]
    public async Task TheModelsName_IsKeptAsTheVersion_CutToWhatTheDatabaseHolds_OrElseTheConfiguredOne()
    {
        var longName = new string('m', 80);
        var (named, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Said("""{"kind":"unknown","fields":[]}""", model: longName))));
        var (unnamed, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{ "choices": [ { "message": { "content": "{\"kind\":\"unknown\"}" } } ] }""")));

        Assert.Equal(new string('m', 64), (await named.ReadAsync(Request(), default)).ModelVersion);
        Assert.Equal("qwen2.5-vl", (await unnamed.ReadAsync(Request(), default)).ModelVersion);
    }

    [Theory]
    [InlineData("models.openai.json", "gpt-vision-example", true)]
    [InlineData("models.openai.json", "GPT-Vision-Example", true)] // case does not matter
    [InlineData("models.openai.json", "gpt-other", false)]
    [InlineData("models.ollama.json", "llama3.2-vision", true)] // Ollama lists the tag
    [InlineData("models.ollama.json", "qwen2.5vl:7b", true)]
    [InlineData("models.ollama.json", "qwen2.5vl", false)]
    [InlineData("models.llamacpp.json", "anything", true)] // one model: the server answers with it whatever is asked for
    [InlineData("models.lmstudio.json", "qwen2.5-vl-7b-instruct", true)]
    [InlineData("models.lmstudio.json", "qwen2.5-vl", false)]
    public async Task Health_NeedsTheModelAmongThoseListed_UnlessThereIsOnlyOne(string example, string wanted, bool healthy)
    {
        var (model, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Contract(example))), model: wanted);

        if (healthy)
        {
            Assert.True(await model.IsHealthyAsync(default));
        }
        else
        {
            var missing = await Assert.ThrowsAsync<RecognitionUnavailableException>(() => model.IsHealthyAsync(default));
            Assert.Contains("Recognition:OpenAiCompatible:Model", missing.Message);
        }
    }

    [Fact]
    public async Task Health_AsksForTheModels_WithTheKey_AndOtherwiseSaysWhyNot()
    {
        var (keyed, handler) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Contract("models.openai.json"))), key: "secret", model: "gpt-vision-example");
        var (refusing, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.Unauthorized, """{ "error": { "message": "Incorrect API key provided", "type": "invalid_request_error", "code": "invalid_api_key" } }""")));
        var (broken, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, "<html>down</html>")));
        var (empty, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{ "object": "list", "data": [] }""")));
        var (unreachable, _) = Model((_, _) => throw new HttpRequestException("connection refused"));
        var (nonsense, _) = Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "<html>a web page, not an API</html>")));

        Assert.True(await keyed.IsHealthyAsync(default));
        var (request, _) = handler.Seen.Single();
        Assert.Equal(("http://model:1234/v1/models", "Bearer secret"), (request.RequestUri!.ToString(), request.Headers.Authorization!.ToString()));
        var refused = await Assert.ThrowsAsync<RecognitionUnavailableException>(() => refusing.IsHealthyAsync(default));
        Assert.Contains("Recognition:OpenAiCompatible:ApiKey", refused.Message);
        Assert.DoesNotContain("Incorrect", refused.Message);
        var down = await Assert.ThrowsAsync<RecognitionUnavailableException>(() => broken.IsHealthyAsync(default));
        Assert.Contains("503", down.Message); // what an operator has to go by: it answered, with an error
        var none = await Assert.ThrowsAsync<RecognitionUnavailableException>(() => empty.IsHealthyAsync(default));
        Assert.Contains("no models", none.Message);
        var away = await Assert.ThrowsAsync<RecognitionUnavailableException>(() => unreachable.IsHealthyAsync(default));
        Assert.IsType<HttpRequestException>(away.InnerException); // and the cause behind "could not be reached"
        var page = await Assert.ThrowsAsync<RecognitionUnavailableException>(() => nonsense.IsHealthyAsync(default));
        Assert.Contains("Recognition:OpenAiCompatible:BaseUrl", page.Message);
    }

    [Fact]
    public async Task AHealthCheckThatIsCancelledByTheCaller_IsACancellation_NotAnUnavailableServer()
    {
        var (model, _) = Model(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(10), ct); return Json(HttpStatusCode.OK, "{}"); });
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.IsHealthyAsync(cancelled.Token));
    }

    [Fact]
    public async Task NeitherTheKey_NorThePrompt_NorWhatTheServerOrTheModelSaid_EverReachesAnErrorMessage()
    {
        const string key = "hunter2-key", prompt = "SECRET PROMPT TEXT", server = "leak-me-server-message", read = "987654";
        var problems = new List<Exception>();
        async Task Collect(OpenAiCompatibleRecognitionProvider model, bool health = false)
        {
            problems.Add(await Assert.ThrowsAnyAsync<Exception>(() => health ? model.IsHealthyAsync(default) : model.ReadAsync(Request(), default)));
        }
        var error = (HttpStatusCode status, string code) => Json(status, $$"""{ "error": { "message": "{{server}}", "type": "invalid_request_error", "code": "{{code}}" } }""");

        await Collect(Model((_, _) => Task.FromResult(error(HttpStatusCode.Unauthorized, "invalid_api_key")), key: key, prompt: prompt).Provider);
        await Collect(Model((_, _) => Task.FromResult(error(HttpStatusCode.BadRequest, "invalid_image")), key: key, prompt: prompt).Provider);
        await Collect(Model((_, _) => Task.FromResult(error(HttpStatusCode.BadRequest, server)), key: key, prompt: prompt).Provider); // a "code" that is a sentence is no code
        await Collect(Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Said($"The odometer reads {read} km, I think."))), key: key, prompt: prompt).Provider);
        await Collect(Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{" + server)), key: key, prompt: prompt).Provider);
        await Collect(Model((_, _) => Task.FromResult(error(HttpStatusCode.Unauthorized, "invalid_api_key")), key: key, prompt: prompt).Provider, health: true);
        await Collect(Model((_, _) => Task.FromResult(Json(HttpStatusCode.InternalServerError, server)), key: key, prompt: prompt).Provider, health: true);
        await Collect(Model((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "<html>" + server)), key: key, prompt: prompt).Provider, health: true);

        Assert.Equal(8, problems.Count);
        foreach (var text in problems.SelectMany(p => new[] { p.Message, p.InnerException?.Message ?? "" }))
            foreach (var secret in new[] { key, prompt, server, read })
                Assert.DoesNotContain(secret, text);
    }
}
