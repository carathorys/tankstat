using System.Text.Json;

namespace Tankstat.Reader.Core;

/// <summary>What a photo can show. The names are the reader's contract (v1) with its callers.</summary>
public static class DocumentKinds
{
    public const string Odometer = "odometer";
    public const string FuelReceipt = "fuel-receipt";
    public const string ExpenseReceipt = "expense-receipt";
    public const string Unknown = "unknown";

    public static IReadOnlyList<string> All { get; } = [Odometer, FuelReceipt, ExpenseReceipt];
}

/// <summary>The values a reading can contain, named the same for every document kind and every provider.</summary>
public static class FieldNames
{
    public const string Odometer = "odometer";
    public const string Total = "total";
    public const string Volume = "volume";
    public const string UnitPrice = "unitPrice";
    public const string Currency = "currency";
    public const string Date = "date";
    public const string Title = "title";
}

/// <summary>Where a value came from: read from the photo, worked out from other values (volume × unit price), or the caller's hint.</summary>
public static class FieldSources
{
    public const string Ocr = "ocr";
    public const string Derived = "derived";
    public const string Hint = "hint";
}

/// <summary>What the caller knows that helps reading: the user's language, the vehicle's latest odometer, the latest currency, today.</summary>
/// <param name="Locale">hu, en or de: decides how ambiguous numbers ("24.669") and dates ("02/03/2026") are read.</param>
public sealed record ReadHints(string Locale, long? LastOdometer, string? Currency, DateOnly Today)
{
    public static string NormalizeLocale(string? locale) =>
        locale?.Trim().ToLowerInvariant() switch
        {
            { } l when l.StartsWith("hu", StringComparison.Ordinal) => "hu",
            { } l when l.StartsWith("de", StringComparison.Ordinal) => "de",
            _ => "en",
        };
}

/// <param name="Kinds">The kinds the caller's form accepts (a refueling form: odometer and fuel receipt).</param>
public sealed record ReadRequest(IReadOnlySet<string> Kinds, ReadHints Hints);

/// <summary>One value read from the photo, as an invariant string ("38.52", "2026-09-17", "HUF", "123456"), with a confidence from 0 to 1.</summary>
public sealed record ReadField(string Name, string Value, double Confidence, string Source);

/// <summary>What the photo shows and the values read from it; an unreadable photo is kind "unknown" with no fields, never an error.</summary>
public sealed record ReadResult(string ModelVersion, string Kind, IReadOnlyList<ReadField> Fields)
{
    public string? Value(string field) => Fields.FirstOrDefault(f => f.Name == field)?.Value;
}

/// <summary>How the contract is written as JSON (camelCase, like ASP.NET Core's web defaults): one place for the host, tools and tests.</summary>
public static class ReaderJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
