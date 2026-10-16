using System.Text;

namespace Wimf.Search;

/// <summary>Tokenizes name search: whitespace separates words except inside double quotes.</summary>
public static class SearchTerms
{
    public static string[] Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var terms = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var character in text)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (!quoted && char.IsWhiteSpace(character))
            {
                if (current.Length == 0) continue;
                terms.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }
        if (quoted)
            throw new ArgumentException("Unclosed double quote in search text.", nameof(text));
        if (current.Length > 0) terms.Add(current.ToString());
        return terms.ToArray();
    }
}
