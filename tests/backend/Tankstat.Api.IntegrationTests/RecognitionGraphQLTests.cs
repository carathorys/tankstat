using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Recognition;
using Tankstat.Domain.Recognition;
using Tankstat.TestSupport;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// Photo reading through the real host: the draft upload queues the photo, the background worker reads it through the provider (a
/// fake one, or the real adapter against a fake model server) and GraphQL hands the dialog what was found.
/// </summary>
[Collection(ApiCollection.Name)]
public class RecognitionGraphQLTests
{
    private static byte[] Png(byte marker = 0) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker, 1, 2, 3, 4];

    private const string DraftsQuery = "query($ids: [UUID!]!) { photoDrafts(ids: $ids) { id url reading { status kind values { name value confidence } } } }";

    /// <summary>Answers like a model would, without a network.</summary>
    private sealed class FakeProvider : IRecognitionProvider
    {
        public ConcurrentQueue<(RecognitionRequest Request, byte[] Image)> Requests { get; } = new();
        public Func<RecognitionRequest, RecognitionResult> Answer { get; set; } = _ => new("fake-1", DocumentKind.FuelReceipt,
        [
            new(ReadingFieldName.Total, "24687", 0.94, ValueSource.Read),
            new(ReadingFieldName.Volume, "38.52", 0.93, ValueSource.Read),
            new(ReadingFieldName.UnitPrice, "640.9", 0.4, ValueSource.Read), // too unsure to fill in
            new(ReadingFieldName.Currency, "HUF", 0.95, ValueSource.Hint), // only the hint coming back
        ]);
        /// <summary>When set, a read waits until it is released (a photo that is still being read).</summary>
        public SemaphoreSlim? Gate { get; set; }
        public string Name => "fake";
        public bool IsConfigured => true;
        public Task<bool> IsHealthyAsync(CancellationToken ct) => Task.FromResult(true);
        public async Task<RecognitionResult> ReadAsync(RecognitionRequest request, CancellationToken ct)
        {
            Requests.Enqueue((request, request.Image.ToArray()));
            if (Gate is { } gate) await gate.WaitAsync(ct);
            return Answer(request);
        }
    }

    private static TestApp WithReading(FakeProvider provider) => new(new Dictionary<string, string?>
    {
        ["Auth:Mode"] = "Standalone",
        ["Auth:Standalone:AdminEmail"] = "root@example.com",
        ["Auth:Standalone:AdminPassword"] = "initial-password-1",
        ["Recognition:Provider"] = "OpenAiCompatible",
        ["Recognition:OpenAiCompatible:BaseUrl"] = "http://model.invalid:1234/v1",
        ["Recognition:OpenAiCompatible:Model"] = "test-vision",
    }, s => s.AddSingleton<IRecognitionProvider>(provider));

    private static async Task<(HttpClient Admin, string VehicleId)> Signed(TestApp app)
    {
        var admin = app.NewClient();
        await admin.LoginAs("root@example.com", "initial-password-1");
        var vehicle = (await admin.Gql("mutation { addVehicle(input: { name: \"Car\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        return (admin, vehicle);
    }

    private static async Task<string> UploadDraft(HttpClient c, string vehicleId, string query = "?form=refueling&locale=hu")
    {
        var content = new ByteArrayContent(Png());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await c.PutAsync($"/media/vehicles/{vehicleId}/photo-drafts{query}", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> Drafts(HttpClient c, params string[] ids) => (await c.Gql(DraftsQuery, new { ids })).Data().GetProperty("photoDrafts");

    private static async Task<JsonElement> ReadDraft(HttpClient c, string id)
    {
        for (var i = 0; i < 100; i++)
        {
            var draft = (await Drafts(c, id))[0];
            if (draft.GetProperty("reading") is { ValueKind: JsonValueKind.Object } reading && reading.GetProperty("status").GetString() is "READ" or "FAILED") return draft;
            await Task.Delay(100);
        }
        throw new TimeoutException("The photo was not read.");
    }

    [Fact]
    public async Task APhotoPickedForARefueling_IsReadOnTheServer_AndItsSureValuesComeBack()
    {
        var provider = new FakeProvider();
        using var app = WithReading(provider);
        var (admin, vehicle) = await Signed(app);
        await admin.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = vehicle, date = "2026-09-02", volume = 40, totalCost = 60, currency = "HUF", odometer = 12_345, isFullTank = true } });

        var id = await UploadDraft(admin, vehicle);
        var draft = await ReadDraft(admin, id);

        Assert.Equal($"/media/{Guid.Parse(id):N}", draft.GetProperty("url").GetString());
        var reading = draft.GetProperty("reading");
        Assert.Equal(("READ", "FUEL_RECEIPT"), (reading.GetProperty("status").GetString(), reading.GetProperty("kind").GetString()));
        Assert.Equal(["TOTAL=24687", "VOLUME=38.52"], reading.GetProperty("values").EnumerateArray().Select(v => $"{v.GetProperty("name").GetString()}={v.GetProperty("value").GetString()}"));
        var (request, image) = Assert.Single(provider.Requests);
        Assert.Equal(Png(), image);
        Assert.Equal(("image/png", "hu", (long?)12_345, "HUF"), (request.ContentType, request.Locale, request.LastOdometer, request.Currency));
        Assert.Equal([DocumentKind.Odometer, DocumentKind.FuelReceipt], request.Kinds.Order());
        Assert.True((await admin.Gql("{ recognitionStatus { available } }")).Data().GetProperty("recognitionStatus").GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task APhotoForAnExpense_MayShowAnExpenseReceipt_AndOneUploadedWithoutAForm_IsNotRead()
    {
        var provider = new FakeProvider();
        using var app = WithReading(provider);
        var (admin, vehicle) = await Signed(app);

        var expense = await UploadDraft(admin, vehicle, "?form=expense&locale=de");
        var plain = await UploadDraft(admin, vehicle, "");
        await ReadDraft(admin, expense);

        var (request, _) = Assert.Single(provider.Requests);
        Assert.Equal([DocumentKind.Odometer, DocumentKind.ExpenseReceipt], request.Kinds.Order());
        Assert.Equal("de", request.Locale);
        Assert.Equal(JsonValueKind.Null, (await Drafts(admin, plain))[0].GetProperty("reading").ValueKind);
    }

    [Fact]
    public async Task OnlyTheUploader_SeesTheirDraftsAndReadings_AndOnlySignedInUsersAsk()
    {
        using var app = WithReading(new FakeProvider());
        var (admin, vehicle) = await Signed(app);
        var id = await UploadDraft(admin, vehicle);
        var created = (await admin.Gql("mutation($i: CreateUserInput!) { createUser(input: $i) { reset { token } } }", new { i = new { email = "bob@example.com", displayName = "Bob", isAdmin = false } })).Data();
        await app.NewClient().Gql("mutation($i: ResetPasswordInput!) { resetPassword(input: $i) }",
            new { i = new { token = created.GetProperty("createUser").GetProperty("reset").GetProperty("token").GetString(), newPassword = "bob-password-1" } });
        var bob = app.NewClient();
        await bob.LoginAs("bob@example.com", "bob-password-1");

        Assert.Empty((await Drafts(bob, id)).EnumerateArray());
        Assert.Equal("UNAUTHENTICATED", (await app.NewClient().Gql("{ recognitionStatus { available } }")).ErrorCode());
        Assert.Equal("UNAUTHENTICATED", (await app.NewClient().Gql(DraftsQuery, new { ids = new[] { id } })).ErrorCode());
    }

    [Fact]
    public async Task WithoutPhotoReading_NothingIsQueued_AndTheStatusSaysSo()
    {
        using var app = TestApp.Standalone();
        var (admin, vehicle) = await Signed(app);

        var id = await UploadDraft(admin, vehicle);

        Assert.False((await admin.Gql("{ recognitionStatus { available } }")).Data().GetProperty("recognitionStatus").GetProperty("available").GetBoolean());
        var draft = Assert.Single((await Drafts(admin, id)).EnumerateArray());
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("reading").ValueKind);
    }

    private const string RefuelingQuery = "query($id: UUID!) { refueling(id: $id) { odometer volume totalCost reviewState filledFromPhoto } }";

    private static async Task<JsonElement> WaitForReview(HttpClient c, string id)
    {
        for (var i = 0; i < 100; i++)
        {
            var log = (await c.Gql(RefuelingQuery, new { id })).Data().GetProperty("refueling");
            if (log.GetProperty("reviewState").GetString() != "AWAITING_PHOTOS") return log;
            await Task.Delay(100);
        }
        throw new TimeoutException("The log was not filled in.");
    }

    [Fact]
    public async Task ARefuelingSavedWhileItsPhotoIsRead_IsFilledInLater_AndWaitsForAReview()
    {
        var provider = new FakeProvider
        {
            Gate = new SemaphoreSlim(0),
            Answer = _ => new("fake-1", DocumentKind.Odometer, [new(ReadingFieldName.Odometer, "315193", 0.72, ValueSource.Read)]),
        };
        using var app = WithReading(provider);
        var (admin, vehicle) = await Signed(app);
        var photo = await UploadDraft(admin, vehicle);

        var saved = (await admin.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id odometer reviewState } }",
            new { i = new { vehicleId = vehicle, date = "2026-09-02", volume = 40, totalCost = 60, currency = "HUF", isFullTank = true, photoIds = new[] { photo } } }))
            .Data().GetProperty("logRefueling");
        Assert.Equal((JsonValueKind.Null, "AWAITING_PHOTOS"), (saved.GetProperty("odometer").ValueKind, saved.GetProperty("reviewState").GetString()));

        provider.Gate.Release();
        var id = saved.GetProperty("id").GetString()!;
        var log = await WaitForReview(admin, id);

        Assert.Equal((315193L, "NEEDS_REVIEW"), (log.GetProperty("odometer").GetInt64(), log.GetProperty("reviewState").GetString()));
        Assert.Equal(["ODOMETER"], log.GetProperty("filledFromPhoto").EnumerateArray().Select(v => v.GetString()));
        var told = (await admin.Gql("{ notifications(take: 10) { kind subject { type id } args { name value } } }")).Data().GetProperty("notifications")[0];
        Assert.Equal(("LOG_FILLED_FROM_PHOTO", "REFUELING"), (told.GetProperty("kind").GetString(), told.GetProperty("subject").GetProperty("type").GetString()));

        var checkedLog = (await admin.Gql("mutation($i: UpdateRefuelingInput!) { updateRefueling(input: $i) { reviewState filledFromPhoto } }",
            new { i = new { id, date = "2026-09-02", volume = 40, totalCost = 60, odometer = 315193, isFullTank = true } })).Data().GetProperty("updateRefueling");
        Assert.Equal("NONE", checkedLog.GetProperty("reviewState").GetString());
        Assert.Empty(checkedLog.GetProperty("filledFromPhoto").EnumerateArray());
    }

    [Fact]
    public async Task WithoutAPhotoBeingRead_ALogNeedsEveryValue()
    {
        using var app = WithReading(new FakeProvider());
        var (admin, vehicle) = await Signed(app);

        var body = await admin.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = vehicle, date = "2026-09-02", volume = 40, currency = "HUF", odometer = 1000, isFullTank = true } });

        Assert.Equal("VALIDATION_FAILED", body.ErrorCode());
        Assert.Contains("log.valuesRequired", body.ToString());
    }

    // ---- a model behind an OpenAI-compatible API, through the real adapter ---------------------------------------

    /// <summary>Stands in for an OpenAI-compatible server behind the host's own HTTP client, so the real adapter runs from end to end.</summary>
    private sealed class FakeModelServer : HttpMessageHandler
    {
        private const string Answer = """
            { "model": "test-vision", "choices": [ { "message": { "role": "assistant", "content": "{\"kind\":\"fuel-receipt\",\"fields\":[{\"name\":\"total\",\"value\":\"24687\",\"confidence\":0.95},{\"name\":\"volume\",\"value\":\"38.52\",\"confidence\":0.9},{\"name\":\"unitPrice\",\"value\":\"640.9\",\"confidence\":0.9},{\"name\":\"currency\",\"value\":\"HUF\",\"confidence\":0.9},{\"name\":\"date\",\"value\":\"2026-09-17\",\"confidence\":0.4}]}" }, "finish_reason": "stop" } ] }
            """;

        public ConcurrentQueue<(HttpRequestMessage Request, string Body)> Requests { get; } = new();
        public HttpStatusCode ChatStatus { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Enqueue((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            var models = request.RequestUri!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal);
            var body = models ? """{ "object": "list", "data": [ { "id": "test-vision" }, { "id": "other" } ] }""" : Answer;
            return new HttpResponseMessage(models ? HttpStatusCode.OK : ChatStatus) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static TestApp WithModel(FakeModelServer server, string key = "secret-key") => new(new Dictionary<string, string?>
    {
        ["Auth:Mode"] = "Standalone",
        ["Auth:Standalone:AdminEmail"] = "root@example.com",
        ["Auth:Standalone:AdminPassword"] = "initial-password-1",
        ["Recognition:Provider"] = "OpenAiCompatible",
        ["Recognition:OpenAiCompatible:BaseUrl"] = "http://model.invalid:1234/v1",
        ["Recognition:OpenAiCompatible:ApiKey"] = key,
        ["Recognition:OpenAiCompatible:Model"] = "test-vision",
        ["Recognition:OpenAiCompatible:SystemPrompt"] = "Custom prompt for the test.",
    }, s => s.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => server))); // the adapter's client name is internal: every client goes to the fake

    private static async Task<LogEntry> WorkerSaid(TestApp app, string outcome)
    {
        for (var i = 0; i < 100; i++)
        {
            var line = app.Log.Entries.FirstOrDefault(e => e.Category.EndsWith("RecognitionWorker", StringComparison.Ordinal) && e.Values.GetValueOrDefault("Outcome")?.ToString() == outcome);
            if (line is not null) return line;
            await Task.Delay(100);
        }
        throw new TimeoutException($"The worker never said a reading was {outcome}.");
    }

    /// <summary>A line of one of the app's classes (by the end of its category: the adapter is internal) that has the placeholder; the worker writes after the read is visible, so this waits for it.</summary>
    private static async Task<LogEntry> Logged(TestApp app, string category, string placeholder)
    {
        for (var i = 0; i < 100; i++)
        {
            var line = app.Log.Entries.FirstOrDefault(e => e.Category.EndsWith(category, StringComparison.Ordinal) && e.Values.ContainsKey(placeholder));
            if (line is not null) return line;
            await Task.Delay(100);
        }
        throw new TimeoutException($"{category} never logged a line with {placeholder}.");
    }

    [Fact]
    public async Task AModelBehindAnOpenAiCompatibleApi_ReadsThePhoto_ThroughTheRealAdapter_AndNothingSecretReachesTheLog()
    {
        var server = new FakeModelServer();
        using var app = WithModel(server);
        var (admin, vehicle) = await Signed(app);

        var id = await UploadDraft(admin, vehicle);
        var draft = await ReadDraft(admin, id);

        var reading = draft.GetProperty("reading");
        Assert.Equal(("READ", "FUEL_RECEIPT"), (reading.GetProperty("status").GetString(), reading.GetProperty("kind").GetString()));
        Assert.Equal(["TOTAL=24687", "VOLUME=38.52", "UNIT_PRICE=640.9", "CURRENCY=HUF"], // the date was too unsure to be offered
            reading.GetProperty("values").EnumerateArray().Select(v => $"{v.GetProperty("name").GetString()}={v.GetProperty("value").GetString()}"));
        var (request, body) = Assert.Single(server.Requests, r => r.Request.Method == HttpMethod.Post);
        Assert.Equal(("http://model.invalid:1234/v1/chat/completions", "Bearer secret-key"), (request.RequestUri!.ToString(), request.Headers.Authorization!.ToString()));
        Assert.Contains("Custom prompt for the test.", body);
        Assert.Contains("data:image/png;base64," + Convert.ToBase64String(Png()), body);
        Assert.Contains(server.Requests, r => r.Request.Method == HttpMethod.Get && r.Request.RequestUri!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal));
        foreach (var secret in new[] { "secret-key", "Custom prompt", "24687", "38.52", "640.9" }) Assert.False(app.Log.Mentions(secret), secret);

        // What an operator can follow instead: which model and prompt are in use, the photo's question and answer, and what came of the read.
        var photo = Guid.Parse(id);
        var started = await Logged(app, "RecognitionWorker", "Server");
        Assert.Equal<object?[]>(["test-vision", "http://model.invalid:1234/v1"], [started.Values["Model"], started.Values["Server"]]);
        var prompt = await Logged(app, "RecognitionWorker", "PromptSource");
        Assert.Equal<object?[]>(["from Recognition:OpenAiCompatible:SystemPrompt", "Custom prompt for the test.".Length], [prompt.Values["PromptSource"], prompt.Values["Characters"]]);
        var asked = await Logged(app, "OpenAiCompatibleRecognitionProvider", "Kilobytes");
        var answered = await Logged(app, "OpenAiCompatibleRecognitionProvider", "Issues");
        Assert.Equal<object?[]>([photo, photo], [asked.Values["PhotoId"], answered.Values["PhotoId"]]);
        Assert.Equal("Total@0.95, Volume@0.90, UnitPrice@0.90, Currency@0.90, Date@0.40", answered.Values["Kept"]);
        var summary = await Logged(app, "RecognitionWorker", "Sure");
        Assert.Equal<object?[]>([photo, DocumentKind.FuelReceipt, 5, 4], [summary.Values["Id"], summary.Values["Kind"], summary.Values["Values"], summary.Values["Sure"]]); // the date was too unsure
    }

    [Fact]
    public async Task AKeyTheModelServerRefuses_IsSaidInTheLog_ByItsSettingsName_NeverByItsValue()
    {
        var server = new FakeModelServer { ChatStatus = HttpStatusCode.Unauthorized };
        using var app = WithModel(server, key: "wrong-secret-key");
        var (admin, vehicle) = await Signed(app);

        await UploadDraft(admin, vehicle);
        var line = await WorkerSaid(app, "Retrying");

        Assert.Contains("Recognition:OpenAiCompatible:ApiKey", line.Message);
        Assert.False(app.Log.Mentions("wrong-secret-key"));
    }
}
