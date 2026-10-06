using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Tankstat.Api.IntegrationTests;

/// <summary>Kestrel serves the built web app from wwwroot: the cache rule and the manifest's content type, on a web root made for the test.</summary>
[Collection(ApiCollection.Name)]
public class StaticFilesTests : IDisposable
{
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-wwwroot");
    private readonly TestApp _app;

    public StaticFilesTests()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>Tankstat</title>");
        File.WriteAllText(Path.Combine(_webRoot, "manifest.webmanifest"), """{"name":"Tankstat","short_name":"Tankstat"}""");
        File.WriteAllText(Path.Combine(_webRoot, "assets", "app-abc123.js"), "console.log('built')");
        _app = new TestApp(new() { ["Auth:Mode"] = "None" }, host: b => b.UseWebRoot(_webRoot));
    }

    public void Dispose()
    {
        _app.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }

    [Theory]
    [InlineData("/assets/app-abc123.js", StaticCaching.Immutable)]
    [InlineData("/", StaticCaching.Revalidate)]
    [InlineData("/index.html", StaticCaching.Revalidate)]
    [InlineData("/manifest.webmanifest", StaticCaching.Revalidate)]
    [InlineData("/vehicles/abc", StaticCaching.Revalidate)] // the SPA fallback answers with index.html
    public async Task StaticFiles_CarryTheCacheRule(string path, string cacheControl)
    {
        var response = await _app.NewClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(cacheControl, response.Headers.GetValues("Cache-Control").Single());
    }

    [Fact]
    public async Task Manifest_IsServedAsAManifest()
    {
        var response = await _app.NewClient().GetAsync("/manifest.webmanifest");

        Assert.Equal("application/manifest+json", response.Content.Headers.ContentType!.MediaType);
    }
}
