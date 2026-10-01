using TextMagic.Text;

namespace TextMagic.Analysis;

/// <summary>
/// Chooses the 2–4 figures worth a card. Each candidate is a measure with a metric word next to it
/// ("$645.00 … spend", "120,000 clicks"). Candidates are scored on salience, then picked by Maximal
/// Marginal Relevance (Carbonell and Goldstein, 1998), so the cards cover different metrics rather
/// than four versions of spend.
/// </summary>
internal static class KeyFigures
{
    private sealed record Candidate(string Label, string Value, string MetricKey, Trend Trend, double Score, int Order);

    /// <summary>
    /// Cards show the WHOLE, not its parts: aggregate figures from the prose (a figure with no
    /// entity attached: "You spent $645.00", "120,000 clicks in total") and a table's total row.
    /// Per-entity figures belong in the table, where the reader can compare them.
    /// </summary>
    public static IReadOnlyList<KeyFigure> Select(IReadOnlyList<Sentence> sentences, Sentence? headline, int max,
        IReadOnlyList<(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows)>? tables = null)
    {
        var candidates = new List<Candidate>();
        var order = 0;

        foreach (var s in sentences)
        {
            if (s.Role is Role.Caveat or Role.Decision or Role.Definition) continue;
            foreach (var q in s.Measures)
            {
                if (InParentheses(s.Text, q)) continue; // workings, "($95.00 ÷ 55,000)"
                if (EntityFor(s, q) is not null) continue;
                var metric = MetricFor(s, q);
                if (metric is null) continue;
                var score = 0.25 * s.Salience + (ReferenceEquals(s, headline) ? 0.4 : 0) + (s.IsTotal ? 0.15 : 0)
                          + (q.Unit is QuantityUnit.Currency or QuantityUnit.Percent or QuantityUnit.Multiplier ? 0.1 : 0);
                candidates.Add(new Candidate(metric.Label, q.Raw, metric.Label, TrendOf(s, q), score, order++));
            }
        }

        // A table's total row: one card per figure column.
        foreach (var (columns, rows) in tables ?? Array.Empty<(IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string>>)>())
        {
            var total = rows.FirstOrDefault(r => r.Count > 0 && TotalRow.IsMatch(r[0].Replace("**", "").Trim()));
            if (total is null) continue;
            for (var c = 1; c < Math.Min(columns.Count, total.Count); c++)
            {
                var value = total[c].Replace("**", "").Trim();
                if (QuantityRecognizer.Find(value).Count(x => x.IsMeasure) != 1) continue;
                var label = HeaderLabel(columns[c]);
                if (label.Length == 0) continue;
                candidates.Add(new Candidate(label, value, label.ToLowerInvariant(), Trend.None, 0.55, order++));
            }
        }

        // MMR over metric kind, so the cards cover different measures.
        const double lambda = 0.7;
        var chosen = new List<Candidate>();
        while (chosen.Count < max)
        {
            var next = candidates
                .Where(c => !chosen.Any(x => Same(x.MetricKey, c.MetricKey) || x.Value == c.Value))
                .Select(c => (c, mmr: lambda * c.Score - (1 - lambda) * chosen.Select(x => Related(x.MetricKey, c.MetricKey) ? 0.6 : 0).DefaultIfEmpty(0).Max()))
                .OrderByDescending(t => t.mmr).ThenBy(t => t.c.Order)
                .Select(t => t.c).FirstOrDefault();
            if (next is null) break;
            chosen.Add(next);
        }
        if (chosen.Count < 2) return Array.Empty<KeyFigure>(); // one figure is just the headline again

        return chosen.OrderBy(c => c.Order).Select(c => new KeyFigure(c.Label, c.Value, null, c.Trend)).ToList();
    }

    private static readonly System.Text.RegularExpressions.Regex TotalRow =
        new(@"^(total|all|overall|all channels|all three|combined|sum|grand total)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>"Spend (USD)" → "Spend".</summary>
    private static string HeaderLabel(string header)
        => System.Text.RegularExpressions.Regex.Replace(header.Replace("**", ""), @"\s*\((USD|BDT|EUR|GBP|AED|\$|%)\)\s*$", "").Trim();

    private static bool Same(string a, string b) => string.Equals(Norm(a), Norm(b), StringComparison.Ordinal);
    private static bool Related(string a, string b) => Norm(a).Contains(Norm(b)) || Norm(b).Contains(Norm(a));
    private static string Norm(string s) => s.ToLowerInvariant().Replace("-", " ").Replace("reported", "").Replace("  ", " ").Trim();

    private static LexiconMetric? MetricFor(Sentence s, Quantity q)
    {
        // A metric word just before the figure ("spend of $645.00", "CPC: $0.0017") or just after it
        // ("120,000 clicks"), within the same clause.
        var after = s.Metrics.Where(m => m.Start >= q.End && m.Start - q.End <= 3 && Compatible(m.Value, q)).OrderBy(m => m.Start).FirstOrDefault();
        if (after is not null) return after.Value;
        // A bare count names its metric after it ("120,000 clicks"); "(Promo) 2" is not a count of engagements.
        if (q.Unit == QuantityUnit.Number) return null;
        var before = s.Metrics.Where(m => m.End <= q.Start && q.Start - m.End <= 14 && Compatible(m.Value, q) && !Between(s.Text, m.End, q.Start, ',', ';', '.', '('))
            .OrderByDescending(m => m.End).FirstOrDefault();
        if (before is not null) return before.Value;
        // A sentence about exactly one metric owns its money figures ("You spent $645.00 …").
        var only = s.Metrics.Select(m => m.Value).Distinct().ToList();
        return only.Count == 1 && Compatible(only[0], q) ? only[0] : null;
    }

    private static bool Compatible(LexiconMetric m, Quantity q) => m.Unit switch
    {
        MetricUnit.Money => q.Unit == QuantityUnit.Currency,
        MetricUnit.Percent => q.Unit is QuantityUnit.Percent or QuantityUnit.PercentagePoints,
        MetricUnit.Ratio => q.Unit is QuantityUnit.Multiplier or QuantityUnit.Number,
        MetricUnit.Count => q.Unit == QuantityUnit.Number,
        _ => true,
    };

    private static LexiconEntity? EntityFor(Sentence s, Quantity q)
    {
        // Any entity in the same clause owns the figure, however far apart ("Facebook bought over half
        // of all impressions but at a 1.30% CTR").
        var near = s.Entities.Where(e => !Between(s.Text, Math.Min(e.End, q.End), Math.Max(e.Start, q.Start), ',', ';', '—'))
            .OrderBy(e => Math.Abs(e.Start - q.Start)).FirstOrDefault();
        return near?.Value;
    }

    private static Trend TrendOf(Sentence s, Quantity q)
    {
        var raw = q.Raw.TrimStart();
        if (raw.StartsWith('+')) return Trend.Up;
        if (raw.StartsWith('-') || raw.StartsWith('−')) return Trend.Down;
        return s.Direction;
    }

    private static bool InParentheses(string text, Quantity q)
    {
        var open = text.LastIndexOf('(', Math.Max(0, Math.Min(q.Start, text.Length - 1)));
        if (open < 0) return false;
        var close = text.IndexOf(')', open);
        return close > q.Start;
    }

    private static bool Between(string text, int from, int to, params char[] marks)
    {
        if (to <= from || from < 0 || to > text.Length) return false;
        return text.AsSpan(from, to - from).IndexOfAny(marks) >= 0;
    }
}
