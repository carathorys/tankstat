using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.UnitTests;
using Tankstat.Reader.Ocr;
using Tankstat.TestSupport;

namespace Tankstat.Reader.IntegrationTests;

/// <summary>The reader host with in-memory settings (they outrank appsettings.json and the environment) and, by default, a fake OCR.</summary>
internal sealed class ReaderApp : IDisposable
{
    public const string Key = "test-key";

    /// <param name="realOcr">Use the installed Tesseract instead of the fake (tests marked <c>[TesseractFact]</c>).</param>
    public ReaderApp(Dictionary<string, string?>? settings = null, IOcrEngine? ocr = null, bool realOcr = false)
    {
        var all = new Dictionary<string, string?> { ["Reader:ApiKey"] = Key };
        foreach (var (key, value) in settings ?? []) all[key] = value;
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(all));
            b.ConfigureLogging(l => l.AddProvider(Log));
            b.ConfigureServices(s =>
            {
                if (realOcr) return;
                s.AddSingleton(ocr ?? Ocr);
                s.AddSingleton<IOcrStatus>(Status);
            });
        });
    }

    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>Everything the reader logged.</summary>
    public CapturedLog Log { get; } = new();
    public FakeOcr Ocr { get; } = new();
    public FakeStatus Status { get; } = new();

    public HttpClient Client(string? key = Key)
    {
        var client = Factory.CreateClient();
        if (key is not null) client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    public void Dispose() => Factory.Dispose();
}

internal sealed class FakeStatus : IOcrStatus
{
    public TesseractInfo Current { get; set; } = new(true, "5.3.4", ["hun", "eng", "deu"], "hun+eng+deu", "eng", null);
    public TesseractInfo Info() => Current;
}
