using System.Text.Json.Nodes;
using Tankstat.Domain.Recognition;
using Tankstat.Infrastructure.Recognition;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>What a model is told: the contract names every kind and field it may answer with, and the schema is one strict mode accepts.</summary>
public class OpenAiCompatiblePromptTests
{
    private static readonly HashSet<DocumentKind> Refuelling = [DocumentKind.Odometer, DocumentKind.FuelReceipt];
    private static readonly HashSet<DocumentKind> Expense = [DocumentKind.Odometer, DocumentKind.ExpenseReceipt];

    [Fact]
    public void TheContract_NamesWhatThePhotoMayShow_ItsFields_AndHowTheLanguageWritesNumbers()
    {
        var refuelling = OpenAiCompatiblePrompt.Contract(Refuelling, "hu");
        var expense = OpenAiCompatiblePrompt.Contract(Expense, "de");
        var unknownLanguage = OpenAiCompatiblePrompt.Contract(Expense, "fr");

        Assert.Contains("a refuelling entry", refuelling);
        Assert.Contains("fuel-receipt: total, volume, unitPrice, currency, date.", refuelling);
        Assert.DoesNotContain("expense-receipt", refuelling);
        Assert.Contains("Hungarian", refuelling);
        Assert.Contains("an expense entry", expense);
        Assert.Contains("expense-receipt: total, currency, date, title.", expense);
        Assert.Contains("German", expense);
        Assert.DoesNotContain("probably", unknownLanguage); // nothing known about how that language writes
        Assert.Contains("JSON", unknownLanguage); // what OpenAI's json_object mode wants to see in the prompt
    }

    [Fact]
    public void TheSchema_IsOneStrictModeTakes_EveryPropertyRequiredAndNothingElseAllowed()
    {
        var schema = OpenAiCompatiblePrompt.Schema(Expense);

        var objects = new List<JsonObject>();
        void Walk(JsonNode? node)
        {
            if (node is JsonObject o)
            {
                if (o["type"]?.GetValue<string>() == "object") objects.Add(o);
                foreach (var (_, child) in o) Walk(child);
            }
            else if (node is JsonArray a) foreach (var child in a) Walk(child);
        }
        Walk(schema);

        Assert.Equal(2, objects.Count);
        foreach (var o in objects)
        {
            Assert.False(o["additionalProperties"]!.GetValue<bool>());
            Assert.Equal(o["properties"]!.AsObject().Select(p => p.Key).Order(), o["required"]!.AsArray().Select(r => r!.GetValue<string>()).Order());
        }
        Assert.Equal(["odometer", "expense-receipt", "unknown"], schema["properties"]!["kind"]!["enum"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.Equal(["odometer", "total", "currency", "date", "title"], schema["properties"]!["fields"]!["items"]!["properties"]!["name"]!["enum"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.DoesNotContain("minimum", schema.ToJsonString()); // bounds and patterns are not portable across servers
    }

    [Fact]
    public void TheReadme_PrintsTheBuiltInPrompt_SoAnOperatorsOwnCanStartFromIt()
    {
        var readme = File.ReadAllLines(Path.Combine(RepoRoot(), "README.md"));
        var start = Array.FindIndex(readme, line => line.Contains("The built-in system prompt is:", StringComparison.Ordinal)) + 1;
        Assert.True(start > 0, "the README no longer says where the built-in prompt is");
        var quoted = readme.Skip(start).SkipWhile(string.IsNullOrWhiteSpace).TakeWhile(line => line.StartsWith('>')).Select(line => line.TrimStart('>').Trim());

        Assert.Equal(
            OpenAiCompatiblePrompt.DefaultSystemPrompt.ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()),
            quoted);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tankstat.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Tankstat.slnx not found above the test output.");
    }
}
