using System.Text;
using System.Text.RegularExpressions;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// An operation exactly as the web app sends it (#320): taken by name from the frontend's documents
/// (<c>src/frontend/tankstat-react/src/graphql/*.graphql</c>) with every fragment it spreads, so a test measures the page's own query, never
/// a shape of it that drifts (#311's test passed on a shape the home page never sent).
/// </summary>
internal static partial class FrontendOperations
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Definitions = new(Read);

    /// <summary>The operation <paramref name="name"/> and the fragments it uses, as one document.</summary>
    public static string Document(string name)
    {
        var definitions = Definitions.Value;
        if (!definitions.TryGetValue(name, out var operation)) throw new InvalidOperationException($"No operation or fragment {name} in the frontend's documents.");
        var document = new StringBuilder(operation);
        var seen = new HashSet<string> { name };
        var pending = new Queue<string>(Spreads(operation));
        while (pending.TryDequeue(out var fragment))
        {
            if (!seen.Add(fragment)) continue;
            var text = definitions[fragment];
            document.Append('\n').Append(text);
            foreach (var next in Spreads(text)) pending.Enqueue(next);
        }
        return document.ToString();
    }

    private static IEnumerable<string> Spreads(string definition) => SpreadPattern().Matches(definition).Select(m => m.Groups[1].Value);

    /// <summary>Every top-level definition of every document by its name, split at the braces (comments taken out first).</summary>
    private static IReadOnlyDictionary<string, string> Read()
    {
        var folder = Path.Combine(RepositoryRoot(), "src", "frontend", "tankstat-react", "src", "graphql");
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(folder, "*.graphql"))
        {
            var text = CommentPattern().Replace(File.ReadAllText(file), "");
            var (depth, start) = (0, -1);
            for (var i = 0; i < text.Length; i++)
            {
                if (depth == 0 && start < 0 && !char.IsWhiteSpace(text[i])) start = i;
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0)
                {
                    var definition = text[start..(i + 1)];
                    definitions[HeadPattern().Match(definition).Groups[1].Value] = definition;
                    start = -1;
                }
            }
        }
        return definitions;
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tankstat.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root (Tankstat.slnx) was not found.");
    }

    [GeneratedRegex(@"#[^\n]*")]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"^\s*(?:query|mutation|subscription|fragment)\s+(\w+)")]
    private static partial Regex HeadPattern();

    [GeneratedRegex(@"\.\.\.\s*(?!on\s)(\w+)")]
    private static partial Regex SpreadPattern();
}
