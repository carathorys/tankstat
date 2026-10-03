using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tankstat.Reader.Core.UnitTests;

/// <summary>The reader writes exactly the shapes in tests/backend/contracts/reader, which the main app's adapter relies on.</summary>
public class ContractTests
{
    private static JsonNode Fixture(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contracts", name)))!;

    [Fact]
    public void AReadingIsWrittenLikeTheFixture()
    {
        var result = new ReadResult(DocumentReader.ModelVersion, DocumentKinds.FuelReceipt,
        [
            new ReadField(FieldNames.Currency, "HUF", 0.9, FieldSources.Ocr),
            new ReadField(FieldNames.Total, "24687", 0.94, FieldSources.Ocr),
            new ReadField(FieldNames.Volume, "38.52", 0.93, FieldSources.Ocr),
            new ReadField(FieldNames.UnitPrice, "640.9", 0.93, FieldSources.Ocr),
            new ReadField(FieldNames.Date, "2026-09-17", 0.82, FieldSources.Ocr),
        ]);

        var written = JsonSerializer.SerializeToNode(result, ReaderJson.Options);

        Assert.True(JsonNode.DeepEquals(Fixture("read-response.fuel-receipt.json"), written), written!.ToJsonString());
    }

    [Theory]
    [InlineData("read-response.fuel-receipt.json")]
    [InlineData("read-response.expense-receipt.json")]
    [InlineData("read-response.odometer.json")]
    [InlineData("read-response.unknown.json")]
    public void EveryFixture_UsesOnlyKnownKindsFieldsAndSources_AndRoundTrips(string name)
    {
        var fixture = Fixture(name);
        var result = fixture.Deserialize<ReadResult>(ReaderJson.Options)!;

        Assert.Contains(result.Kind, DocumentKinds.All.Append(DocumentKinds.Unknown));
        Assert.All(result.Fields, f => Assert.Contains(f.Name, new[] { FieldNames.Odometer, FieldNames.Total, FieldNames.Volume, FieldNames.UnitPrice, FieldNames.Currency, FieldNames.Date, FieldNames.Title }));
        Assert.All(result.Fields, f => Assert.Contains(f.Source, new[] { FieldSources.Ocr, FieldSources.Derived, FieldSources.Hint }));
        Assert.All(result.Fields, f => Assert.InRange(f.Confidence, 0, 1));
        Assert.True(JsonNode.DeepEquals(fixture, JsonSerializer.SerializeToNode(result, ReaderJson.Options)));
    }
}
