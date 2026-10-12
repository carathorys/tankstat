using System.Net;
using Tankstat.Infrastructure.Settings;

namespace Tankstat.Infrastructure.UnitTests;

public class OidcDiscoveryTests
{
    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("https://id.example.com", "https://id.example.com/.well-known/openid-configuration")]
    [InlineData("https://id.example.com/", "https://id.example.com/.well-known/openid-configuration")]
    [InlineData("https://id.example.com/realms/x/", "https://id.example.com/realms/x/.well-known/openid-configuration")]
    [InlineData("http://localhost:8081/realms/x", "http://localhost:8081/realms/x/.well-known/openid-configuration")] // http, as the handler allows it
    public async Task AsksTheAuthorityForItsDiscoveryDocument_AndAnswersWithTheIssuer(string authority, string expected)
    {
        var stub = new Stub(_ => Json("""{ "issuer": "https://id.example.com/realms/x", "authorization_endpoint": "https://id.example.com/auth" }"""));

        var result = await new OidcDiscovery(new HttpClient(stub)).ProbeAsync(authority, default);

        Assert.Equal((true, "https://id.example.com/realms/x", null), (result.Reachable, result.Issuer, result.Reason));
        Assert.Equal(new Uri(expected), Assert.Single(stub.Requests));
    }

    [Theory]
    [InlineData("", "not_a_url")]
    [InlineData("id.example.com", "not_a_url")]
    [InlineData("ftp://id.example.com", "not_a_url")]
    public async Task NotAnAddress_IsSaidWithoutAsking(string authority, string reason)
    {
        var stub = new Stub(_ => Json("{}"));

        var result = await new OidcDiscovery(new HttpClient(stub)).ProbeAsync(authority, default);

        Assert.Equal((false, reason), (result.Reachable, result.Reason));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task WhatIsNotADiscoveryDocument_IsSaidByReason()
    {
        async Task<string?> Reason(Func<HttpRequestMessage, HttpResponseMessage> answer) =>
            (await new OidcDiscovery(new HttpClient(new Stub(answer))).ProbeAsync("https://id.example.com", default)).Reason;

        Assert.Equal("redirected", await Reason(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://elsewhere.example.com/") } }));
        Assert.Equal("status_404", await Reason(_ => Json("not found", HttpStatusCode.NotFound)));
        Assert.Equal("status_500", await Reason(_ => Json("{}", HttpStatusCode.InternalServerError)));
        Assert.Equal("not_json", await Reason(_ => Json("<html>sign in</html>")));
        Assert.Equal("not_json", await Reason(_ => Json("[1, 2]")));
        Assert.Equal("no_issuer", await Reason(_ => Json("""{ "authorization_endpoint": "https://id.example.com/auth" }""")));
        Assert.Equal("no_issuer", await Reason(_ => Json("""{ "issuer": "" }""")));
        Assert.Equal("too_large", await Reason(_ => Json("{ \"issuer\": \"" + new string('x', OidcDiscovery.MaxBytes + 1) + "\" }")));
        Assert.Equal("unreachable", await Reason(_ => throw new HttpRequestException("no route to host")));
    }

    [Fact]
    public async Task AServerThatDoesNotAnswerInTime_IsATimeout()
    {
        var slow = new Slow();
        var client = new HttpClient(slow) { Timeout = TimeSpan.FromMilliseconds(100) };

        var result = await new OidcDiscovery(client).ProbeAsync("https://id.example.com", default);

        Assert.Equal((false, "timeout"), (result.Reachable, result.Reason));
    }

    private sealed class Slow : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task TheAppsClient_AsksAsTheHandlerDoes_WithinTheTimeout()
    {
        await using var db = new TestDatabase();

        var client = db.Get<IHttpClientFactory>().CreateClient(nameof(Tankstat.Application.SystemSettings.IOidcDiscovery)); // the typed client's name

        Assert.Equal((OidcDiscovery.UserAgent, OidcDiscovery.Timeout), (client.DefaultRequestHeaders.UserAgent.ToString(), client.Timeout));
    }

    [Fact]
    public async Task ABodyThatStallsAfterTheHeaders_IsATimeoutToo()
    {
        var stub = new Stub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Body(stall: true)) });
        var client = new HttpClient(stub) { Timeout = TimeSpan.FromMilliseconds(200) }; // ends with the headers, which come at once
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(20)); // the request's own limit, far later

        var result = await new OidcDiscovery(client).ProbeAsync("https://id.example.com", request.Token);

        Assert.Equal((false, "timeout"), (result.Reachable, result.Reason));
    }

    [Fact]
    public async Task ABodyCutShort_IsUnreachable()
    {
        var stub = new Stub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Body(stall: false)) });

        var result = await new OidcDiscovery(new HttpClient(stub)).ProbeAsync("https://id.example.com", default);

        Assert.Equal((false, "unreachable"), (result.Reachable, result.Reason));
    }

    [Fact]
    public async Task ADocumentWithAByteOrderMark_IsRead()
    {
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes("""{ "issuer": "https://id.example.com" }""")).ToArray();
        var stub = new Stub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

        var result = await new OidcDiscovery(new HttpClient(stub)).ProbeAsync("https://id.example.com", default);

        Assert.Equal((true, "https://id.example.com"), (result.Reachable, result.Issuer));
    }

    /// <summary>A response body that never comes (<paramref name="stall"/>) or breaks off, as a connection that drops after the headers does.</summary>
    private sealed class Body(bool stall) : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!stall) throw new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely.");
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
