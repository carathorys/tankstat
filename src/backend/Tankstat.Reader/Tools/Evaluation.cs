using System.Globalization;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Tankstat.Reader.Core;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Tools;

/// <summary>A photo with the values a perfect reading gives (from a sidecar file, or generated).</summary>
public sealed record EvalCase(string Name, string Kind, ReadHints Hints, IReadOnlyDictionary<string, string> Expected, byte[] Image);

/// <summary>How well one field of one kind was read.</summary>
public sealed class FieldScore(string kind, string field)
{
    public string Kind { get; } = kind;
    public string Field { get; } = field;
    public int Count { get; set; }
    public int Exact { get; set; }
    public int Close { get; set; }
    public int Wrong { get; set; }
    public int Missing { get; set; }
    public double ConfidenceRight { get; set; }
    public double ConfidenceWrong { get; set; }

    /// <summary>Values confident enough to be shown to the user (see <see cref="EvalReport.ShowFrom"/>), and how many of those were wrong.</summary>
    public int Shown { get; set; }
    public int ShownWrong { get; set; }

    public double ExactRate => Count == 0 ? 0 : (double)Exact / Count;
}

public sealed record Mismatch(string Case, string Field, string Expected, string? Actual, double? Confidence);

/// <summary>The result of an evaluation: per kind and field, how many values were exact, close (within 0.5 %), wrong or missing.</summary>
public sealed class EvalReport(double showFrom = EvalReport.DefaultShowFrom)
{
    /// <summary>The confidence from which the app fills a value in (its default <c>MinConfidence</c>): wrong values below it are never seen.</summary>
    public const double DefaultShowFrom = 0.6;

    private readonly Dictionary<(string, string), FieldScore> _fields = [];

    public double ShowFrom { get; } = showFrom;

    public int Cases { get; private set; }
    public int KindRight { get; private set; }
    public List<Mismatch> Mismatches { get; } = [];
    public IEnumerable<FieldScore> Fields => _fields.Values.OrderBy(f => f.Kind, StringComparer.Ordinal).ThenBy(f => f.Field, StringComparer.Ordinal);

    public FieldScore? Score(string kind, string field) => _fields.GetValueOrDefault((kind, field));

    public void Add(EvalCase c, ReadResult result)
    {
        Cases++;
        if (result.Kind == c.Kind) KindRight++;
        else Mismatches.Add(new Mismatch(c.Name, "kind", c.Kind, result.Kind, null));
        foreach (var (field, expected) in c.Expected)
        {
            var score = _fields.TryGetValue((c.Kind, field), out var s) ? s : _fields[(c.Kind, field)] = new FieldScore(c.Kind, field);
            score.Count++;
            var actual = result.Fields.FirstOrDefault(f => f.Name == field);
            if (actual is null)
            {
                score.Missing++;
                Mismatches.Add(new Mismatch(c.Name, field, expected, null, null));
                continue;
            }
            var (exact, close) = Compare(field, expected, actual.Value);
            if (actual.Confidence >= ShowFrom)
            {
                score.Shown++;
                if (!exact) score.ShownWrong++;
            }
            if (exact) score.Exact++;
            if (close) score.Close++;
            if (exact) score.ConfidenceRight += actual.Confidence;
            else
            {
                score.Wrong++;
                score.ConfidenceWrong += actual.Confidence;
                Mismatches.Add(new Mismatch(c.Name, field, expected, actual.Value, actual.Confidence));
            }
        }
    }

    /// <summary>Numbers must be equal (close: within 0.5 %, never for odometers); the title is compared without case and accents.</summary>
    public static (bool Exact, bool Close) Compare(string field, string expected, string actual)
    {
        if (field is FieldNames.Total or FieldNames.Volume or FieldNames.UnitPrice or FieldNames.Odometer)
        {
            if (!decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var e)
                || !decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var a)) return (false, false);
            var exact = Math.Abs(a - e) < 0.0005m;
            return (exact, exact || (field != FieldNames.Odometer && e != 0 && Math.Abs(a - e) / Math.Abs(e) <= 0.005m));
        }
        if (field == FieldNames.Title)
        {
            static string Plain(string s) => string.Join(' ', Folding.Fold(s).Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var (pe, pa) = (Plain(expected), Plain(actual));
            return (pe == pa, pe == pa || Folding.Distance(pe, pa, 2) <= 2);
        }
        var same = string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);
        return (same, same);
    }

    public string Format(int mismatches = 20)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Photo reader evaluation ({DocumentReader.ModelVersion}): {Cases} photos, kind right {KindRight} ({Percent(KindRight, Cases)} %)");
        text.AppendLine();
        text.AppendLine($"{"kind",-16} {"field",-10} {"n",4} {"exact",6} {"close",6} {"wrong",6} {"missing",8} {"exact %",8} {"conf right",11} {"conf wrong",11} {"shown",6} {"shown wrong",12}");
        foreach (var f in Fields)
            text.AppendLine(CultureInfo.InvariantCulture,
                $"{f.Kind,-16} {f.Field,-10} {f.Count,4} {f.Exact,6} {f.Close,6} {f.Wrong,6} {f.Missing,8} {Percent(f.Exact, f.Count),8} {Average(f.ConfidenceRight, f.Exact),11} {Average(f.ConfidenceWrong, f.Wrong),11} {f.Shown,6} {f.ShownWrong,12}");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"shown = confidence of at least {ShowFrom:0.00}, which the app fills in; values below it are left for the user.");
        if (Mismatches.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"Mismatches (first {Math.Min(mismatches, Mismatches.Count)} of {Mismatches.Count}):");
            foreach (var m in Mismatches.Take(mismatches))
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"  {m.Case}  {m.Field}: expected {m.Expected}, got {m.Actual ?? "nothing"}{(m.Confidence is { } c ? $" ({c:0.00})" : "")}");
        }
        return text.ToString();
    }

    public string ToJson() => JsonSerializer.Serialize(new
    {
        modelVersion = DocumentReader.ModelVersion,
        cases = Cases,
        kindRight = KindRight,
        showFrom = ShowFrom,
        fields = Fields.Select(f => new { f.Kind, f.Field, f.Count, f.Exact, f.Close, f.Wrong, f.Missing, f.Shown, f.ShownWrong, exactRate = Math.Round(f.ExactRate, 4) }),
        mismatches = Mismatches,
    }, new JsonSerializerOptions(ReaderJson.Options) { WriteIndented = true });

    private static string Percent(int part, int whole) => whole == 0 ? "-" : (100.0 * part / whole).ToString("0.0", CultureInfo.InvariantCulture);
    private static string Average(double sum, int count) => count == 0 ? "-" : (sum / count).ToString("0.00", CultureInfo.InvariantCulture);
}

public static class Evaluation
{
    /// <summary>The kinds a photo is read with: those of the form it would be taken in (a dashboard is taken in the refueling form).</summary>
    public static IReadOnlySet<string> KindsFor(string kind) => kind == DocumentKinds.ExpenseReceipt
        ? new HashSet<string> { DocumentKinds.Odometer, DocumentKinds.ExpenseReceipt }
        : new HashSet<string> { DocumentKinds.Odometer, DocumentKinds.FuelReceipt };

    /// <param name="edge">Shrink each photo first the way the browser does (longest edge, WebP 85 %), to see what a smaller upload costs.</param>
    public static async Task<EvalReport> RunAsync(
        IEnumerable<EvalCase> cases, Func<EvalCase, byte[], CancellationToken, Task<ReadResult>> read, int? edge, CancellationToken ct, double showFrom = EvalReport.DefaultShowFrom)
    {
        var report = new EvalReport(showFrom);
        foreach (var c in cases)
        {
            var image = edge is { } e ? BrowserResize(c.Image, e) : c.Image;
            report.Add(c, await read(c, image, ct));
        }
        return report;
    }

    /// <summary>What src/frontend/pictures/resizeImage.ts does: scale down (never up) to the longest edge, encode as WebP at 85 %.</summary>
    public static byte[] BrowserResize(byte[] image, int edge)
    {
        using var bitmap = SKBitmap.Decode(image);
        if (bitmap is null) return image;
        var scale = Math.Min(1, (double)edge / Math.Max(bitmap.Width, bitmap.Height));
        using var resized = scale < 1
            ? bitmap.Resize(new SKImageInfo((int)Math.Round(bitmap.Width * scale), (int)Math.Round(bitmap.Height * scale)), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            : bitmap.Copy();
        using var encoded = SKImage.FromBitmap(resized);
        using var data = encoded.Encode(SKEncodedImageFormat.Webp, 85);
        return data.ToArray();
    }
}

/// <summary>
/// A folder of private photos for <c>--eval</c>: next to each picture a <c>&lt;name&gt;.expected.json</c> with its kind, the hints and the
/// values it shows. <c>--init</c> writes empty ones to fill in. Nothing of it is ever committed (the repo ignores <c>eval/</c>).
/// </summary>
public static class EvalFolder
{
    private static readonly string[] Pictures = [".jpg", ".jpeg", ".png", ".webp"];

    public sealed class ExpectedFile
    {
        public string Kind { get; set; } = "";
        public HintsFile? Hints { get; set; }
        public Dictionary<string, JsonElement> Fields { get; set; } = [];
    }

    public sealed class HintsFile
    {
        public string? Locale { get; set; }
        public long? LastOdometer { get; set; }
        public string? Currency { get; set; }
        public string? Today { get; set; }
    }

    public static string SidecarOf(string picture) => Path.Combine(Path.GetDirectoryName(picture)!, Path.GetFileNameWithoutExtension(picture) + ".expected.json");

    public static IEnumerable<string> PicturesIn(string folder) =>
        Directory.EnumerateFiles(folder).Where(f => Pictures.Contains(Path.GetExtension(f).ToLowerInvariant())).Order(StringComparer.Ordinal);

    /// <summary>The photos that have a filled-in sidecar; the others are reported through <paramref name="skipped"/>.</summary>
    public static IEnumerable<EvalCase> Load(string folder, Action<string> skipped)
    {
        foreach (var picture in PicturesIn(folder))
        {
            var sidecar = SidecarOf(picture);
            if (!File.Exists(sidecar))
            {
                skipped($"{Path.GetFileName(picture)}: no {Path.GetFileName(sidecar)} (run --init to create one)");
                continue;
            }
            var file = JsonSerializer.Deserialize<ExpectedFile>(File.ReadAllText(sidecar), ReaderJson.Options) ?? new ExpectedFile();
            if (!DocumentKinds.All.Contains(file.Kind))
            {
                skipped($"{Path.GetFileName(sidecar)}: kind must be one of {string.Join(", ", DocumentKinds.All)}");
                continue;
            }
            var expected = file.Fields
                .Where(f => f.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                .ToDictionary(f => f.Key, f => f.Value.ValueKind == JsonValueKind.String ? f.Value.GetString()! : f.Value.GetRawText());
            var hints = file.Hints ?? new HintsFile();
            var today = DateOnly.TryParseExact(hints.Today, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                ? t
                : DateOnly.FromDateTime(DateTime.UtcNow);
            yield return new EvalCase(Path.GetFileName(picture), file.Kind, new ReadHints(ReadHints.NormalizeLocale(hints.Locale), hints.LastOdometer, hints.Currency, today),
                expected, File.ReadAllBytes(picture));
        }
    }

    /// <summary>Writes an empty sidecar next to every picture that has none; returns how many were written.</summary>
    public static int Init(string folder)
    {
        var written = 0;
        foreach (var picture in PicturesIn(folder).Where(p => !File.Exists(SidecarOf(p))))
        {
            File.WriteAllText(SidecarOf(picture), """
                {
                  "kind": "fuel-receipt",
                  "hints": { "locale": "hu", "lastOdometer": null, "currency": null, "today": null },
                  "fields": { "odometer": null, "total": null, "volume": null, "unitPrice": null, "currency": null, "date": null, "title": null }
                }
                """);
            written++;
        }
        return written;
    }

    /// <summary>Writes generated cases as pictures with sidecars, so they can be looked at and evaluated like real photos.</summary>
    public static void Write(string folder, IEnumerable<Synthetic.SyntheticCase> cases)
    {
        Directory.CreateDirectory(folder);
        foreach (var c in cases)
        {
            File.WriteAllBytes(Path.Combine(folder, c.Name + ".webp"), c.Image);
            var sidecar = new
            {
                kind = c.Kind,
                hints = new { locale = c.Hints.Locale, lastOdometer = c.Hints.LastOdometer, currency = c.Hints.Currency, today = c.Hints.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                fields = c.Expected,
            };
            File.WriteAllText(Path.Combine(folder, c.Name + ".expected.json"), JsonSerializer.Serialize(sidecar, new JsonSerializerOptions(ReaderJson.Options) { WriteIndented = true }));
        }
    }
}
