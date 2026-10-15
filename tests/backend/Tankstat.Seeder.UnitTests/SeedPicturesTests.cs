using System.Buffers.Binary;
using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Tankstat.Application.Images;
using Tankstat.Seeder.Pictures;

namespace Tankstat.Seeder.UnitTests;

/// <summary>Pictures for the seeded vehicles: drawn ones, photos of their models from Commons (never the network in a test), and the run.</summary>
public sealed class SeedPicturesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tankstat-seed-pictures-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void ADrawnPicture_IsAPngTheAppTakes_TheSameForTheSameSeed()
    {
        var png = DrawnPicture.Png(new Random(7));

        Assert.Equal("image/png", ImageFormat.Detect(png));
        Assert.Equal((DrawnPicture.Width, DrawnPicture.Height), (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))));
        Assert.Equal(png, DrawnPicture.Png(new Random(7)));
        Assert.NotEqual(png, DrawnPicture.Png(new Random(8)));
    }

    /// <summary>Answers the search with one JPEG file and the thumbnail with <paramref name="thumbnail"/>; counts the requests.</summary>
    private sealed class FakeCommons(byte[]? thumbnail, bool offline = false) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            if (offline) throw new HttpRequestException("No network.");
            if (request.RequestUri!.Host == "commons.wikimedia.org")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        { "query": { "pages": {
                          "2": { "index": 2, "imageinfo": [ { "mime": "image/svg+xml", "thumburl": "https://upload.wikimedia.org/b.svg.png" } ] },
                          "1": { "index": 1, "imageinfo": [ { "mime": "image/jpeg", "thumburl": "https://upload.wikimedia.org/a.jpg" } ] } } } }
                        """, Encoding.UTF8, "application/json"),
                });
            return Task.FromResult(thumbnail is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(thumbnail) });
        }
    }

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    [Fact]
    public async Task AModelsCandidates_AreTheSearchsBitmapsInItsOrder_KeptInTheCache_AndEachPhotoDownloadedOnce()
    {
        var commons = new FakeCommons(Jpeg);
        var pictures = new CommonsPictures(new HttpClient(commons), _folder);

        Assert.Equal(["https://upload.wikimedia.org/a.jpg"], await pictures.CandidatesAsync("Skoda Octavia", default)); // the JPEG; the SVG's PNG rendering is no photo
        Assert.Contains("%22Skoda%20Octavia%22", commons.Requests[0]);
        Assert.Equal(Jpeg, await pictures.PhotoAsync("https://upload.wikimedia.org/a.jpg", default));
        Assert.True(File.Exists(Path.Combine(_folder, "skoda-octavia.json")));

        // Again, with no network at all: the same list and the same photo, from the cache.
        var again = new FakeCommons(null, offline: true);
        var cached = new CommonsPictures(new HttpClient(again), _folder);
        Assert.Equal(["https://upload.wikimedia.org/a.jpg"], await cached.CandidatesAsync("Skoda Octavia", default));
        Assert.Equal(Jpeg, await cached.PhotoAsync("https://upload.wikimedia.org/a.jpg", default));
        Assert.Empty(again.Requests);
    }

    [Fact]
    public async Task NoNetwork_NoFile_OrAFileTheAppWouldRefuse_IsNoPhoto()
    {
        Assert.Empty(await new CommonsPictures(new HttpClient(new FakeCommons(Jpeg, offline: true)), _folder).CandidatesAsync("VW Golf", default));
        Assert.Null(await new CommonsPictures(new HttpClient(new FakeCommons(null)), _folder).PhotoAsync("https://upload.wikimedia.org/a.jpg", default));
        Assert.Null(await new CommonsPictures(new HttpClient(new FakeCommons("<svg/>"u8.ToArray())), _folder).PhotoAsync("https://upload.wikimedia.org/a.jpg", default));
        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder).Any()); // nothing cached for a miss
    }

    [Fact]
    public void AVehiclesRandom_IsItsSeedAndName_AndNothingElse()
    {
        int Roll(int seed, string name) => VehiclePictures.RandomFor(seed, name).Next();

        Assert.Equal(Roll(1234, "Skoda Octavia (family)"), Roll(1234, "Skoda Octavia (family)"));
        Assert.NotEqual(Roll(1234, "Skoda Octavia (family)"), Roll(1234, "Skoda Octavia (work)"));
        Assert.NotEqual(Roll(1234, "Skoda Octavia (family)"), Roll(1235, "Skoda Octavia (family)"));
        Assert.Equal(641_316_877, Roll(1234, "Skoda Octavia (family)")); // pinned: string.GetHashCode would differ between runs
    }

    [Theory]
    [InlineData("Skoda Octavia (family)", "Skoda Octavia")]
    [InlineData("Mercedes C200 (work) 3", "Mercedes C200")]
    [InlineData("Nothing to split", "Nothing to split")]
    public void TheModel_IsTheNameBeforeItsTag(string name, string model) => Assert.Equal(model, VehiclePictures.ModelOf(name));

    [Fact]
    public void TheOptions_ChooseThePictures_AndTheirCache()
    {
        Assert.Equal(SeedPictures.None, SeedOptionsParser.Parse([])!.Pictures);
        Assert.Equal(SeedPictures.Photos, SeedOptionsParser.Parse(["--pictures"])!.Pictures);
        Assert.Equal(SeedPictures.Drawn, SeedOptionsParser.Parse(["--drawn-pictures"])!.Pictures);
        Assert.Equal("/tmp/cache", SeedOptionsParser.Parse(["--pictures", "--pictures-cache", "/tmp/cache"])!.PicturesCache);
    }

    private string Db => Path.Combine(_folder, "seed.db");
    private string Uploads => Path.Combine(_folder, "uploads");

    private async Task<(int Code, string Output)> SeedDrawn(int seed)
    {
        Directory.CreateDirectory(_folder);
        var output = new StringWriter();
        var code = await SeederApp.RunAsync(
            ["--vehicles", "6", "--trashed", "2", "--refuelings", "1", "--seed", seed.ToString(), "--drawn-pictures", "--uploads", Uploads, "--yes", "--connection", $"Data Source={Db}", "--settings", Db + "-settings.json"],
            new ConfigurationBuilder().Build(), output, new StringReader(""), new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)));
        using (var pooled = new SqliteConnection($"Data Source={Db}")) SqliteConnection.ClearPool(pooled);
        return (code, output.ToString());
    }

    private long Scalar(string sql)
    {
        using var c = new SqliteConnection($"Data Source={Db}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var value = Convert.ToInt64(cmd.ExecuteScalar());
        c.Close();
        SqliteConnection.ClearPool(c);
        return value;
    }

    /// <summary>Each vehicle's picture by its name, as a hash of the file the store wrote.</summary>
    private Dictionary<string, string> PicturesByName()
    {
        using var c = new SqliteConnection($"Data Source={Db}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "select Name, Id from Vehicles";
        var byName = new Dictionary<string, string>();
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
            {
                var folder = Path.Combine(Uploads, "vehicles", Guid.Parse(reader.GetString(1)).ToString("N"), "picture");
                var file = Assert.Single(Directory.GetFiles(folder));
                byName[reader.GetString(0)] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
            }
        c.Close();
        SqliteConnection.ClearPool(c);
        return byName;
    }

    [Fact]
    public async Task ASeedWithDrawnPictures_GivesEveryVehicleOne_StoredAsTheAppStoresAnUpload()
    {
        var (code, output) = await SeedDrawn(1234);

        Assert.Equal(SeederApp.Success, code);
        Assert.Contains("pictures: 0 photos, 8 drawn", output);
        Assert.Equal(0, Scalar("select count(*) from Vehicles where PictureImageId is null")); // the trash's too
        Assert.Equal(8, Scalar("select count(*) from Images where ContentType = 'image/png' and Folder like 'vehicles/%/picture'"));
        Assert.Equal(8, Directory.EnumerateFiles(Uploads, "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task TheSameSeed_DrawsEveryVehicleTheSamePictureAgain_AnotherSeedOtherOnes()
    {
        await SeedDrawn(1234);
        var first = PicturesByName();
        await SeedDrawn(1234);
        var again = PicturesByName();
        await SeedDrawn(99);
        var other = PicturesByName();

        Assert.Equal(first, again);
        Assert.True(first.Values.Distinct().Count() > 1); // random: not one picture for all
        Assert.NotEqual(first.OrderBy(p => p.Key).Select(p => p.Value), other.OrderBy(p => p.Key).Select(p => p.Value));
    }
}
