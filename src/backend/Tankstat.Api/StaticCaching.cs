namespace Tankstat.Api;

/// <summary>
/// Cache-Control for the files of the built web app, by request path. Pure, so the rule has a unit test of its own; <c>Program.cs</c>
/// applies it to the static files and to the SPA fallback (StaticFilesTests serves a web root through the test host to check that wiring).
/// </summary>
internal static class StaticCaching
{
    /// <summary>Vite puts a content hash in every file name under /assets: a changed file is a new name, so the old one may be kept for a year.</summary>
    public const string Immutable = "public, max-age=31536000, immutable";

    /// <summary>
    /// Everything with a fixed name (index.html, the service worker and its runtime, the manifest, the icons): keep it, but ask the server
    /// (ETag) before using it, so a release reaches every browser on its next start.
    /// </summary>
    public const string Revalidate = "no-cache";

    public static string HeaderFor(PathString path) => path.StartsWithSegments("/assets") ? Immutable : Revalidate;
}
