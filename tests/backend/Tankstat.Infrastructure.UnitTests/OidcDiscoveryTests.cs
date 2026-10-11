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
}
