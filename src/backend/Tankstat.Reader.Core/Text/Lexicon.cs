using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tankstat.Reader.Core.Text;

/// <summary>
/// The words receipts use, per language (embedded <c>Lexicons/*.json</c>, already folded: lower case without accents). All languages are
/// used together, because a Hungarian user may well photograph a receipt from Austria; the locale only decides how ambiguous numbers
/// and dates are read. Single words match with a typo or two (OCR misreads), phrases and words with digits or signs match exactly.
/// </summary>
public sealed class Lexicon
{
    public static IReadOnlyList<string> Languages { get; } = ["hu", "en", "de"];

    public static Lexicon Default { get; } = Load(Languages);

    private readonly (Keyword Word, double Weight)[] _total, _notTotal;
    private readonly Keyword[] _fuel, _unitPrice, _date, _titleNoise, _odometer, _trip, _countdown;

    private Lexicon(IEnumerable<LexiconFile> files)
    {
        var list = files.ToList();
        _total = Merge(list.Select(f => f.Total));
        _notTotal = Merge(list.Select(f => f.NotTotal));
        _fuel = Words(list.SelectMany(f => f.Fuel));
        _unitPrice = Words(list.SelectMany(f => f.UnitPrice));
        _date = Words(list.SelectMany(f => f.Date));
        _titleNoise = Words(list.SelectMany(f => f.TitleNoise));
        _odometer = Words(list.SelectMany(f => f.Odometer));
        _trip = Words(list.SelectMany(f => f.Trip));
        _countdown = Words(list.SelectMany(f => f.Countdown));
    }

    public static Lexicon Load(IEnumerable<string> languages) => new(languages.Select(LoadFile));

    /// <summary>The strongest "this is the total" keyword on the (folded) line, 0 when there is none.</summary>
    public double TotalWeight(string folded) => Weight(_total, folded);

    /// <summary>The strongest "this is not the total" keyword (subtotal, VAT, change, ...), 0 when there is none.</summary>
    public double NotTotalWeight(string folded) => Weight(_notTotal, folded);

    public bool HasFuelWord(string folded) => Any(_fuel, folded);
    public bool HasUnitPriceWord(string folded) => Any(_unitPrice, folded);
    public bool HasDateWord(string folded) => Any(_date, folded);
    public bool IsTitleNoise(string folded) => Any(_titleNoise, folded);
    public bool HasOdometerWord(string folded) => Any(_odometer, folded);
    public bool HasTripWord(string folded) => Any(_trip, folded);

    /// <summary>A service countdown or a range ("Service in 4800 km", "Szerviz 4800 km múlva"): a distance to go, not the odometer.</summary>
    public bool HasCountdownWord(string folded) => Any(_countdown, folded);

    private static bool Any(Keyword[] words, string folded)
    {
        var tokens = Folding.Words(folded).ToArray();
        return words.Any(k => k.IsIn(folded, tokens));
    }

    private static double Weight((Keyword Word, double Weight)[] words, string folded)
    {
        var tokens = Folding.Words(folded).ToArray();
        return words.Where(k => k.Word.IsIn(folded, tokens)).Select(k => k.Weight).DefaultIfEmpty(0).Max();
    }

    private static Keyword[] Words(IEnumerable<string> words) => words.Distinct().Select(w => new Keyword(w)).ToArray();

    private static (Keyword, double)[] Merge(IEnumerable<Dictionary<string, double>> parts)
    {
        var merged = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var part in parts)
            foreach (var (word, weight) in part)
                merged[word] = Math.Max(weight, merged.GetValueOrDefault(word));
        return merged.Select(kv => (new Keyword(kv.Key), kv.Value)).ToArray();
    }

    /// <summary>
    /// A keyword and how it matches: a single word matches a word of the line, allowing one OCR typo from six letters and two from ten;
    /// a phrase or a word with digits or signs ("zu zahlen", "ft/l", "e10") must appear verbatim, not glued to other letters or digits.
    /// </summary>
    private sealed class Keyword
    {
        private readonly string _text;
        private readonly int _typos;
        private readonly Regex? _phrase;

        public Keyword(string text)
        {
            _text = text;
            _typos = text.Length >= 10 ? 2 : text.Length >= 6 ? 1 : 0;
            if (!text.All(char.IsLetter)) _phrase = new Regex($"(?<![a-z0-9]){Regex.Escape(text)}(?![a-z0-9])", RegexOptions.CultureInvariant);
        }

        public bool IsIn(string folded, string[] tokens) =>
            _phrase?.IsMatch(folded) ?? tokens.Any(t => _typos == 0 ? t == _text : Folding.Distance(t, _text, _typos) <= _typos);
    }

    private static LexiconFile LoadFile(string language)
    {
        var name = $"Tankstat.Reader.Core.Lexicons.{language}.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"There is no lexicon for the language '{language}'.");
        return JsonSerializer.Deserialize<LexiconFile>(stream, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            ?? throw new InvalidOperationException($"The lexicon '{language}' is empty.");
    }

    private sealed class LexiconFile
    {
        public string Language { get; set; } = "";
        public Dictionary<string, double> Total { get; set; } = [];
        public Dictionary<string, double> NotTotal { get; set; } = [];
        public string[] Fuel { get; set; } = [];
        public string[] UnitPrice { get; set; } = [];
        public string[] Date { get; set; } = [];
        public string[] TitleNoise { get; set; } = [];
        public string[] Odometer { get; set; } = [];
        public string[] Trip { get; set; } = [];
        public string[] Countdown { get; set; } = [];
    }
}
