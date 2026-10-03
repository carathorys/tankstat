using System.Globalization;
using SkiaSharp;
using Tankstat.Reader.Core;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Synthetic;

internal enum LineStyle
{
    Normal,
    Bold,
    Large,
}

/// <summary>One printed line: text on the left (or centred), an optional amount on the right.</summary>
internal sealed record ReceiptLine(string Left, string? Right = null, LineStyle Style = LineStyle.Normal, bool Centered = false);

/// <summary>What a generated receipt says and what reading it must give.</summary>
internal sealed record ReceiptContent(IReadOnlyList<ReceiptLine> Lines, string Kind, string Locale, string Currency, IReadOnlyDictionary<string, string> Expected);

/// <summary>
/// Writes receipts the way Hungarian, English and German receipts are printed: the station or shop, its address and tax number, the
/// items, for fuel the "litres x price per litre" line, the total with its keyword, the card payment, VAT, the date and a thank-you.
/// The values are consistent (total = litres × price per litre, rounded like the currency) and formatted the local way.
/// </summary>
internal static class Receipts
{
    private sealed record Language(
        string Locale, string Currency, string Tax, string[] FuelStations, string[] Shops, string[] Products, (string Name, int Min, int Max)[] Items,
        string Total, string Card, string Vat, int VatRate, string Thanks, string PerLitre, string Times, string Receipt);

    private static readonly Language[] Languages =
    [
        new("hu", "HUF", "Adószám: {0}-2-42",
            ["Benzinkút Kft.", "Útmenti Töltőállomás Zrt.", "Tisza Petrol Kft.", "Kút és Társa Bt."],
            ["Csillag Autómosó Kft.", "Belvárosi Parkoló Zrt.", "Gumi és Szerviz Kft.", "Fék Autószerviz Bt."],
            ["Gázolaj", "Benzin 95", "Benzin 100", "Prémium dízel"],
            [("Prémium mosás", 3990, 8990), ("Parkolás 2 óra", 800, 1600), ("Olajcsere", 15000, 35000), ("Ablaktörlő lapát", 4990, 9990)],
            "ÖSSZESEN:", "Bankkártya", "ÁFA 27%", 27, "Köszönjük a vásárlást!", "Ft/l", "x", "NYUGTA"),
        new("en", "GBP", "VAT No. GB {0}",
            ["NORTHWAY FUELS LTD", "RIVERSIDE SERVICE STATION", "HILLTOP FUEL STOP"],
            ["CITY PARKING LTD", "QUICK WASH CENTRE", "GREEN LANE GARAGE"],
            ["Unleaded", "Diesel", "Super Unleaded"],
            [("Premium wash", 8, 15), ("Parking 2h", 4, 9), ("Oil change", 60, 120), ("Wiper blades", 15, 30)],
            "TOTAL", "CARD", "VAT 20%", 20, "Thank you for your visit", "GBP/L", "@", "RECEIPT"),
        new("de", "EUR", "St.-Nr. {0}",
            ["Tankstelle Süd GmbH", "Autohof Mitte GmbH", "Rasthof Weide GmbH"],
            ["Waschanlage Nord GmbH", "Parkhaus am Markt", "Kfz-Werkstatt Meier"],
            ["Super E10", "Super E5", "Diesel"],
            [("Programm Premium", 9, 18), ("Parken 2 Std.", 3, 8), ("Ölwechsel", 60, 130), ("Wischerblätter", 15, 35)],
            "SUMME EUR", "EC-Karte", "MwSt 19%", 19, "Vielen Dank und gute Fahrt", "EUR/l", "x", "BELEG"),
    ];

    public static ReceiptContent Make(Random random, bool fuel, DateOnly today)
    {
        var lang = Languages[random.Next(Languages.Length)];
        var date = today.AddDays(-random.Next(0, 60));
        var time = $"{random.Next(6, 23):00}:{random.Next(0, 60):00}";
        var lines = new List<ReceiptLine>();
        var shop = fuel ? Pick(random, lang.FuelStations) : Pick(random, lang.Shops);
        lines.Add(new ReceiptLine(shop, Style: LineStyle.Bold, Centered: true));
        lines.Add(new ReceiptLine(Address(random, lang.Locale), Centered: true));
        lines.Add(new ReceiptLine(string.Format(CultureInfo.InvariantCulture, lang.Tax, random.Next(10_000_000, 99_999_999)), Centered: true));
        lines.Add(new ReceiptLine(""));
        lines.Add(new ReceiptLine(lang.Receipt, Style: LineStyle.Bold, Centered: true));

        decimal total;
        var expected = new Dictionary<string, string>();
        if (fuel)
        {
            var volume = Math.Round((decimal)(5 + random.NextDouble() * 55), 2);
            var price = lang.Currency == "HUF" ? Math.Round((decimal)(560 + random.NextDouble() * 160), 1) : Math.Round((decimal)(1.3 + random.NextDouble() * 0.7), 3);
            total = Math.Round(volume * price, Currencies.Decimals(lang.Currency), MidpointRounding.AwayFromZero);
            lines.Add(new ReceiptLine(Pick(random, lang.Products), Money(total, lang)));
            lines.Add(new ReceiptLine($"{Number(volume, 2, lang)} {(lang.Locale == "en" ? "L" : "l")} {lang.Times} {Number(price, lang.Currency == "HUF" ? 1 : 3, lang)} {lang.PerLitre}"));
            expected[FieldNames.Volume] = Numbers.Format(volume, 3);
            expected[FieldNames.UnitPrice] = Numbers.Format(price, 3);
        }
        else
        {
            total = 0;
            foreach (var (name, min, max) in Enumerable.Range(0, random.Next(1, 3)).Select(_ => Pick(random, lang.Items)).DistinctBy(i => i.Name))
            {
                var amount = lang.Currency == "HUF" ? random.Next(min, max) / 10 * 10 : Math.Round((decimal)(min + random.NextDouble() * (max - min)), 2);
                total += amount;
                lines.Add(new ReceiptLine(name, Money(amount, lang)));
            }
            expected[FieldNames.Title] = shop;
        }

        lines.Add(new ReceiptLine(new string('-', 32)));
        lines.Add(new ReceiptLine(lang.Total, Money(total, lang), LineStyle.Large));
        lines.Add(new ReceiptLine(lang.Card, Money(total, lang)));
        var vat = Math.Round(total * lang.VatRate / (100m + lang.VatRate), Currencies.Decimals(lang.Currency), MidpointRounding.AwayFromZero);
        lines.Add(new ReceiptLine(lang.Vat, Money(vat, lang)));
        lines.Add(new ReceiptLine($"{Date(date, lang.Locale)} {time}"));
        lines.Add(new ReceiptLine(""));
        lines.Add(new ReceiptLine(lang.Thanks, Centered: true));

        expected[FieldNames.Total] = Numbers.Format(total, 2);
        expected[FieldNames.Currency] = lang.Currency;
        expected[FieldNames.Date] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new ReceiptContent(lines, fuel ? DocumentKinds.FuelReceipt : DocumentKinds.ExpenseReceipt, lang.Locale, lang.Currency, expected);
    }

    /// <summary>Prints the receipt on a paper strip (80 mm at about 180 dpi) and remembers where every word went.</summary>
    public static (SKBitmap Paper, List<DrawnWord> Words) Print(ReceiptContent content, Random random)
    {
        const int width = 600;
        const float margin = 24;
        using var normal = new SKFont(Fonts.Mono, 24);
        using var bold = new SKFont(Fonts.Mono, 24) { Embolden = true };
        using var large = new SKFont(Fonts.Mono, 34) { Embolden = true };
        var height = (int)(margin * 2 + content.Lines.Sum(l => Writer.Height(Font(l.Style)) * 1.35f));

        var paper = new SKBitmap(width, height);
        using var canvas = new SKCanvas(paper);
        var shade = (byte)random.Next(238, 256);
        canvas.Clear(new SKColor(shade, shade, (byte)Math.Max(0, shade - random.Next(0, 10))));
        var ink = (byte)random.Next(10, 70);
        var writer = new Writer(canvas);
        var top = margin;
        foreach (var line in content.Lines)
        {
            var font = Font(line.Style);
            var x = line.Centered ? (width - Writer.Width(line.Left, font)) / 2 : margin;
            if (line.Left.Length > 0) writer.Write(line.Left, x, top, font, new SKColor(ink, ink, ink));
            if (line.Right is { } right) writer.Write(right, width - margin - Writer.Width(right, font), top, font, new SKColor(ink, ink, ink));
            top += Writer.Height(font) * 1.35f;
        }
        return (paper, writer.Words);

        SKFont Font(LineStyle style) => style switch { LineStyle.Large => large, LineStyle.Bold => bold, _ => normal };
    }

    private static string Money(decimal value, Language lang) => lang.Locale switch
    {
        "hu" => $"{Grouped(Math.Round(value, 0))} Ft",
        "en" => "£" + value.ToString("0.00", CultureInfo.InvariantCulture),
        _ => value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ','),
    };

    private static string Number(decimal value, int decimals, Language lang)
    {
        var text = value.ToString("0." + new string('0', decimals), CultureInfo.InvariantCulture);
        return lang.Locale == "en" ? text : text.Replace('.', ',');
    }

    private static string Grouped(decimal whole)
    {
        var digits = whole.ToString("0", CultureInfo.InvariantCulture);
        for (var i = digits.Length - 3; i > 0; i -= 3) digits = digits.Insert(i, " ");
        return digits;
    }

    private static string Date(DateOnly date, string locale) => locale switch
    {
        "hu" => date.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
        "en" => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        _ => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
    };

    private static string Address(Random random, string locale) => locale switch
    {
        "hu" => $"{random.Next(1000, 9999)} {Pick(random, ["Budapest", "Szeged", "Győr", "Pécs"])}, {Pick(random, ["Fő utca", "Kossuth u.", "Petőfi út"])} {random.Next(1, 120)}.",
        "en" => $"{random.Next(1, 200)} {Pick(random, ["High Street", "Station Road", "Mill Lane"])}",
        _ => $"{Pick(random, ["Hauptstraße", "Bahnhofstraße", "Lindenweg"])} {random.Next(1, 90)}, {random.Next(10000, 99999)} {Pick(random, ["Graz", "Linz", "Passau"])}",
    };

    private static T Pick<T>(Random random, T[] items) => items[random.Next(items.Length)];
}
