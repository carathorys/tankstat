using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Reader.Core;
using Tankstat.Reader.Imaging;
using Tankstat.Reader.Synthetic;
using Tankstat.Reader.Tools;
using Xunit.Abstractions;

namespace Tankstat.Reader.IntegrationTests;

/// <summary>The whole path with the real Tesseract (CI's reader job installs it with hun, eng and deu; skipped where it is missing).</summary>
public class RealOcrTests(ITestOutputHelper output)
{
    [TesseractFact]
    public async Task AGeneratedFuelReceipt_IsReadOverHttp_WithTheRealTesseract()
    {
        var receipt = SyntheticGenerator.Generate(1, seed: 2, kind: DocumentKinds.FuelReceipt).Single(); // English, read right with any language set
        using var app = new ReaderApp(realOcr: true);

        var response = await app.Client().PostAsync("/v1/read?kinds=odometer,fuel-receipt&locale=en&today=2026-10-01", TestImages.Content(receipt.Image, "image/webp"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ReadResult>(ReaderJson.Options))!;
        Assert.Equal(DocumentKinds.FuelReceipt, result.Kind);
        Assert.Equal((receipt.Expected[FieldNames.Total], receipt.Expected[FieldNames.Volume], receipt.Expected[FieldNames.UnitPrice]),
            (result.Value(FieldNames.Total), result.Value(FieldNames.Volume), result.Value(FieldNames.UnitPrice)));
    }

    [TesseractFact]
    public async Task GeneratedReceipts_AreMostlyReadRight_WithTheRealTesseract()
    {
        using var app = new ReaderApp(realOcr: true);
        var reader = app.Factory.Services.GetRequiredService<DocumentReader>();
        var images = app.Factory.Services.GetRequiredService<SkiaImagePreparer>();
        var cases = SyntheticGenerator.Generate(8, seed: 21).Where(c => c.Kind != DocumentKinds.Odometer).Select(c => new EvalCase(c.Name, c.Kind, c.Hints, c.Expected, c.Image));

        var report = await Evaluation.RunAsync(cases, (c, image, ct) => reader.ReadAsync(images.Prepare(image), new ReadRequest(Evaluation.KindsFor(c.Kind), c.Hints), ct), null, default);
        output.WriteLine(report.Format());

        // A floor, not a target: it catches a broken pipeline (languages, preprocessing, parsing), while the numbers live in the report.
        Assert.True(report.Score(DocumentKinds.FuelReceipt, FieldNames.Total)!.ExactRate >= 0.66, report.Format());
        Assert.True(report.Score(DocumentKinds.ExpenseReceipt, FieldNames.Total)!.ExactRate >= 0.66, report.Format());
    }
}
