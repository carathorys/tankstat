using System.Text;

namespace Tankstat.Application.Imports;

/// <summary>Reads RFC 4180 CSV: quoted fields, doubled quotes, and line breaks inside quotes. Blank lines are skipped.</summary>
public static class CsvReader
{
    public static IEnumerable<IReadOnlyList<string>> Read(TextReader reader)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var wasQuoted = false;
        int c;
        while ((c = reader.Read()) >= 0)
        {
            var ch = (char)c;
            if (quoted)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                    else quoted = false;
                }
                else field.Append(ch);
                continue;
            }

            switch (ch)
            {
                case '"' when field.Length == 0 && !wasQuoted:
                    quoted = true;
                    wasQuoted = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    wasQuoted = false;
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    wasQuoted = false;
                    if (!(fields.Count == 1 && fields[0].Length == 0)) yield return fields;
                    fields = [];
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            if (!(fields.Count == 1 && fields[0].Length == 0)) yield return fields;
        }
    }
}
