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

        // The sparse text pass serves the receipt and the odometer alike, and each pass is a Tesseract run: it is never run twice.
        OcrPage? sparsePage = null;
        async Task<OcrPage> SparseAsync() =>
            sparsePage ??= await ocr.RecognizeAsync(image.Png(ImageVariant.Normal), new OcrPass(ImageVariant.Normal, 11, OcrPurpose.Text), ct);

        ReadResult? receipt = null;
        if (wantsReceipt && (receiptScore >= DocumentClassifier.ReceiptFrom || !wantsOdometer))
        {
            receipt = await ReadReceiptAsync(block, request, SparseAsync);
            // A receipt of nothing the app fills in (a stray currency, a total that is only a guess) may be a dashboard whose noise
            // looked like one: the odometer is looked for before settling. One read with a value the app fills in is a receipt, and what
            // else its digits might be (a receipt number, one of its amounts) is far more often read as a weak odometer than a dashboard
            // is read as a sure receipt.
            if (!wantsOdometer || FillsIn(receipt.Fields)) return receipt.Fields.Count > 0 ? receipt : Unknown;
        }

        if (wantsOdometer)
        {
            var sparse = await SparseAsync();
            var digits = new List<OcrPage>();
            foreach (var variant in new[] { ImageVariant.Normal, ImageVariant.Inverted, ImageVariant.Thickened, ImageVariant.ThickenedMore })
                digits.Add(await ocr.RecognizeAsync(image.Png(variant), new OcrPass(variant, 11, OcrPurpose.Digits), ct));

            if (_odometers.Extract([block, sparse], digits, hints) is { } odometer)
                return new ReadResult(ModelVersion, DocumentKinds.Odometer, [odometer]);
        }

        // No odometer after all: what was read of the receipt, or a weak receipt with a total, is still better than nothing.
        if (receipt is { Fields.Count: > 0 }) return receipt;
        if (wantsReceipt && receipt is null && receiptScore >= DocumentClassifier.WeakReceiptFrom)
        {
            receipt = await ReadReceiptAsync(block, request, SparseAsync);
            if (HasTotal(receipt.Fields)) return receipt;
        }
        return Unknown;
    }

    private static ReadResult Unknown => new(ModelVersion, DocumentKinds.Unknown, []);

    private static bool HasTotal(IReadOnlyList<ReadField> fields) => fields.Any(f => f.Name == FieldNames.Total);

    private static bool FillsIn(IReadOnlyList<ReadField> fields) => fields.Any(f => f.Confidence >= ReadField.FillInFrom);

    private async Task<ReadResult> ReadReceiptAsync(OcrPage block, ReadRequest request, Func<Task<OcrPage>> sparseText)
    {
        var kind = DocumentClassifier.ReceiptKind(block, request.Kinds, lexicon);
        var fields = _receipts.Extract(block, kind, request.Hints);
        if (!HasTotal(fields))
        {
            var second = _receipts.Extract(await sparseText(), kind, request.Hints);
            if (HasTotal(second) || second.Count > fields.Count) fields = second;
        }
        return new ReadResult(ModelVersion, kind, fields);
    }
}
