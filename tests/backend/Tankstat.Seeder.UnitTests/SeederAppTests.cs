using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Tankstat.Seeder;

namespace Tankstat.Seeder.UnitTests;

public sealed class SeederAppTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"tankstat-seed-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_db);
    }

    private string Connection => $"Data Source={_db}";

    private async Task<(int Code, string Output)> Run(string[] args, string input = "", IDictionary<string, string?>? env = null)
    {
        var output = new StringWriter();
        var config = new ConfigurationBuilder().AddInMemoryCollection(env ?? new Dictionary<string, string?>()).Build();
        var code = await SeederApp.RunAsync([.. args, "--connection", Connection], config, output, new StringReader(input), _clock);
        return (code, output.ToString());
    }

    private long Scalar(string sql)
    {
        using var c = new SqliteConnection(Connection);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public async Task CreatesAndMigratesTheDatabase_AndSeedsTheRequestedAmounts()
    {
        var (code, output) = await Run(["--vehicles", "40", "--trashed", "6", "--refuelings", "5-15", "--yes"]);

        Assert.Equal(SeederApp.Success, code);
        Assert.Contains("40 vehicles, 6 in the trash", output);
        Assert.Equal(46, Scalar("select count(*) from Vehicles"));
        Assert.Equal(6, Scalar("select count(*) from Vehicles where DeletedAt is not null"));
        Assert.InRange(Scalar("select count(*) from Refuelings"), 46 * 5, 46 * 15);
        Assert.Equal(0, Scalar("select count(*) from Users")); // no users, ever
        Assert.Equal(Scalar("select count(*) from Refuelings"), Scalar("select count(*) from OdometerReadings"));
        Assert.Equal(Scalar("select count(*) from Refuelings"), Scalar("select count(*) from Costs"));
        Assert.Equal(0, Scalar("select count(*) from Vehicles where OwnerId <> '00000000-0000-0000-0000-000000000000'"));
        Assert.True(Scalar("select count(*) from __EFMigrationsHistory") >= 2); // really migrated
    }

    [Fact]
    public async Task DeletesWhateverWasThereBefore()
    {
        await Run(["--vehicles", "50", "--trashed", "10", "--yes"]);
        Assert.Equal(60, Scalar("select count(*) from Vehicles"));

        var (code, _) = await Run(["--vehicles", "3", "--refuelings", "2", "--yes"]);

        Assert.Equal(SeederApp.Success, code);
        Assert.Equal(3, Scalar("select count(*) from Vehicles"));
        Assert.Equal(6, Scalar("select count(*) from Refuelings"));
    }

    [Fact]
    public async Task SeedingZeroVehiclesStillRecreatesAnEmptyDatabase()
    {
        await Run(["--vehicles", "5", "--yes"]);

        await Run(["--vehicles", "0", "--yes"]);

        Assert.Equal(0, Scalar("select count(*) from Vehicles"));
    }

    [Fact]
    public async Task AsksForConfirmation_AndDoesNothingUnlessTheAnswerIsYes()
    {
        await Run(["--vehicles", "4", "--yes"]);

        var (declined, output) = await Run(["--vehicles", "9"], input: "no\n");
        var (silent, _) = await Run(["--vehicles", "9"], input: "");

        Assert.Equal((SeederApp.Refused, SeederApp.Refused), (declined, silent));
        Assert.Contains("Cancelled", output);
        Assert.Contains(_db, output); // tells which database it would delete
        Assert.Equal(4, Scalar("select count(*) from Vehicles"));

        var (confirmed, _) = await Run(["--vehicles", "9"], input: "yes\n");
        Assert.Equal(SeederApp.Success, confirmed);
        Assert.Equal(9, Scalar("select count(*) from Vehicles"));
    }

    [Theory]
    [InlineData("Standalone")]
    [InlineData("Oidc")]
    [InlineData("ProxyHeader")]
    public async Task RefusesToRun_WhenAuthenticationIsOn_AndLeavesTheDatabaseAlone(string mode)
    {
        await Run(["--vehicles", "4", "--yes"]);

        var (code, output) = await Run(["--vehicles", "9", "--yes"], env: new Dictionary<string, string?> { ["Auth:Mode"] = mode });

        Assert.Equal(SeederApp.Refused, code);
        Assert.Contains("authentication mode None only", output);
        Assert.Equal(4, Scalar("select count(*) from Vehicles"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("None")]
    [InlineData("none")]
    public async Task RunsWhenAuthenticationIsOffOrUnset(string? mode)
    {
        var env = mode is null ? null : new Dictionary<string, string?> { ["Auth:Mode"] = mode };

        Assert.Equal(SeederApp.Success, (await Run(["--vehicles", "2", "--yes"], env: env)).Code);
    }

    [Fact]
    public async Task ClearsTheUploadedPictures_WhenAFolderIsGiven()
    {
        var uploads = Path.Combine(Path.GetTempPath(), $"tankstat-seed-uploads-{Guid.NewGuid():N}");
        Directory.CreateDirectory(uploads);
        File.WriteAllText(Path.Combine(uploads, "old-picture"), "x");

        var (code, output) = await Run(["--vehicles", "2", "--uploads", uploads, "--yes"]);

        Assert.Equal(SeederApp.Success, code);
        Assert.False(Directory.Exists(uploads));
        Assert.Contains(uploads, output); // told the user before deleting
    }

    [Fact]
    public async Task LeavesPicturesAloneWhenTheUserDeclines_OrNoFolderIsKnown()
    {
        var uploads = Path.Combine(Path.GetTempPath(), $"tankstat-seed-uploads-{Guid.NewGuid():N}");
        Directory.CreateDirectory(uploads);
        try
        {
            await Run(["--vehicles", "2", "--uploads", uploads], input: "no\n");
            await Run(["--vehicles", "2", "--yes"]); // no folder given or configured: nothing is deleted

            Assert.True(Directory.Exists(uploads));
        }
        finally
        {
            Directory.Delete(uploads, recursive: true);
        }
    }

    [Fact]
    public async Task UsesTheConfiguredStoragePath_WhenNoFolderIsGiven()
    {
        var uploads = Path.Combine(Path.GetTempPath(), $"tankstat-seed-uploads-{Guid.NewGuid():N}");
        Directory.CreateDirectory(uploads);

        await Run(["--vehicles", "1", "--yes"], env: new Dictionary<string, string?> { ["Storage:Path"] = uploads });

        Assert.False(Directory.Exists(uploads));
    }

    [Fact]
    public async Task InvalidArguments_AreExplained_AndNothingHappens()
    {
        var (code, output) = await Run(["--vehicles", "abc", "--yes"]);

        Assert.Equal(SeederApp.Refused, code);
        Assert.Contains("--vehicles must be a whole number", output);
        Assert.False(File.Exists(_db));
    }

    [Fact]
    public async Task Help_PrintsUsage_WithoutTouchingTheDatabase()
    {
        var (code, output) = await Run(["--help"]);

        Assert.Equal(SeederApp.Success, code);
        Assert.Contains("--vehicles", output);
        Assert.Contains("--trashed", output);
        Assert.Contains("--refuelings", output);
        Assert.False(File.Exists(_db));
    }

    [Fact]
    public async Task PasswordsInTheConnectionStringAreNotPrinted()
    {
        var output = new StringWriter();
        var config = new ConfigurationBuilder().Build();

        await SeederApp.RunAsync(["--provider", "PostgreSql", "--connection", "Host=nowhere;Database=x;Password=hunter2", "--vehicles", "1"], config, output, new StringReader("no\n"), _clock);

        Assert.DoesNotContain("hunter2", output.ToString());
        Assert.Contains("Host=nowhere", output.ToString());
    }

    [Fact]
    public async Task SameSeedGivesTheSameDatabaseContent()
    {
        await Run(["--vehicles", "20", "--trashed", "3", "--seed", "99", "--yes"]);
        var first = Scalar("select sum(Value) + count(*) from OdometerReadings");

        await Run(["--vehicles", "20", "--trashed", "3", "--seed", "99", "--yes"]);

        Assert.Equal(first, Scalar("select sum(Value) + count(*) from OdometerReadings"));
    }

    [Fact]
    public async Task EveryRefuelingBelongsToAVehicle_AndTheLogsAreConsistentInTheDatabase()
    {
        await Run(["--vehicles", "30", "--trashed", "5", "--refuelings", "3-20", "--yes"]);

        Assert.Equal(0, Scalar("select count(*) from Refuelings r left join Vehicles v on v.Id = r.VehicleId where v.Id is null"));
        Assert.Equal(0, Scalar("select count(*) from Refuelings r join Vehicles v on v.Id = r.VehicleId where r.OwnerId <> v.OwnerId"));
        // every log owns exactly one reading and one cost of its own vehicle, dated like the log
        Assert.Equal(0, Scalar("select count(*) from Refuelings r left join OdometerReadings o on o.Id = r.OdometerReadingId where o.Id is null or o.VehicleId <> r.VehicleId or o.Date <> r.Date"));
        Assert.Equal(0, Scalar("select count(*) from Refuelings r left join Costs c on c.Id = r.CostId where c.Id is null or c.VehicleId <> r.VehicleId or c.Date <> r.Date"));
        // odometer and date both increase within a vehicle
        Assert.Equal(0, Scalar("""
            select count(*) from OdometerReadings a join OdometerReadings b on a.VehicleId = b.VehicleId
            where a.Date < b.Date and a.Value >= b.Value
            """));
    }

    [Fact]
    public async Task TheApplicationServesTheSeededData_InNoAuthMode()
    {
        await Run(["--vehicles", "30", "--trashed", "4", "--refuelings", "5-12", "--yes"]);

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = Connection,
            ["Auth:Mode"] = "None",
        })));
        var client = factory.CreateClient();
        async Task<JsonElement> Gql(string query, object? variables = null) =>
            await (await client.PostAsJsonAsync("/graphql", new { query, variables })).Content.ReadFromJsonAsync<JsonElement>();

        var counts = (await Gql("{ vehicleCount trashCount }")).GetProperty("data");
        Assert.Equal(30, counts.GetProperty("vehicleCount").GetInt32());
        Assert.Equal(4, counts.GetProperty("trashCount").GetInt32());

        var page = (await Gql("{ vehicles(orderBy: REFUELING_COUNT, direction: DESC, take: 5) { refuelingCount canEdit owner { id } } }"))
            .GetProperty("data").GetProperty("vehicles").EnumerateArray().ToList();
        Assert.Equal(5, page.Count);
        Assert.Equal(page.Select(v => v.GetProperty("refuelingCount").GetInt32()).OrderByDescending(x => x), page.Select(v => v.GetProperty("refuelingCount").GetInt32()));
        Assert.All(page, v => Assert.True(v.GetProperty("canEdit").GetBoolean()));

        Assert.Equal(4, (await Gql("mutation { emptyTrash }")).GetProperty("data").GetProperty("emptyTrash").GetInt32());
        Assert.Equal(0, Scalar("select count(*) from Vehicles where DeletedAt is not null"));
        Assert.Equal(0, Scalar("select count(*) from Refuelings r left join Vehicles v on v.Id = r.VehicleId where v.Id is null")); // cascade left no orphans
    }
}
