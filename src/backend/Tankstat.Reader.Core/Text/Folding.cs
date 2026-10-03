using System.Globalization;
using System.Text;

namespace Tankstat.Reader.Core.Text;

/// <summary>Text compared the way receipts need it: lower case and without accents, so "ÖSSZESEN:" matches "osszesen".</summary>
public static class Folding
{
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch == 'ß') folded.Append("ss");
            else folded.Append(char.ToLowerInvariant(ch));
        }
        return folded.ToString();
    }

    /// <summary>The words of a folded line: runs of letters (digits and signs split them).</summary>
    public static IEnumerable<string> Words(string folded)
    {
        var start = -1;
        for (var i = 0; i <= folded.Length; i++)
        {
            var letter = i < folded.Length && char.IsLetter(folded[i]);
            if (letter && start < 0) start = i;
            else if (!letter && start >= 0)
            {
                yield return folded[start..i];
                start = -1;
            }
        }
    }

    /// <summary>Edit distance, giving up (returning <paramref name="limit"/> + 1) once it is clear it is larger than the limit.</summary>
    public static int Distance(string a, string b, int limit)
    {
        if (Math.Abs(a.Length - b.Length) > limit) return limit + 1;
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }
            if (rowMin > limit) return limit + 1;
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
