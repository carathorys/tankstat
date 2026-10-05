using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Recognition;
using Tankstat.Domain.Recognition;
using Tankstat.Infrastructure.Recognition;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>The opt-in dump of what goes to the model server and comes back: complete enough to debug a model with, and never the photo or the key.</summary>
public class OpenAiTrafficHandlerTests
{
    private const string Key = "hunter2-key";

    private sealed class Inner(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add(request);
            return respond(request);
        }
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (HttpClient Client, CapturedLog Log, Inner Inner) Setup(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond, bool on = true)
    {
        var log = new CapturedLog();
        var inner = new Inner(respond);
        var options = new OpenAiCompatibleRecognitionOptions { LogTraffic = on, ApiKey = Key };
        return (new HttpClient(new OpenAiTrafficHandler(options, log.For<OpenAiTrafficHandler>()) { InnerHandler = inner }), log, inner);
    }

    private static HttpRequestMessage Chat(string url = "http://model:1234/v1/chat/completions", string body = "{}", Guid? photo = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Key);
        request.Options.Set(OpenAiTrafficHandler.Photo, photo);
        return request;
    }

    private static string Value(LogEntry entry, string name) => entry.Values[name]?.ToString() ?? "";

    [Fact]
    public async Task Off_EveryCallPassesThroughUntouched_AndNothingIsLogged()
    {
        var (client, log, inner) = Setup(_ => Task.FromResult(Json(HttpStatusCode.OK, """{ "ok": true }""")), on: false);
        var request = Chat();

        using var response = await client.SendAsync(request);

        Assert.Same(request, Assert.Single(inner.Seen));
        Assert.Equal("""{ "ok": true }""", await response.Content.ReadAsStringAsync());
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task On_TheRequestAndItsAnswer_AreWrittenPrettyPrinted_ThePhotoOnlyAsItsSize_TheKeyMasked()
    {
        var photo = Guid.NewGuid();
        var (client, log, _) = Setup(_ => Task.FromResult(Json(HttpStatusCode.OK, """{ "choices": [ { "message": { "content": "{\"kind\":\"unknown\"}" } } ] }""")));
        var body = $$"""{ "model": "qwen", "messages": [ { "role": "system", "content": "You read photos." }, { "role": "user", "content": [ { "type": "image_url", "image_url": { "url": "data:image/jpeg;base64,{{new string('A', 4000)}}" } }, { "type": "text", "text": "Árvíztűrő" } ] } ] }""";

        using var response = await client.SendAsync(Chat("http://user:secret@model:1234/v1/chat/completions", body, photo));

        var (request, answer) = (log.Entries[0], log.Entries[1]);
        Assert.Equal([LogLevel.Information, LogLevel.Information], log.Entries.Select(e => e.Level));
        Assert.Equal<object?[]>([photo.ToString().Insert(0, "photo "), "POST", "http://model:1234/v1/chat/completions"], [request.Values["Purpose"], request.Values["Method"], request.Values["Url"]]); // no user info
        Assert.Equal(request.Values["Seq"], answer.Values["Seq"]); // the pair
        Assert.Contains("Authorization: Bearer ***", Value(request, "Headers"));
        var text = Value(request, "Body");
        Assert.Contains("\"url\": \"data:image/jpeg;base64,[3 KB]\"", text); // indented, and the photo is its size
        Assert.Contains("\"content\": \"You read photos.\"", text);
        Assert.Contains("Árvíztűrő", text); // not escaped into Á…
        Assert.DoesNotContain("AAAA", text);
        Assert.True(text.Split('\n').Length > 10);
        Assert.Equal<object?[]>([200, "photo " + photo], [answer.Values["Status"], answer.Values["Purpose"]]);
        Assert.Contains("\"kind\\\":\\\"unknown", Value(answer, "Body"));
        Assert.True(Convert.ToInt64(answer.Values["ElapsedMs"]) >= 0);
        Assert.Contains("Content-Type: application/json", Value(answer, "Headers"));
        Assert.Contains("\"kind", await response.Content.ReadAsStringAsync()); // the caller can still read what the handler read
        Assert.False(log.Mentions(Key));
    }

    [Fact]
    public async Task AHealthCheck_IsSaidToBeOne_AndHasNoBody()
    {
        var (client, log, _) = Setup(_ => Task.FromResult(Json(HttpStatusCode.OK, """{ "data": [] }""")));

        using var response = await client.GetAsync("http://model:1234/v1/models");

        Assert.Equal<object?[]>(["health check", "GET", "(no body)"], [log.Entries[0].Values["Purpose"], log.Entries[0].Values["Method"], log.Entries[0].Values["Body"]]);
    }

    [Fact]
    public async Task TheKey_IsMaskedWhereverAServerEchoesIt_AndSoAreCookies()
    {
        var (client, log, _) = Setup(_ =>
        {
            var response = Json(HttpStatusCode.Unauthorized, $$"""{ "error": { "message": "Incorrect API key provided: {{Key}}." } }""");
            response.Headers.Add("X-Echo", $"key {Key}");
            response.Headers.Add("Set-Cookie", "session=abc123");
            return Task.FromResult(response);
        });
        var (page, pageLog, _) = Setup(_ => Task.FromResult(Json(HttpStatusCode.BadGateway, $"<html>sent {Key}</html>")));

        using var one = await client.SendAsync(Chat());
        using var two = await page.SendAsync(Chat());

        Assert.False(log.Mentions(Key));
        Assert.False(pageLog.Mentions(Key));
        Assert.False(log.Mentions("abc123"));
        Assert.Contains("Incorrect API key provided: ***.", Value(log.Entries[1], "Body"));
        Assert.Contains("X-Echo: key ***", Value(log.Entries[1], "Headers"));
        Assert.Contains("Set-Cookie: ***", Value(log.Entries[1], "Headers"));
        Assert.Contains("<html>sent ***</html>", Value(pageLog.Entries[1], "Body"));
    }

    [Fact]
    public async Task WhatAServerWrites_CannotAddALineToTheLog_OnlyTheLayoutBreaksLines()
    {
        const string forged = "FAKE LOG LINE: signed in as admin";
        var (page, pageLog, _) = Setup(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent($"oops\r\n{forged}\u2028and\u0085more\tdone", Encoding.UTF8, "text/plain") }));
        var (json, jsonLog, _) = Setup(_ => Task.FromResult(Json(HttpStatusCode.OK, $$"""{ "text": "first\n{{forged}}\u2028second\u0085third", "{{forged}}": 1 }""")));

        using var one = await page.SendAsync(Chat());
        using var two = await json.SendAsync(Chat());

        var flat = Value(pageLog.Entries[1], "Body");
        Assert.Equal("oops\\r\\nFAKE LOG LINE: signed in as admin\\u2028and\\u0085more\\tdone", flat); // one line, every break written out
        var layout = Value(jsonLog.Entries[1], "Body");
        foreach (var raw in new[] { '\u2028', '\u2029', '\u0085', '\t' }) Assert.DoesNotContain(raw, layout);
        Assert.DoesNotContain(layout.Split('\n'), line => line.TrimStart().StartsWith("FAKE", StringComparison.Ordinal)); // the layout's own breaks, never the server's
    }

    [Fact]
    public async Task ALongBody_IsCut_WithoutSplittingACharacterAndSayingHowMuchWasLeftOut()
    {
        var (long_, longLog, _) = Setup(_ => Task.FromResult(Json(HttpStatusCode.OK, $$"""{ "text": "{{new string('x', 20_000)}}" }""")));
        var (emoji, emojiLog, _) = Setup(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('a', OpenAiTrafficHandler.MaxBodyCharacters - 1) + "😀😀", Encoding.UTF8, "text/plain") }));

        using var one = await long_.SendAsync(Chat());
        using var two = await emoji.SendAsync(Chat());

        var cut = Value(longLog.Entries[1], "Body");
        Assert.True(cut.Length < OpenAiTrafficHandler.MaxBodyCharacters + 100);
        Assert.EndsWith("more characters]", cut);
        var kept = Value(emojiLog.Entries[1], "Body");
        Assert.Equal(OpenAiTrafficHandler.MaxBodyCharacters - 1, kept.IndexOf('\n')); // the pair that straddles the cut is left out whole
        Assert.EndsWith("… [4 more characters]", kept);
        Assert.DoesNotContain(kept, c => char.IsSurrogate(c));
    }

    [Fact]
    public async Task AFailedCall_IsWrittenWithItsTime_AndStillThrown()
    {
        var (client, log, _) = Setup(_ => throw new HttpRequestException($"Connection refused (model:1234) {Key}"));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(Chat()));

        var (request, failure) = (log.Entries[0], log.Entries[1]);
        Assert.Equal(request.Values["Seq"], failure.Values["Seq"]);
        Assert.Contains("HttpRequestException: Connection refused (model:1234) ***", Value(failure, "Error"));
        Assert.True(Convert.ToInt64(failure.Values["ElapsedMs"]) >= 0);
        Assert.False(log.Mentions(Key));
    }

    [Fact]
    public async Task EveryExchange_HasItsOwnNumber()
    {
        var (client, log, _) = Setup(_ => Task.FromResult(Json(HttpStatusCode.OK, "{}")));

        using var one = await client.SendAsync(Chat());
        using var two = await client.SendAsync(Chat());

        var numbers = log.Entries.Select(e => Convert.ToInt64(e.Values["Seq"])).ToList();
        Assert.Equal(numbers[0], numbers[1]);
        Assert.Equal(numbers[2], numbers[3]);
        Assert.NotEqual(numbers[0], numbers[2]);
    }

    [Fact]
    public async Task TheProvider_StillReadsTheAnswer_AfterTheHandlerWroteIt_AndTellsItWhichPhoto()
    {
        var log = new CapturedLog();
        var inner = new Inner(_ => Task.FromResult(Json(HttpStatusCode.OK, """{ "model": "m", "choices": [ { "message": { "content": "{\"kind\":\"odometer\",\"fields\":[{\"name\":\"odometer\",\"value\":\"123789\",\"confidence\":0.9}]}" }, "finish_reason": "stop" } ] }""")));
        var options = new OpenAiCompatibleRecognitionOptions { BaseUrl = "http://model:1234/v1", Model = "qwen", ApiKey = Key, LogTraffic = true };
        var client = new HttpClient(new OpenAiTrafficHandler(options, log.For<OpenAiTrafficHandler>()) { InnerHandler = inner });
        var provider = new OpenAiCompatibleRecognitionProvider(new Factory(client), options, null, log.For<OpenAiCompatibleRecognitionProvider>());
        var photo = Guid.NewGuid();

        var result = await provider.ReadAsync(
            new RecognitionRequest(new byte[] { 1, 2, 3 }, "image/jpeg", new HashSet<DocumentKind> { DocumentKind.Odometer, DocumentKind.FuelReceipt }, "hu", 100_000, "HUF", new DateOnly(2026, 10, 1), photo), default);

        Assert.Equal("Odometer=123789", string.Join(' ', result.Values.Select(v => $"{v.Name}={v.Value}")));
        var traffic = log.From<OpenAiTrafficHandler>().ToList();
        Assert.Equal(["photo " + photo, "photo " + photo], traffic.Select(t => Value(t, "Purpose")));
        Assert.Contains("You read photos for a vehicle fuel log", Value(traffic[0], "Body")); // the prompt, which no other line has
        Assert.Contains("data:image/jpeg;base64,[1 KB]", Value(traffic[0], "Body"));
        Assert.Contains("123789", Value(traffic[1], "Body")); // and what the model read
        Assert.False(log.Mentions(Key));
    }
}
