using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Tankstat.Reader.Core;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.UnitTests;
using Tankstat.Reader.Ocr;

namespace Tankstat.Reader.IntegrationTests;

public class EndpointTests
{
    private static readonly string[] Receipt =
    [
        "Benzinkút Kft.", "NYUGTA", "Gázolaj\t24 687 Ft", "38,52 l x 640,9 Ft/l", "!ÖSSZESEN:\t24 687 Ft", "Bankkártya\t24 687 Ft", "2026.09.17 18:15",
    ];

    private static JsonNode Fixture(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contracts", name)))!;

    private static IEnumerable<string> Keys(JsonNode node) => node.AsObject().Select(p => p.Key);

    [Fact]
    public async Task Health_IsOpen_AndShapedLikeTheContract()
    {
        using var app = new ReaderApp();

        var response = await app.Client(key: null).GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(Keys(Fixture("health.json")), Keys(body));
        Assert.Equal(Keys(Fixture("health.json")["ocr"]!), Keys(body["ocr"]!));
        Assert.Equal(("ok", "rules-1", "tesseract"), ((string?)body["status"], (string?)body["modelVersion"], (string?)body["ocr"]!["engine"]));
    }

    [Fact]
    public async Task Health_Is503_WhenTheOcrCannotRun()
    {
        using var app = new ReaderApp();
        app.Status.Current = new TesseractInfo(false, null, [], "", "", "not installed");

        var response = await app.Client(key: null).GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("unavailable", (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["status"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    [InlineData("")]
    public async Task Reading_NeedsTheApiKey(string? key)
    {
        using var app = new ReaderApp();

        var response = await app.Client(key).PostAsync("/v1/read", TestImages.Content(TestImages.Make()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(Keys(Fixture("error.json")), Keys(body));
        Assert.Equal("unauthorized", (string?)body["code"]);
        Assert.Empty(app.Ocr.Passes);
        var warning = Assert.Single(app.Log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("X-Api-Key", warning.Message);
    }

    [Fact]
    public async Task ARefusedKey_IsLogged_WithoutTheKeyThatWasSent()
    {
        using var app = new ReaderApp();

        var response = await app.Client("canary-secret-key").PostAsync("/v1/read", TestImages.Content(TestImages.Make()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(app.Log.Mentions("canary-secret-key"));
        Assert.False(app.Log.Mentions(ReaderApp.Key)); // nor the right one
    }

    [Fact]
    public async Task AReceipt_IsReadIntoTheContractShape_WithTheHints()
    {
        using var app = new ReaderApp();
        app.Ocr.On(ImageVariant.Normal, 6, OcrPurpose.Text, OcrFixture.Page(Receipt));

        var response = await app.Client().PostAsync("/v1/read?kinds=odometer,fuel-receipt&locale=hu&today=2026-10-03", TestImages.Content(TestImages.Make()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(Keys(Fixture("read-response.fuel-receipt.json")), Keys(body));
        var result = body.Deserialize<ReadResult>(ReaderJson.Options)!;
        Assert.Equal(("fuel-receipt", "24687", "38.52", "640.9", "HUF", "2026-09-17"),
            (result.Kind, result.Value(FieldNames.Total), result.Value(FieldNames.Volume), result.Value(FieldNames.UnitPrice), result.Value(FieldNames.Currency), result.Value(FieldNames.Date)));

        // The log says that a receipt was read and how long it took: never what was on it (the user's data).
        var read = Assert.Single(app.Log.Entries, e => e.Message.StartsWith("Read a", StringComparison.Ordinal));
        Assert.Contains("fuel-receipt", read.Message);
        foreach (var secret in new[] { "24687", "24 687", "38.52", "38,52", "640.9", "640,9", "2026-09-17", "2026.09.17", "Benzink", "NYUGTA" })
            Assert.False(app.Log.Mentions(secret), secret);
    }

    [Fact]
    public async Task TheLatestOdometerHint_ReachesTheReading()
    {
        using var app = new ReaderApp();
        app.Ocr.On(ImageVariant.Normal, 11, OcrPurpose.Digits, OcrFixture.Page("!99999", "123456", "18 15", "21"));

        var plain = await (await app.Client().PostAsync("/v1/read?kinds=odometer", TestImages.Content(TestImages.Make()))).Content.ReadFromJsonAsync<ReadResult>(ReaderJson.Options);
        var hinted = await (await app.Client().PostAsync("/v1/read?kinds=odometer&lastOdometer=123000", TestImages.Content(TestImages.Make()))).Content.ReadFromJsonAsync<ReadResult>(ReaderJson.Options);

        Assert.Equal(("99999", "123456"), (plain!.Value(FieldNames.Odometer), hinted!.Value(FieldNames.Odometer)));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp")]
    public async Task JpegAndWebp_AreReadToo(SKEncodedImageFormat format, string type)
    {
        using var app = new ReaderApp();

        var response = await app.Client().PostAsync("/v1/read", TestImages.Content(TestImages.Make(format: format), type));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("unknown", (await response.Content.ReadFromJsonAsync<ReadResult>(ReaderJson.Options))!.Kind); // nothing on it: not an error
    }

    [Theory]
    [InlineData("text/plain", HttpStatusCode.UnsupportedMediaType, "unsupported_type")]
    [InlineData("image/gif", HttpStatusCode.UnsupportedMediaType, "unsupported_type")]
    [InlineData("image/png", HttpStatusCode.BadRequest, "bad_image")] // says PNG, is not one
    public async Task WhatIsNotASupportedPicture_IsRefused(string type, HttpStatusCode status, string code)
    {
        using var app = new ReaderApp();

        var response = await app.Client().PostAsync("/v1/read", TestImages.Content("not a picture"u8.ToArray(), type));

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]);
        var line = Assert.Single(app.Log.Entries, e => e.Message.StartsWith("Refused a read", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, line.Level); // the caller's doing, not the reader's trouble
        Assert.Contains(code, line.Message);
    }

    [Fact]
    public async Task ABadRequest_IsLoggedByItsCode_NotByItsMessage_WhichEchoesTheRequest()
    {
        using var app = new ReaderApp();

        var response = await app.Client().PostAsync("/v1/read?kinds=canary-kind", TestImages.Content(TestImages.Make()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("canary-kind", await response.Content.ReadAsStringAsync()); // the caller is told what was wrong with the request...
        var line = Assert.Single(app.Log.Entries, e => e.Message.StartsWith("Refused a read", StringComparison.Ordinal));
        Assert.Contains("bad_request", line.Message);
        Assert.False(app.Log.Mentions("canary-kind")); // ...the log only that it was refused
    }

    [Fact]
    public async Task APictureLargerThanAllowed_Is413()
    {
        using var app = new ReaderApp(new() { ["Reader:MaxImageBytes"] = "1000" });

        var response = await app.Client().PostAsync("/v1/read", TestImages.Content(TestImages.Make()));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("too_large", (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]);
    }

    [Theory]
    [InlineData("kinds=receipt")]
    [InlineData("kinds=,")]
    [InlineData("lastOdometer=-5")]
    [InlineData("lastOdometer=12.5")]
    [InlineData("currency=Euro")]
    [InlineData("today=03.10.2026")]
    public async Task BadHints_Are400(string query)
    {
        using var app = new ReaderApp();

        var response = await app.Client().PostAsync($"/v1/read?{query}", TestImages.Content(TestImages.Make()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bad_request", (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]);
    }

    [Fact]
    public async Task WhenEverySlotAndThePlacesInLineAreTaken_TheReaderSaysBusy()
    {
        var blocked = new BlockingOcr();
        using var app = new ReaderApp(new() { ["Reader:MaxConcurrent"] = "1", ["Reader:QueueLimit"] = "0" }, blocked);
        var first = app.Client().PostAsync("/v1/read?kinds=odometer", TestImages.Content(TestImages.Make()));
        await blocked.Started.Task;

        var second = await app.Client().PostAsync("/v1/read?kinds=odometer", TestImages.Content(TestImages.Make()));
        blocked.Release.SetResult();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal("busy", (string?)JsonNode.Parse(await second.Content.ReadAsStringAsync())!["code"]);
        Assert.Equal("2", second.Headers.RetryAfter?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
        var warning = Assert.Single(app.Log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("busy", warning.Message);
    }

    [Fact]
    public async Task AReadingWithoutTheOcr_Is503()
    {
        using var app = new ReaderApp(ocr: new UnavailableOcr());

        var response = await app.Client().PostAsync("/v1/read", TestImages.Content(TestImages.Make()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ocr_unavailable", (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]);
        Assert.Contains("Tesseract", Assert.Single(app.Log.Entries, e => e.Level == LogLevel.Warning).Message);
    }

    [Fact]
    public void TheReaderRefusesToStart_WithoutAnApiKey()
    {
        using var app = new ReaderApp(new() { ["Reader:ApiKey"] = "" });

        var error = Assert.Throws<OptionsValidationException>(() => app.Client());

        Assert.Contains("Reader:ApiKey is required", error.Message);
    }

    private sealed class BlockingOcr : IOcrEngine
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<OcrPage> RecognizeAsync(ReadOnlyMemory<byte> png, OcrPass pass, CancellationToken ct)
        {
            Started.TrySetResult();
            await Release.Task;
            return OcrPage.Empty;
        }
    }

    private sealed class UnavailableOcr : IOcrEngine
    {
        public Task<OcrPage> RecognizeAsync(ReadOnlyMemory<byte> png, OcrPass pass, CancellationToken ct) => throw Reading.ReaderErrors.OcrUnavailable();
    }
}
