using TextMagic.Text;

namespace TextMagic.Analysis;

/// <summary>
/// Gives each sentence a rhetorical role from surface cues, in the manner of argumentative zoning
/// (Teufel): cue phrases for caveats, decision notes and definitions, explicit discourse
/// connectives (PDTB) for contrast, and lexical features (figures, entities, comparatives) for the
/// rest. Scores rather than first-match, so a sentence with a figure and a passing "only" stays a
/// figure unless the caveat evidence is the stronger.
/// </summary>
internal static class RoleClassifier
{
    private static readonly string[] Comparatives =
    {
        "than", "compared", "versus", " vs ", "ahead of", "behind", "the most", "the least", "highest", "lowest",
        "followed by", "then ", "whereas", "while", "more ", "less ", "fewer", "larger", "smaller", "cheapest",
        "most expensive", "best", "worst", "top", "bottom",
    };

    private static readonly string[] ContextOpeners =
    {
        "that's", "that is", "this means", "which is", "in other words", "put simply", "for context", "about half",
        "roughly", "almost", "nearly",
    };

    public static void Classify(IReadOnlyList<Sentence> sentences, Lexicon lexicon)
    {
        var caveat = new PhraseMatcher<bool>(lexicon.CaveatCues.Select(c => (c, true)));
        var warning = new PhraseMatcher<bool>(lexicon.WarningCues.Select(c => (c, true)));
        var decision = new PhraseMatcher<bool>(lexicon.DecisionCues.Select(c => (c, true)));
        var definition = new PhraseMatcher<bool>(lexicon.DefinitionCues.Select(c => (c, true)));

        foreach (var s in sentences)
        {
            var lower = " " + s.Text.ToLowerInvariant() + " ";
            var measures = s.Measures.Count();

            var scores = new Dictionary<Role, double>
            {
                [Role.Decision] = decision.FindAll(lower).Count * 3.0,
                [Role.Caveat] = caveat.FindAll(lower).Count * 1.2
                                + (lower.TrimStart().StartsWith("note", StringComparison.Ordinal) || lower.Contains("caveat") ? 2.0 : 0)
                                + (s.HasContrast ? 0.6 : 0)
                                - Math.Min(measures, 4) * 0.15,
                [Role.Definition] = definition.FindAll(lower).Count * 1.5 + (lower.Contains(" = ") ? 1.0 : 0),
                [Role.Comparison] = Comparatives.Count(c => lower.Contains(c, StringComparison.Ordinal)) * 0.6
                                    + (s.Entities.Count >= 2 ? 0.8 : 0) + (measures >= 2 ? 0.4 : 0),
                [Role.Figure] = measures > 0 ? 1.0 + Math.Min(measures, 4) * 0.25 : 0,
                [Role.Context] = ContextOpeners.Count(c => lower.Contains(" " + c, StringComparison.Ordinal)) * 0.9,
            };

            var best = scores.OrderByDescending(kv => kv.Value).First();
            s.Role = best.Value >= 1.0 ? best.Key : Role.Other;
            // An intro ("A few things stand out:") belongs with what it introduces, never in a callout.
            if (IsIntro(s) && s.Role is Role.Caveat or Role.Decision && !lower.Contains("caveat")) s.Role = Role.Other;
            if (s.Role == Role.Caveat) s.Warning = warning.FindAll(lower).Count > 0;
        }

        // A caveat's continuation is part of the caveat: the next sentence of the same paragraph
        // when it opens by referring back ("And …", "This …") or carries a contrast connective.
        for (var i = 1; i < sentences.Count; i++)
        {
            var prev = sentences[i - 1];
            var s = sentences[i];
            if (prev.Role != Role.Caveat || s.Paragraph != prev.Paragraph || s.Role is Role.Decision or Role.Caveat) continue;
            var opener = ContinuationOpener.IsMatch(s.Text.TrimStart('*', ' '));
            // A label-style lead ("One gap worth flagging: headroom.") is completed by the next sentence.
            var shortLead = prev.Text.Contains(':') && Salience.Tokens(prev.Text).Count <= 6;
            if (opener || shortLead || (s.HasContrast && s.Role != Role.Comparison))
            {
                s.Role = Role.Caveat;
                s.Warning = prev.Warning;
            }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex ContinuationOpener =
        new(@"^(and|also|this|that|these|those|so|which|it|they)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>A sentence that introduces what follows ("Last 30 days, USD:") is never the answer.</summary>
    public static bool IsIntro(Sentence s) => s.Text.TrimEnd().EndsWith(':');
}

/// <summary>
/// Sentence salience: which sentence is "the answer". A weighted blend of four classic signals:
///
///  * position: writers lead with the point (the lead bias behind Lead-3 baselines);
///  * overlap with the question (when one is given), over TF-IDF vectors;
///  * centrality: LexRank (Erkan and Radev, 2004), eigenvector centrality over the cosine
///    similarity graph of sentences, computed by power iteration;
///  * figure density: an answer to an analytics question carries a figure.
/// </summary>
internal static class Salience
{
    public static void Score(IReadOnlyList<Sentence> sentences, string? question)
    {
        if (sentences.Count == 0) return;

        var docs = sentences.Select(s => Tokens(s.Text)).ToList();
        var idf = Idf(docs.Append(Tokens(question ?? "")).ToList());
        var vectors = docs.Select(d => Vector(d, idf)).ToList();
        var q = string.IsNullOrWhiteSpace(question) ? null : Vector(Tokens(question!), idf);
        var centrality = LexRank(vectors);
        var maxC = centrality.DefaultIfEmpty(0).Max();

        for (var i = 0; i < sentences.Count; i++)
        {
            var s = sentences[i];
            var position = 1.0 / (1 + i);
            var overlap = q is null ? 0 : Cosine(vectors[i], q);
            var central = maxC > 0 ? centrality[i] / maxC : 0;
            var figures = s.Measures.Any() ? 1.0 : 0.0;
            s.Salience = q is null
                ? 0.45 * position + 0.35 * central + 0.20 * figures
                : 0.30 * position + 0.35 * overlap + 0.15 * central + 0.20 * figures;
        }
    }

    internal static double[] LexRank(IReadOnlyList<Dictionary<string, double>> vectors, double threshold = 0.1, double damping = 0.85)
    {
        var n = vectors.Count;
        if (n == 0) return Array.Empty<double>();
        var adj = new double[n, n];
        var degree = new double[n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                if (i != j && Cosine(vectors[i], vectors[j]) > threshold) { adj[i, j] = 1; degree[i]++; }

        var p = Enumerable.Repeat(1.0 / n, n).ToArray();
        for (var iter = 0; iter < 50; iter++)
        {
            var next = new double[n];
            for (var j = 0; j < n; j++)
            {
                double sum = 0;
                for (var i = 0; i < n; i++) if (adj[i, j] > 0) sum += p[i] / degree[i];
                next[j] = (1 - damping) / n + damping * sum;
            }
            var delta = next.Zip(p, (a, b) => Math.Abs(a - b)).Sum();
            p = next;
            if (delta < 1e-6) break;
        }
        return p;
    }

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "and", "or", "of", "to", "in", "on", "for", "at", "by", "with", "from", "is", "are", "was",
        "were", "be", "been", "it", "its", "this", "that", "these", "those", "as", "your", "you", "we", "our", "over",
        "about", "how", "what", "which", "who", "did", "do", "does", "has", "have", "had", "so", "but", "not", "no",
        "per", "than", "then", "there", "their", "they", "i", "me", "my", "can", "could", "would", "should", "will",
    };

    internal static List<string> Tokens(string text)
        => System.Text.RegularExpressions.Regex.Split(text.ToLowerInvariant(), @"[^a-z0-9%$]+")
            .Where(t => t.Length > 1 && !Stop.Contains(t) && !t.All(char.IsDigit))
            .Select(t => t.Length > 4 && t.EndsWith('s') ? t[..^1] : t)
            .ToList();

    private static Dictionary<string, double> Idf(IReadOnlyList<List<string>> docs)
    {
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var d in docs) foreach (var t in d.Distinct()) df[t] = df.GetValueOrDefault(t) + 1;
        return df.ToDictionary(kv => kv.Key, kv => Math.Log((1.0 + docs.Count) / (1.0 + kv.Value)) + 1.0);
    }

    private static Dictionary<string, double> Vector(List<string> tokens, Dictionary<string, double> idf)
        => tokens.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count() * idf.GetValueOrDefault(g.Key, 1.0));

    internal static double Cosine(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        double dot = 0;
        foreach (var (k, v) in a) if (b.TryGetValue(k, out var w)) dot += v * w;
        var na = Math.Sqrt(a.Values.Sum(v => v * v));
        var nb = Math.Sqrt(b.Values.Sum(v => v * v));
        return na == 0 || nb == 0 ? 0 : dot / (na * nb);
    }
}
