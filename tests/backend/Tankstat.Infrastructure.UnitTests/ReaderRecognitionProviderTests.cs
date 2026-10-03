using System.Net;
using System.Text;
using Tankstat.Application.Recognition;
using Tankstat.Domain.Recognition;
using Tankstat.Infrastructure.Recognition;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>The adapter against the reader's contract v1 (the same example bodies the reader's own tests check it writes).</summary>
public class ReaderRecognitionProviderTests
{
    private static readonly byte[] Picture = [0x52, 0x49, 0x46, 0x46, 1, 2, 3];

    private static string Contract(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contracts", name));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, byte[] Body)> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request, request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct)));
            return await respond(request, ct);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (ReaderRecognitionProvider Provider, Handler Handler) Reader(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, string url = "http://reader:8081", int timeoutSeconds = 30)
    {
        var handler = new Handler(respond);
        return (new ReaderRecognitionProvider(new Factory(handler), new ReaderRecognitionOptions { BaseUrl = url, ApiKey = "secret", TimeoutSeconds = timeoutSeconds }), handler);
    }

    private static RecognitionRequest Request(long? lastOdometer = 123_456, string? currency = "HUF") => new(
        Picture, "image/webp", new HashSet<DocumentKind> { DocumentKind.Odometer, DocumentKind.FuelReceipt }, "hu", lastOdometer, currency, new DateOnly(2026, 10, 1));

    [Fact]
    public async Task Reading_SendsThePictureAndTheHints_AsTheContractSays()
    {
        var (reader, handler) = Reader((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Contract("read-response.fuel-receipt.json"))));

        var result = await reader.ReadAsync(Request(), default);

        var (request, body) = Assert.Single(handler.Seen);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://reader:8081/v1/read?kinds=fuel-receipt,odometer&locale=hu&today=2026-10-01&lastOdometer=123456&currency=HUF", request.RequestUri!.ToString());
        Assert.Equal("secret", Assert.Single(request.Headers.GetValues("X-Api-Key")));
        Assert.Equal("image/webp", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(Picture, body);
        Assert.Equal(("rules-1", DocumentKind.FuelReceipt), (result.ModelVersion, result.Kind));
        Assert.Equal(
            ["Currency=HUF@0.9", "Total=24687@0.94", "Volume=38.52@0.93", "UnitPrice=640.9@0.93", "Date=2026-09-17@0.82"],
            result.Values.Select(v => $"{v.Name}={v.Value}@{v.Confidence}"));
        Assert.All(result.Values, v => Assert.Equal(ValueSource.Read, v.Source));
    }

    [Theory]
    [InlineData("read-response.odometer.json", DocumentKind.Odometer, 1)]
    [InlineData("read-response.fuel-receipt.json", DocumentKind.FuelReceipt, 5)]
    [InlineData("read-response.expense-receipt.json", DocumentKind.ExpenseReceipt, 4)]
    [InlineData("read-response.unknown.json", DocumentKind.Unknown, 0)]
    public async Task EveryAnswerOfTheContract_IsUnderstood(string example, DocumentKind kind, int values)
    {
        var (reader, _) = Reader((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Contract(example))));

        var result = await reader.ReadAsync(Request(), default);

        Assert.Equal((kind, values), (result.Kind, result.Values.Count));
    }

    [Fact]
    public async Task HintsThatAreNotKnown_AreLeftOut_AndAnAddressWithAPathIsKept()
    {
        var (reader, handler) = Reader((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Contract("read-response.unknown.json"))), url: "https://tank.example/reader");

        await reader.ReadAsync(Request(lastOdometer: null, currency: null), default);

        Assert.Equal("https://tank.example/reader/v1/read?kinds=fuel-receipt,odometer&locale=hu&today=2026-10-01", handler.Seen.Single().Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task UnknownFieldsAreSkipped_AndSourcesAreKept()
    {
        const string answer = """
            { "modelVersion": "rules-2", "kind": "fuel-receipt", "fields": [
              { "name": "station", "value": "MOL", "confidence": 0.9, "source": "ocr" },
              { "name": "unitPrice", "value": "640.9", "confidence": 0.5, "source": "derived" },
              { "name": "currency", "value": "HUF", "confidence": 0.45, "source": "hint" } ] }
            """;
        var (reader, _) = Reader((_, _) => Task.FromResult(Json(HttpStatusCode.OK, answer)));

        var result = await reader.ReadAsync(Request(), default);

        Assert.Equal(["UnitPrice:Derived", "Currency:Hint"], result.Values.Select(v => $"{v.Name}:{v.Source}"));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "bad_image", true)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "too_large", true)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, "unsupported_type", true)]
    [InlineData(HttpStatusCode.BadRequest, "bad_request", false)] // our request, not the photo: trying again later is all that is left
    [InlineData(HttpStatusCode.ServiceUnavailable, "busy", false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ocr_unavailable", false)]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized", false)]
    [InlineData(HttpStatusCode.InternalServerError, null, false)]
    public async Task Errors_AreToldApart_ByWhetherTryingAgainCanHelp(HttpStatusCode status, string? code, bool rejected)
    {
        var body = code is null ? new StringContent("<html>proxy error</html>") : new StringContent($$"""{ "code": "{{code}}", "message": "no" }""", Encoding.UTF8, "application/json");
        var (reader, _) = Reader((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = body }));

        var error = await Assert.ThrowsAnyAsync<Exception>(() => reader.ReadAsync(Request(), default));

        Assert.IsType(rejected ? typeof(RecognitionRejectedException) : typeof(RecognitionUnavailableException), error);
        if (status == HttpStatusCode.Unauthorized) Assert.Contains("Recognition:Reader:ApiKey", error.Message);
    }

    [Fact]
    public async Task TheContractsErrorExample_IsABusyReader()
    {
        var (reader, _) = Reader((_, _) => Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, Contract("error.json"))));

        var error = await Assert.ThrowsAsync<RecognitionUnavailableException>(() => reader.ReadAsync(Request(), default));

        Assert.Contains("busy", error.Message);
    }

    [Fact]
    public async Task AReaderThatCannotBeReached_IsTooSlow_OrAnswersNonsense_IsUnavailable()
    {
        var (unreachable, _) = Reader((_, _) => throw new HttpRequestException("connection refused"));
        var (slow, _) = Reader(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(10), ct); return Json(HttpStatusCode.OK, "{}"); }, timeoutSeconds: 1);
        var (nonsense, _) = Reader((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "not json")));

        await Assert.ThrowsAsync<RecognitionUnavailableException>(() => unreachable.ReadAsync(Request(), default));
        var timedOut = await Assert.ThrowsAsync<RecognitionUnavailableException>(() => slow.ReadAsync(Request(), default));
        await Assert.ThrowsAsync<RecognitionUnavailableException>(() => nonsense.ReadAsync(Request(), default));
        Assert.Contains("1 seconds", timedOut.Message);
    }

    [Fact]
    public async Task Health_IsOkOnlyWhenTheReaderSaysSo()
    {
        var (healthy, handler) = Reader((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Contract("health.json"))));
        var (unavailable, _) = Reader((_, _) => Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, """{ "status": "unavailable" }""")));
        var (unreachable, _) = Reader((_, _) => throw new HttpRequestException("connection refused"));

        Assert.True(await healthy.IsHealthyAsync(default));
        Assert.False(await unavailable.IsHealthyAsync(default));
        Assert.False(await unreachable.IsHealthyAsync(default));
        var (request, _) = handler.Seen.Single();
        Assert.Equal(("http://reader:8081/v1/health", false), (request.RequestUri!.ToString(), request.Headers.Contains("X-Api-Key"))); // health needs no key
    }
}
