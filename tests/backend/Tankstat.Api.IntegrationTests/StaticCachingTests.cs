using Microsoft.AspNetCore.Http;

namespace Tankstat.Api.IntegrationTests;

/// <summary>The cache rule for the built web app: hashed assets are kept for good, everything with a fixed name is asked about again.</summary>
public class StaticCachingTests
{
    [Theory]
    [InlineData("/assets/index-BxQ1n2.js", StaticCaching.Immutable)]
    [InlineData("/assets/VehiclePage-9f3a.css", StaticCaching.Immutable)]
    [InlineData("/", StaticCaching.Revalidate)]
    [InlineData("/index.html", StaticCaching.Revalidate)]
    [InlineData("/sw.js", StaticCaching.Revalidate)]
    [InlineData("/workbox-5f1b2c3d.js", StaticCaching.Revalidate)]
    [InlineData("/manifest.webmanifest", StaticCaching.Revalidate)]
    [InlineData("/favicon.svg", StaticCaching.Revalidate)]
    [InlineData("/icon-512.png", StaticCaching.Revalidate)]
    [InlineData("/vehicles/abc", StaticCaching.Revalidate)]
    [InlineData("/assetsx/evil.js", StaticCaching.Revalidate)] // a segment, not a prefix
    public void HashedAssetsAreKept_FixedNamesAreRevalidated(string path, string expected) =>
        Assert.Equal(expected, StaticCaching.HeaderFor(new PathString(path)));
}
