using Tankstat.Reader.Core.Extraction;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Core;

/// <summary>
/// Reads one photo: runs the OCR passes the allowed kinds need, decides what the photo shows and extracts its values. A receipt is read
/// as one block of text first and, if no total turns up, as sparse text; a dashboard is read as sparse text plus digit-only passes on
/// the photo, its inverted copy (light digits on a dark display) and two copies with thickened strokes (seven-segment digits).
/// </summary>
public sealed class DocumentReader(IOcrEngine ocr, Lexicon lexicon, ICandidateScorer scorer)
{
    /// <summary>Which reader made a reading: the rules now, trained models later. Callers store it next to what they learn from.</summary>
    public const string ModelVersion = "rules-1";

    private readonly ReceiptExtractor _receipts = new(lexicon, scorer);
    private readonly OdometerExtractor _odometers = new(lexicon);

    public DocumentReader(IOcrEngine ocr) : this(ocr, Lexicon.Default, new RuleScorer()) { }

    public async Task<ReadResult> ReadAsync(IPreparedImage image, ReadRequest request, CancellationToken ct)
    {
        var hints = request.Hints;
        var wantsReceipt = request.Kinds.Contains(DocumentKinds.FuelReceipt) || request.Kinds.Contains(DocumentKinds.ExpenseReceipt);
        var wantsOdometer = request.Kinds.Contains(DocumentKinds.Odometer);

        var block = wantsReceipt ? await ocr.RecognizeAsync(image.Png(ImageVariant.Normal), new OcrPass(ImageVariant.Normal, 6, OcrPurpose.Text), ct) : OcrPage.Empty;
        var receiptScore = DocumentClassifier.ReceiptScore(block, lexicon, hints.Today);
        if (wantsReceipt && (receiptScore >= 1.5 || !wantsOdometer))
        {
            var receipt = await ReadReceiptAsync(image, block, request, ct);
            if (receipt.Fields.Count > 0 || receiptScore >= 1.5 || !wantsOdometer) return receipt.Fields.Count > 0 ? receipt : Unknown;
        }

        if (wantsOdometer)
        {
            var sparse = await ocr.RecognizeAsync(image.Png(ImageVariant.Normal), new OcrPass(ImageVariant.Normal, 11, OcrPurpose.Text), ct);
            var digits = new List<OcrPage>();
            foreach (var variant in new[] { ImageVariant.Normal, ImageVariant.Inverted, ImageVariant.Thickened, ImageVariant.ThickenedMore })
                digits.Add(await ocr.RecognizeAsync(image.Png(variant), new OcrPass(variant, 11, OcrPurpose.Digits), ct));
            if (_odometers.Extract([block, sparse], digits, hints) is { } odometer)
                return new ReadResult(ModelVersion, DocumentKinds.Odometer, [odometer]);
        }

        // No odometer after all: a weak receipt is still better than nothing.
        if (wantsReceipt && receiptScore >= 0.8)
        {
            var receipt = await ReadReceiptAsync(image, block, request, ct);
            if (receipt.Fields.Any(f => f.Name == FieldNames.Total)) return receipt;
        }
        return Unknown;
    }

    private static ReadResult Unknown => new(ModelVersion, DocumentKinds.Unknown, []);

    private async Task<ReadResult> ReadReceiptAsync(IPreparedImage image, OcrPage block, ReadRequest request, CancellationToken ct)
    {
        var kind = DocumentClassifier.ReceiptKind(block, request.Kinds, lexicon);
        var fields = _receipts.Extract(block, kind, request.Hints);
        if (fields.All(f => f.Name != FieldNames.Total))
        {
            var sparse = await ocr.RecognizeAsync(image.Png(ImageVariant.Normal), new OcrPass(ImageVariant.Normal, 11, OcrPurpose.Text), ct);
            var second = _receipts.Extract(sparse, kind, request.Hints);
            if (second.Any(f => f.Name == FieldNames.Total) || second.Count > fields.Count) fields = second;
        }
        return new ReadResult(ModelVersion, kind, fields);
    }
}
