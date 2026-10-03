using SkiaSharp;
using Tankstat.Reader.Core;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.UnitTests;
using Tankstat.Reader.Synthetic;
using Tankstat.Reader.Tools;

namespace Tankstat.Reader.IntegrationTests;

public sealed class EvalTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tankstat-eval-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static EvalCase Case(SyntheticCase c) => new(c.Name, c.Kind, c.Hints, c.Expected, c.Image);

    /// <summary>An OCR that reads exactly what was drawn (digit passes see only the digits, as Tesseract with its whitelist would).</summary>
    private sealed class PerfectOcr(SyntheticCase drawn) : IOcrEngine
    {
        public Task<OcrPage> RecognizeAsync(ReadOnlyMemory<byte> png, OcrPass pass, CancellationToken ct)
        {
            var words = pass.Purpose == OcrPurpose.Text
                ? drawn.Words
                : drawn.Words.Select(w => w with { Text = new string(w.Text.Where(char.IsDigit).ToArray()) }).Where(w => w.Text.Length > 0).ToList();
            return Task.FromResult(new OcrPage(drawn.Width, drawn.Height, words));
        }
    }

    [Fact]
    public async Task WithAPerfectOcr_TheRulesReadEveryKindOfGeneratedPhoto()
    {
        var cases = SyntheticGenerator.Generate(30, seed: 11).ToList();

        var report = await Evaluation.RunAsync(cases.Select(Case), (c, _, ct) =>
            new DocumentReader(new PerfectOcr(cases.Single(x => x.Name == c.Name))).ReadAsync(new FakeImage(), new ReadRequest(Evaluation.KindsFor(c.Kind), c.Hints), ct), null, default);

        Assert.Equal((30, 30), (report.Cases, report.KindRight));
        Assert.All(report.Fields, f => Assert.True(f.ExactRate >= 0.9, $"{f.Kind} {f.Field}: {f.Exact} of {f.Count}\n{report.Format()}"));
    }

    [Fact]
    public void TheReport_CountsEveryField_AndListsTheMismatches()
    {
        var report = new EvalReport();
        var expected = new Dictionary<string, string> { [FieldNames.Total] = "24687", [FieldNames.Volume] = "38.52", [FieldNames.Date] = "2026-09-17" };
        report.Add(new EvalCase("a", DocumentKinds.FuelReceipt, new ReadHints("hu", null, null, default), expected, []),
            new ReadResult("rules-1", DocumentKinds.FuelReceipt, [new ReadField(FieldNames.Total, "24687", 0.9, "ocr"), new ReadField(FieldNames.Volume, "38.5", 0.4, "ocr")]));

        var text = report.Format();

        Assert.Equal((1, 0, 1), (report.Score(DocumentKinds.FuelReceipt, FieldNames.Total)!.Exact, report.Score(DocumentKinds.FuelReceipt, FieldNames.Volume)!.Exact,
            report.Score(DocumentKinds.FuelReceipt, FieldNames.Date)!.Missing));
        Assert.Equal(1, report.Score(DocumentKinds.FuelReceipt, FieldNames.Volume)!.Close);    // 38.5 is within 0.5 % of 38.52
        Assert.Equal(0, report.Score(DocumentKinds.FuelReceipt, FieldNames.Volume)!.Shown);    // and too unsure to be filled in
        Assert.Contains("fuel-receipt     total", text);
        Assert.Contains("a  volume: expected 38.52, got 38.5 (0.40)", text);
        Assert.Contains("a  date: expected 2026-09-17, got nothing", text);
        Assert.Contains("\"exactRate\": 1", report.ToJson());
    }

    [Theory]
    [InlineData("odometer", "123456", "123457", false, false)] // an odometer is right or wrong
    [InlineData("total", "24687", "24687.00", true, true)]
    [InlineData("total", "100", "100.4", false, true)]
    [InlineData("title", "Fék Autószerviz Bt.", "FEK AUTOSZERVIZ BT.", true, true)]
    [InlineData("title", "Fék Autószerviz Bt.", "Fék Autészerviz Bt.", false, true)]
    [InlineData("currency", "HUF", "huf", true, true)]
    public void ValuesAreCompared_ByWhatTheyMean(string field, string expected, string actual, bool exact, bool close) =>
        Assert.Equal((exact, close), EvalReport.Compare(field, expected, actual));

    [Fact]
    public void GeneratedPhotosWrittenToAFolder_LoadBackAsTheSameCases()
    {
        var cases = SyntheticGenerator.Generate(4, seed: 12).ToList();
        EvalFolder.Write(_dir, cases);
        var skipped = new List<string>();

        var loaded = EvalFolder.Load(_dir, skipped.Add).ToList();

        Assert.Empty(skipped);
        Assert.Equal(cases.Select(c => c.Name + ".webp").Order(), loaded.Select(c => c.Name).Order());
        Assert.All(loaded, l =>
        {
            var original = cases.Single(c => c.Name + ".webp" == l.Name);
            Assert.Equal((original.Kind, original.Hints), (l.Kind, l.Hints));
            Assert.Equal(original.Expected.OrderBy(e => e.Key), l.Expected.OrderBy(e => e.Key));
            Assert.Equal(original.Image, l.Image);
        });
    }

    [Fact]
    public void Init_WritesEmptySidecars_OnlyWhereThereIsNone_AndThoseAreSkippedUntilFilledIn()
    {
        File.WriteAllBytes(Path.Combine(_dir, "a.jpg"), TestImages.Make(format: SKEncodedImageFormat.Jpeg));
        File.WriteAllBytes(Path.Combine(_dir, "b.png"), TestImages.Make());
        File.WriteAllText(Path.Combine(_dir, "b.expected.json"), """{ "kind": "odometer", "fields": { "odometer": 123456 } }""");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "not a photo");

        Assert.Equal(1, EvalFolder.Init(_dir));
        Assert.Equal(0, EvalFolder.Init(_dir));

        var loaded = EvalFolder.Load(_dir, _ => { }).ToList();
        Assert.Equal(["a.jpg", "b.png"], loaded.Select(c => c.Name));
        Assert.Empty(loaded[0].Expected);                                   // the template's values are all empty
        Assert.Equal("123456", loaded[1].Expected[FieldNames.Odometer]);  // a number in the sidecar is fine too
    }

    [Fact]
    public async Task TheCommandLineTools_WriteGeneratedPhotos_AndExplainTheirUsage()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        Assert.Equal(0, await ToolCommands.RunAsync(["--synth", _dir, "--count", "3", "--seed", "2"], output, errors));
        Assert.Equal(6, Directory.GetFiles(_dir).Length);
        Assert.Equal(0, await ToolCommands.RunAsync(["--init", _dir], output, errors));
        Assert.Contains("Wrote 0 empty sidecar(s).", output.ToString());
        Assert.Equal(2, await ToolCommands.RunAsync(["--eval"], output, errors));
        Assert.Equal(2, await ToolCommands.RunAsync(["--synth", _dir, "--count", "zero"], output, errors));
        Assert.Contains("Usage: --eval <folder>", errors.ToString());
    }

    [Fact]
    public void TheBrowserResize_ShrinksToTheEdge_AndNeverEnlarges()
    {
        using var shrunk = SKBitmap.Decode(Evaluation.BrowserResize(TestImages.Make(3200, 2400), 1600));
        using var kept = SKBitmap.Decode(Evaluation.BrowserResize(TestImages.Make(800, 600), 1600));

        Assert.Equal((1600, 1200, 800, 600), (shrunk.Width, shrunk.Height, kept.Width, kept.Height));
    }
}
