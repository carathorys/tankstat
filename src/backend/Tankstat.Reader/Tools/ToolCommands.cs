using System.Globalization;
using Microsoft.Extensions.Logging;
using Tankstat.Reader.Core;
using Tankstat.Reader.Core.Extraction;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.Text;
using Tankstat.Reader.Imaging;
using Tankstat.Reader.Synthetic;

namespace Tankstat.Reader.Tools;

/// <summary>
/// The reader's command line tools (they never start the web server):
/// <list type="bullet">
/// <item><c>--eval &lt;folder&gt;</c> or <c>--eval --synthetic &lt;n&gt;</c>: read photos with known values and print how well each field was read
/// (<c>--edge &lt;px&gt;</c> shrinks them like the browser first, <c>--tessdata &lt;dir&gt;</c> tries other traineddata, <c>--json &lt;file&gt;</c>
/// writes the report, <c>--seed</c> picks other generated photos, <c>--show-from</c> sets the confidence the app fills values in from,
/// <c>--verbose</c> prints what the OCR saw on photos that were not read right).</item>
/// <item><c>--synth &lt;folder&gt; [--count n] [--seed s] [--kind k]</c>: writes generated photos with their sidecars.</item>
/// <item><c>--init &lt;folder&gt;</c>: writes an empty sidecar next to every photo that has none.</item>
/// </list>
/// </summary>
internal static class ToolCommands
{
    public static bool Handles(string argument) => argument is "--eval" or "--synth" or "--init";

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter errors)
    {
        var (folder, options) = Parse(args[1..]);
        try
        {
            switch (args[0])
            {
                case "--init":
                    output.WriteLine($"Wrote {EvalFolder.Init(Require(folder, "--init <folder>"))} empty sidecar(s).");
                    return 0;
                case "--synth":
                    var cases = SyntheticGenerator.Generate(Int(options, "count", 30), Int(options, "seed", 1), kind: options.GetValueOrDefault("kind")).ToList();
                    EvalFolder.Write(Require(folder, "--synth <folder>"), cases);
                    output.WriteLine($"Wrote {cases.Count} generated photo(s) to {folder}.");
                    return 0;
                default:
                    return await EvaluateAsync(folder, options, output, errors);
            }
        }
        catch (ArgumentException e)
        {
            errors.WriteLine(e.Message);
            return 2;
        }
    }

    private static async Task<int> EvaluateAsync(string? folder, Dictionary<string, string> options, TextWriter output, TextWriter errors)
    {
        var settings = new Dictionary<string, string?>();
        if (options.TryGetValue("tessdata", out var tessdata)) settings["Reader:Tesseract:TessdataPath"] = tessdata;
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(settings)
            .Build();
        await using var services = new ServiceCollection()
            .AddLogging(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning))
            .AddReader(configuration, requireApiKey: false)
            .BuildServiceProvider();
        var images = services.GetRequiredService<SkiaImagePreparer>();
        var verbose = options.ContainsKey("verbose");
        var ocr = new RecordingOcr(services.GetRequiredService<IOcrEngine>());
        var reader = new DocumentReader(ocr, services.GetRequiredService<Lexicon>(), services.GetRequiredService<ICandidateScorer>());

        var cases = options.ContainsKey("synthetic")
            ? SyntheticGenerator.Generate(Int(options, "synthetic", 30), Int(options, "seed", 1), kind: options.GetValueOrDefault("kind"))
                .Select(c => new EvalCase(c.Name, c.Kind, c.Hints, c.Expected, c.Image))
            : EvalFolder.Load(Require(folder, "--eval <folder> (or --eval --synthetic <n>)"), errors.WriteLine);

        var report = await Evaluation.RunAsync(cases, async (c, image, ct) =>
        {
            ocr.Pages.Clear();
            var result = await reader.ReadAsync(images.Prepare(image), new ReadRequest(Evaluation.KindsFor(c.Kind), c.Hints), ct);
            // With --verbose, what the OCR saw on photos that were not read right (local photos only: this prints their text).
            if (verbose && c.Expected.Any(e => !EvalReport.Compare(e.Key, e.Value, result.Value(e.Key) ?? "").Exact))
            {
                output.WriteLine($"--- {c.Name}");
                foreach (var (pass, page) in ocr.Pages)
                {
                    output.WriteLine($"    [{pass.Variant} psm {pass.PageSegmentation} {pass.Purpose}]");
                    foreach (var line in page.Lines) output.WriteLine($"      {line.Text}");
                }
            }
            return result;
        }, options.ContainsKey("edge") ? Int(options, "edge", 1600) : null, CancellationToken.None, ShowFrom(options));
        output.Write(report.Format());
        if (options.TryGetValue("json", out var json)) await File.WriteAllTextAsync(json, report.ToJson());
        return 0;
    }

    /// <summary>Remembers the pages of the current photo's OCR passes (for --verbose).</summary>
    private sealed class RecordingOcr(IOcrEngine inner) : IOcrEngine
    {
        public List<(OcrPass Pass, OcrPage Page)> Pages { get; } = [];

        public async Task<OcrPage> RecognizeAsync(ReadOnlyMemory<byte> png, OcrPass pass, CancellationToken ct)
        {
            var page = await inner.RecognizeAsync(png, pass, ct);
            Pages.Add((pass, page));
            return page;
        }
    }

    /// <summary>The folder (the first argument that is not an option) and the options (<c>--name value</c>).</summary>
    internal static (string? Folder, Dictionary<string, string> Options) Parse(string[] args)
    {
        string? folder = null;
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                var name = args[i][2..];
                options[name] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
            }
            else folder ??= args[i];
        }
        return (folder, options);
    }

    private static double ShowFrom(Dictionary<string, string> options) =>
        !options.TryGetValue("show-from", out var text) ? EvalReport.DefaultShowFrom
        : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 1 ? value
        : throw new ArgumentException("--show-from must be a confidence between 0 and 1.");

    private static string Require(string? folder, string usage) => folder ?? throw new ArgumentException($"Usage: {usage}");

    private static int Int(Dictionary<string, string> options, string name, int fallback) =>
        !options.TryGetValue(name, out var text) ? fallback
        : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value
        : throw new ArgumentException($"--{name} must be a positive whole number.");
}
