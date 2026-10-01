using System.Globalization;
using System.Text;
using Tankstat.Domain;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Imports;

/// <summary>
/// Fuelio's "sync" CSV export of one vehicle. The file is several CSV sections, each introduced by a line such as <c>"## Log"</c>
/// followed by its own header line; columns are found by their header names, so extra or reordered columns do no harm.
/// <list type="bullet">
/// <item><c>## Vehicle</c>: name, plate, units (<c>DistUnit</c> 0 km / 1 miles, <c>FuelUnit</c> 0 litres / 1 US gal / 2 imperial gal) and the tank's fuel type.</item>
/// <item><c>## Log</c>: the fill-ups. <c>Price</c> is the total paid; the currency is not in the file.</item>
/// <item><c>## CostCategories</c> and <c>## Costs</c>: other expenses. Rows marked as templates (reminders) and income rows are skipped; odometer 0 means "not noted".</item>
/// <item>Everything else (stations, trip categories, GPS, weather) is ignored.</item>
/// </list>
/// </summary>
public sealed class FuelioCsvParser : IImportParser
{
    public const string FormatId = "fuelio";

    public string Format => FormatId;

    public ImportBatch Parse(Stream content)
    {
        var sections = ReadSections(content);
        if (!sections.ContainsKey("Log") && !sections.ContainsKey("Costs"))
            throw new DomainException("import.unreadable", "This does not look like a Fuelio export (no Log or Costs section).", new { Format = FormatId });

        var issues = new List<ImportIssue>();
        var vehicle = ReadVehicle(sections);
        var categories = ReadCategories(sections);
        var fuelLogs = ReadLog(sections, issues);
        var expenses = ReadCosts(sections, categories, issues);
        return new ImportBatch(FormatId, vehicle, fuelLogs, expenses, issues);
    }

    // ---- sections -----------------------------------------------------------------------------------------------

    private sealed record Section(IReadOnlyList<string> Header, List<IReadOnlyList<string>> Rows)
    {
        /// <summary>The column named <paramref name="name"/>, also when Fuelio adds a unit or hint: "Odo (km)", "Price (optional)".</summary>
        public int Index(string name) => Header.ToList().FindIndex(h =>
            h.Equals(name, StringComparison.OrdinalIgnoreCase) || h.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase));

        public string? Get(IReadOnlyList<string> row, string column)
        {
            var i = Index(column);
            return i >= 0 && i < row.Count && row[i].Length > 0 ? row[i] : null;
        }
    }

    private static Dictionary<string, Section> ReadSections(Stream content)
    {
        var sections = new Dictionary<string, Section>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StreamReader(content, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        Section? current = null;
        var expectHeader = false;
        string? name = null;
        foreach (var row in CsvReader.Read(reader))
        {
            if (row.Count == 1 && row[0].StartsWith("## ", StringComparison.Ordinal))
            {
                name = row[0][3..].Trim();
                expectHeader = true;
                continue;
            }
            if (name is null) continue;

            if (expectHeader)
            {
                current = new Section(row, []);
                sections[name] = current;
                expectHeader = false;
            }
            else current?.Rows.Add(row);
        }
        return sections;
    }

    // ---- vehicle ------------------------------------------------------------------------------------------------

    private static ImportedVehicle? ReadVehicle(Dictionary<string, Section> sections)
    {
        if (!sections.TryGetValue("Vehicle", out var s) || s.Rows.Count == 0) return null;
        var row = s.Rows[0];

        var distance = s.Get(row, "DistUnit") switch { "1" => DistanceUnit.Miles, "0" => DistanceUnit.Kilometers, _ => (DistanceUnit?)null };
        var volume = s.Get(row, "FuelUnit") switch
        {
            "0" => VolumeUnit.Liters,
            "1" => VolumeUnit.UsGallons,
            "2" => VolumeUnit.ImperialGallons,
            _ => (VolumeUnit?)null,
        };
        // Fuelio's tank types are grouped by hundreds: 1xx petrol, 2xx diesel, 3xx LPG. Anything else is left for the user to choose.
        FuelType? fuel = null;
        if (int.TryParse(s.Get(row, "Tank1Type"), CultureInfo.InvariantCulture, out var tank))
            fuel = (tank / 100) switch { 1 => FuelType.Petrol, 2 => FuelType.Diesel, 3 => FuelType.Lpg, _ => null };
        return new ImportedVehicle(s.Get(row, "Name"), s.Get(row, "Plate"), fuel, distance, volume);
    }

    private static Dictionary<string, string> ReadCategories(Dictionary<string, Section> sections)
    {
        var map = new Dictionary<string, string>();
        if (!sections.TryGetValue("CostCategories", out var s)) return map;
        foreach (var row in s.Rows)
            if (s.Get(row, "CostTypeID") is { } id && s.Get(row, "Name") is { } name) map[id] = name;
        return map;
    }

    // ---- fuel logs ----------------------------------------------------------------------------------------------

    private static List<ImportedFuelLog> ReadLog(Dictionary<string, Section> sections, List<ImportIssue> issues)
    {
        var logs = new List<ImportedFuelLog>();
        if (!sections.TryGetValue("Log", out var s)) return logs;

        for (var i = 0; i < s.Rows.Count; i++)
        {
            var row = s.Rows[i];
            var n = i + 1;
            if (!TryDate(s.Get(row, "Data"), out var date)) { Skip(issues, "log", n, "import.badDate", s.Get(row, "Data")); continue; }
            if (!TryDecimal(s.Get(row, "Odo"), out var odometer) || odometer < 0) { Skip(issues, "log", n, "import.badNumber", s.Get(row, "Odo"), "odometer"); continue; }
            if (!TryDecimal(s.Get(row, "Fuel"), out var volume) || volume <= 0) { Skip(issues, "log", n, "import.badNumber", s.Get(row, "Fuel"), "volume"); continue; }
            if (!TryDecimal(s.Get(row, "Price"), out var price) || price < 0) { Skip(issues, "log", n, "import.badNumber", s.Get(row, "Price"), "price"); continue; }

            logs.Add(new ImportedFuelLog(n, date, (long)Math.Round(odometer), volume, price, s.Get(row, "Full") == "1", s.Get(row, "Notes")));
        }
        return logs;
    }

    // ---- other costs --------------------------------------------------------------------------------------------

    private static List<ImportedExpense> ReadCosts(Dictionary<string, Section> sections, Dictionary<string, string> categories, List<ImportIssue> issues)
    {
        var expenses = new List<ImportedExpense>();
        if (!sections.TryGetValue("Costs", out var s)) return expenses;

        for (var i = 0; i < s.Rows.Count; i++)
        {
            var row = s.Rows[i];
            var n = i + 1;
            if (s.Get(row, "isTemplate") == "1") { issues.Add(new ImportIssue("costs", n, "import.templateSkipped", new Dictionary<string, object?> { ["title"] = s.Get(row, "CostTitle") })); continue; }
            if (s.Get(row, "isIncome") == "1") { issues.Add(new ImportIssue("costs", n, "import.incomeSkipped", new Dictionary<string, object?> { ["title"] = s.Get(row, "CostTitle") })); continue; }
            if (!TryDate(s.Get(row, "Date"), out var date)) { Skip(issues, "costs", n, "import.badDate", s.Get(row, "Date")); continue; }
            if (!TryDecimal(s.Get(row, "Cost"), out var amount) || amount < 0) { Skip(issues, "costs", n, "import.badNumber", s.Get(row, "Cost"), "cost"); continue; }

            var title = s.Get(row, "CostTitle") ?? "";
            var category = s.Get(row, "CostTypeID") is { } id && categories.TryGetValue(id, out var name) ? name : null;
            // A title is required; fall back to the category so a row that only has a category is not lost.
            if (title.Length == 0) title = category ?? "";
            if (title.Length == 0) { Skip(issues, "costs", n, "import.titleMissing", null); continue; }

            long? odometer = TryDecimal(s.Get(row, "Odo"), out var odo) && odo > 0 ? (long)Math.Round(odo) : null; // 0 means "not noted"
            expenses.Add(new ImportedExpense(n, date, Trim(title, Expense.MaxTitleLength), category, amount, odometer, s.Get(row, "Notes")));
        }
        return expenses;
    }

    // ---- values -------------------------------------------------------------------------------------------------

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max];

    private static void Skip(List<ImportIssue> issues, string section, int row, string key, string? value, string? field = null) =>
        issues.Add(new ImportIssue(section, row, key, new Dictionary<string, object?> { ["value"] = value ?? "", ["field"] = field ?? "" }));

    /// <summary>"2026-09-17 18:15", "2026-04-10" (Fuelio writes the date format named in the vehicle section, ISO by default). Only the date is kept.</summary>
    private static bool TryDate(string? text, out DateOnly date)
    {
        date = default;
        if (text is null) return false;
        var datePart = text.Trim().Split(' ', 'T')[0];
        return DateOnly.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryDecimal(string? text, out decimal value)
    {
        value = 0;
        return text is not null && decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
