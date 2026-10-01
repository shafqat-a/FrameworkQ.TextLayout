namespace TextMagic.Text;

/// <summary>
/// Splits a paragraph into sentences. Rules in the spirit of Punkt (Kiss and Strunk, 2006): a
/// terminal mark ends a sentence only when what follows looks like a new one, and never inside a
/// number ($1.5M, 0.0048, 2026-09-26), an abbreviation (e.g., vs., approx.) or brackets.
/// </summary>
internal static class SentenceSegmenter
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "e.g", "i.e", "etc", "vs", "approx", "inc", "ltd", "co", "mr", "mrs", "ms", "dr", "no", "fig", "est",
        "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep", "sept", "oct", "nov", "dec", "u.s", "a.m", "p.m",
    };

    public static IReadOnlyList<string> Split(string paragraph)
    {
        var text = paragraph.Replace("\r", "").Trim();
        var result = new List<string>();
        if (text.Length == 0) return result;

        var start = 0;
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth = Math.Max(0, depth - 1);

            // A line break inside a paragraph ends a sentence when the next line starts a new one
            // (lists of short lines written without markdown bullets).
            if (c == '\n')
            {
                var nextLine = NextNonSpace(text, i + 1);
                if (nextLine >= 0 && depth == 0 && (char.IsUpper(text[nextLine]) || text[nextLine] is '*' or '-' or '•'))
                {
                    Add(result, text[start..i]);
                    start = i + 1;
                }
                continue;
            }

            if (c is not ('.' or '!' or '?' or ':')) continue;
            if (depth > 0) continue;

            // Decimals and version-like tokens: 0.0048, 3.5x.
            if (c == '.' && i > 0 && char.IsDigit(text[i - 1]) && i + 1 < text.Length && char.IsDigit(text[i + 1])) continue;
            // Ellipsis.
            if (c == '.' && i + 1 < text.Length && text[i + 1] == '.') continue;
            // A colon ends a sentence only before a line break (an intro to a list or table).
            if (c == ':' && !(i + 1 < text.Length && text[i + 1] == '\n')) continue;

            var next = NextNonSpace(text, i + 1);
            if (next < 0) break; // the final mark: the rest is one sentence
            if (next == i + 1 && c != ':') continue; // no space after: "U.S.", "3.5", "e.g."

            var ch = text[next];
            var startsSentence = char.IsUpper(ch) || char.IsDigit(ch) || ch is '"' or '\'' or '“' or '*' or '(' or '$' or '-' or '•' or '£' or '€';
            if (!startsSentence) continue;

            if (c == '.' && IsAbbreviation(text, i)) continue;

            Add(result, text[start..(i + 1)]);
            start = i + 1;
        }
        Add(result, text[start..]);
        return result;
    }

    private static bool IsAbbreviation(string text, int dot)
    {
        var j = dot - 1;
        while (j >= 0 && (char.IsLetter(text[j]) || text[j] == '.')) j--;
        var word = text[(j + 1)..dot];
        // A single capital ("J. Smith") or a known short form.
        return (word.Length == 1 && char.IsUpper(word[0])) || Abbreviations.Contains(word);
    }

    private static int NextNonSpace(string s, int from)
    {
        for (var k = from; k < s.Length; k++)
            if (!char.IsWhiteSpace(s[k])) return k;
        return -1;
    }

    private static void Add(List<string> into, string s)
    {
        var t = s.Trim();
        if (t.Length > 0) into.Add(t);
    }
}
