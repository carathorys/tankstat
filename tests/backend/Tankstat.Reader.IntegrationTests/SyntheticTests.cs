using SkiaSharp;
using Tankstat.Reader.Core;
using Tankstat.Reader.Core.Extraction;
using Tankstat.Reader.Synthetic;

namespace Tankstat.Reader.IntegrationTests;

public class SyntheticTests
{
    private static string Describe(IEnumerable<SyntheticCase> cases) =>
        string.Join("\n", cases.Select(c => $"{c.Name} {c.Hints} {string.Join(",", c.Expected.OrderBy(e => e.Key).Select(e => $"{e.Key}={e.Value}"))}"));

    [Fact]
    public void TheSameSeed_GivesTheSamePhotos_AnotherSeedOthers()
    {
        var first = SyntheticGenerator.Generate(6, seed: 3).ToList();
        var again = SyntheticGenerator.Generate(6, seed: 3).ToList();

        Assert.Equal(Describe(first), Describe(again));
        Assert.All(first.Zip(again), pair => Assert.Equal(pair.First.Image, pair.Second.Image));
        Assert.NotEqual(Describe(first), Describe(SyntheticGenerator.Generate(6, seed: 4)));
    }

    [Fact]
    public void KindsTakeTurns_AndEachKindHasItsValues()
    {
        var cases = SyntheticGenerator.Generate(9, seed: 5).ToList();

        Assert.Equal(DocumentKinds.All.SelectMany(_ => DocumentKinds.All).Take(9), cases.Select(c => c.Kind));
        Assert.All(cases.Where(c => c.Kind == DocumentKinds.Odometer), c => Assert.Equal([FieldNames.Odometer], c.Expected.Keys));
        Assert.All(cases.Where(c => c.Kind == DocumentKinds.FuelReceipt), c =>
            Assert.Equal(new[] { FieldNames.Currency, FieldNames.Date, FieldNames.Total, FieldNames.UnitPrice, FieldNames.Volume }, c.Expected.Keys.Order()));
        Assert.All(cases.Where(c => c.Kind == DocumentKinds.ExpenseReceipt), c => Assert.Contains(FieldNames.Title, c.Expected.Keys));
        Assert.Single(SyntheticGenerator.Generate(1, kind: DocumentKinds.Odometer), c => c.Kind == DocumentKinds.Odometer);
    }

    [Fact]
    public void FuelReceiptsAddUp_LikeRealOnes()
    {
        foreach (var c in SyntheticGenerator.Generate(12, seed: 6, kind: DocumentKinds.FuelReceipt))
        {
            var (total, volume, price) = (decimal.Parse(c.Expected[FieldNames.Total]), decimal.Parse(c.Expected[FieldNames.Volume]), decimal.Parse(c.Expected[FieldNames.UnitPrice]));
            Assert.True(ReceiptExtractor.Fits(total, volume * price, c.Expected[FieldNames.Currency] == "HUF" ? 0 : 2), c.Name);
        }
    }

    [Fact]
    public void ThePhotosAreWebp_TheirWordsLieOnThem_AndCarryTheirLine()
    {
        foreach (var c in SyntheticGenerator.Generate(3, seed: 7))
        {
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(c.Image, 0, 4));
            using var photo = SKBitmap.Decode(c.Image);
            Assert.Equal((c.Width, c.Height), (photo.Width, photo.Height));
            Assert.All(c.Words, w => Assert.True(w.Box.Left > -20 && w.Box.Top > -20 && w.Box.Right < c.Width + 20 && w.Box.Bottom < c.Height + 20, c.Name));
            Assert.All(c.Words, w => Assert.True(w.LineKey > 0));
        }
    }

    [Fact]
    public void TheValuesArePrintedOnThePhoto()
    {
        var receipt = SyntheticGenerator.Generate(1, seed: 2, kind: DocumentKinds.FuelReceipt).Single(); // an English one
        var odometer = SyntheticGenerator.Generate(1, seed: 2, kind: DocumentKinds.Odometer).Single();

        Assert.Contains(receipt.Words, w => w.Text.Contains(receipt.Expected[FieldNames.Total].Split('.')[0]));
        Assert.Contains(odometer.Words, w => w.Text == odometer.Expected[FieldNames.Odometer]);
    }
}
