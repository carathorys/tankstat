using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tankstat.Application.Images;
using Tankstat.Domain;

namespace Tankstat.Seeder.Pictures;

/// <summary>
/// Photos of car models from Wikimedia Commons, for test data only. A model's candidates are the JPEG and PNG files its search finds, as
/// thumbnails of <see cref="ThumbWidth"/> pixels, in the search's order; both the list and every photo downloaded are kept in
/// <paramref name="cacheFolder"/>, so the same seed picks the same photos again and a later seed needs no network. Never committed anywhere:
/// Commons files have licences of their own (mostly CC BY-SA, which asks for attribution when shared). Nothing usable, or no network, is an
/// empty list or null; the caller then draws one.
/// </summary>
public sealed class CommonsPictures(HttpClient http, string cacheFolder)
{
    public const int ThumbWidth = 1280;

    /// <summary>How many of a model's search results are candidates.</summary>
    public const int Candidates = 20;

    /// <summary>Wikimedia asks every client for a User-Agent that says who it is.</summary>
    public const string UserAgent = "Tankstat-Seeder/1.0 (test data; https://github.com/carathorys/tankstat)";

    private const string Api = "https://commons.wikimedia.org/w/api.php";

    /// <summary>The model's candidates (thumbnail addresses), from the cache when it holds them, else from a search, which is then kept.</summary>
    public async Task<IReadOnlyList<string>> CandidatesAsync(string model, CancellationToken ct)
    {
        var cached = Path.Combine(cacheFolder, Slug(model) + ".json");
        if (File.Exists(cached)) return JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(cached, ct)) ?? [];
        try
        {
            var search = Uri.EscapeDataString($"\"{model}\" car filetype:bitmap");
            var url = $"{Api}?action=query&format=json&generator=search&gsrnamespace=6&gsrlimit={Candidates}&gsrsearch={search}&prop=imageinfo&iiprop=url|mime&iiurlwidth={ThumbWidth}";
            using var answer = JsonDocument.Parse(await http.GetStringAsync(url, ct));
            if (!answer.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return [];
            string[] candidates =
            [
                .. pages.EnumerateObject()
                    .Select(p => p.Value)
                    .OrderBy(p => p.TryGetProperty("index", out var index) ? index.GetInt32() : int.MaxValue) // the search's own order
                    .SelectMany(p => p.TryGetProperty("imageinfo", out var info) ? info.EnumerateArray() : Enumerable.Empty<JsonElement>())
                    .Where(i => i.TryGetProperty("mime", out var mime) && mime.GetString() is "image/jpeg" or "image/png")
                    .Select(i => i.TryGetProperty("thumburl", out var thumb) ? thumb.GetString() : null)
                    .OfType<string>()
                    .Where(u => u.Length > 0),
            ];
            if (candidates.Length > 0)
            {
                Directory.CreateDirectory(cacheFolder);
                await File.WriteAllTextAsync(cached, JsonSerializer.Serialize(candidates), ct);
            }
            return candidates;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            return []; // offline, a timeout, or an answer that is not what the API gives: the vehicles get drawn ones
        }
    }

    /// <summary>One candidate's photo, from the cache or downloaded and kept; null when it cannot be had or the app would refuse it.</summary>
    public async Task<byte[]?> PhotoAsync(string url, CancellationToken ct)
    {
        var cached = Path.Combine(cacheFolder, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..24] + ".img");
        if (File.Exists(cached)) return await File.ReadAllBytesAsync(cached, ct);
        try
        {
            var bytes = await http.GetByteArrayAsync(url, ct);
            if (!Usable(bytes)) return null;
            Directory.CreateDirectory(cacheFolder);
            await File.WriteAllBytesAsync(cached, bytes, ct);
            return bytes;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Whether the app would take the file as a picture (a JPEG, PNG or WebP of at most 2 MB).</summary>
    private static bool Usable(byte[] bytes)
    {
        try
        {
            ImageFormat.Detect(bytes);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }

    /// <summary>A model's name as a file name: letters and digits, the rest as dashes.</summary>
    public static string Slug(string model)
    {
        var slug = new StringBuilder();
        foreach (var c in model.ToLowerInvariant()) slug.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        return slug.ToString().Trim('-');
    }
}
