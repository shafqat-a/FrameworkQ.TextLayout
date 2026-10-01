using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FrameworkQ.TextLayout.Text;

namespace FrameworkQ.TextLayout.Analysis;

/// <summary>A table the engine built, and the sentences or list items it stands in for.</summary>
internal sealed record InducedTable(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    BlockOrigin Origin,
    IReadOnlySet<int> CoversSentences,
    int AnchorSentence);

internal static class Tables
{
    // ── 1. From the caller's data (text–table quantity alignment, after BriQ) ─────────────────

    /// <summary>
    /// When the text quotes three or more rows of a data table (an entity named in the text, with at
    /// least one of that row's values quoted as a figure), the table is rebuilt from the DATA:
    /// exact values, every row, and only the columns the text actually quotes.
    /// </summary>
    public static InducedTable? FromData(IReadOnlyList<Sentence> sentences, string fullText, DataTable data, Lexicon lexicon, int minRows)
    {
        if (data.Rows.Count < minRows || data.Columns.Count < 2 || data.Rows.Count > 40) return null;

        var textQuantities = sentences.SelectMany(s => s.Measures).ToList();
        var lowerText = " " + Normalise(fullText) + " ";

        // The label column: the text column whose values the text names most often.
        int labelCol = -1, labelHits = 0;
        for (var c = 0; c < data.Columns.Count; c++)
        {
            if (!data.Rows.All(r => c < r.Count && (r[c] is null || r[c] is string))) continue;
            var hits = data.Rows.Count(r => r[c] is string v && v.Trim().Length > 1 && NamedIn(lowerText, v, lexicon));
            if (hits > labelHits) { labelHits = hits; labelCol = c; }
        }
        if (labelCol < 0 || labelHits < Math.Min(minRows, data.Rows.Count)) return null;

        // The value columns: numeric columns with a value the text quotes for at least two named rows.
        var named = data.Rows.Where(r => r[labelCol] is string v && NamedIn(lowerText, v, lexicon)).ToList();
        var valueCols = new List<int>();
        for (var c = 0; c < data.Columns.Count; c++)
        {
            if (c == labelCol || !data.Rows.All(r => c < r.Count && (r[c] is null || ToDecimal(r[c]) is not null))) continue;
            var quoted = named.Count(r => ToDecimal(r[c]) is { } v && textQuantities.Any(q => QuantityRecognizer.SameFigure(v, q)));
            if (quoted >= Math.Min(2, named.Count)) valueCols.Add(c);
        }
        if (valueCols.Count == 0) return null;

        var columns = new List<string> { LabelHeader(data, labelCol, lexicon) };
        columns.AddRange(valueCols.Select(c => Humanise(data.Columns[c], lexicon)));
        var rows = data.Rows.Select(r => (IReadOnlyList<string>)new List<string> { Display(r[labelCol] as string ?? "", lexicon) }
            .Concat(valueCols.Select(c => FormatCell(r[c], data.Columns[c], lexicon))).ToList()).ToList();

        // Sentences that only enumerate these rows' figures are replaced by the table.
        var covered = new HashSet<int>();
        var anchor = -1;
        foreach (var s in sentences)
        {
            var namedRows = named.Count(r => r[labelCol] is string v && NamedIn(" " + Normalise(s.Text) + " ", v, lexicon));
            if (namedRows >= 2 && anchor < 0) anchor = s.Id;
            var measures = s.Measures.ToList();
            if (namedRows >= minRows && measures.Count >= minRows
                && measures.All(q => data.Rows.Any(r => valueCols.Any(c => ToDecimal(r[c]) is { } v && QuantityRecognizer.SameFigure(v, q))))
                && IsEnumerative(s))
                covered.Add(s.Id);
        }
        return new InducedTable(columns, rows, BlockOrigin.DataTable, covered, anchor);
    }

    // ── 2. From a sentence listing entity–figure pairs ──────────────────────────────────────────

    private static readonly Regex ClauseBreak = new(@",\s|;\s|\s(?:and|then|while|whereas|followed by|vs\.?|versus)\s|\s[—–-]\s", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// "TikTok spent $310.00, Facebook $240.00 and Google Ads $95.00" → a three-row table. Pairs are
    /// formed clause by clause: a clause with exactly one entity owns the figures in it (the quantity
    /// attachment of Roy et al., without a parser).
    /// </summary>
    public static InducedTable? FromEntityPairs(Sentence s, int minRows)
    {
        // Only a sentence that does little but list pairs becomes a table (and is replaced by it); a
        // table beside a sentence that still says the same thing would repeat every figure.
        if (!IsEnumerative(s)) return null;
        var pairs = Pairs(s);
        if (pairs.Count < minRows || pairs.Select(p => p.Entity.Name).Distinct().Count() != pairs.Count) return null;
        var width = pairs.Min(p => p.Figures.Count);
        if (width == 0) return null;
        // A column holds one kind of figure: "$310.00" and "6 conversions" are not one column.
        for (var k = 0; k < width; k++)
            if (pairs.Select(p => p.Figures[k].Unit).Distinct().Count() > 1) return null;
        var withDetail = pairs.All(p => p.Detail.Length > 0);

        var header = new List<string> { EntityHeader(pairs.Select(p => p.Entity)) };
        for (var k = 0; k < width; k++) header.Add(FigureHeader(s, pairs.Select(p => p.Figures[k]).ToList(), k, width));
        if (withDetail) header.Add("Detail");
        var rows = pairs.Select(p =>
        {
            var row = new List<string> { p.Entity.Name };
            row.AddRange(p.Figures.Take(width).Select(f => f.Raw));
            if (withDetail) row.Add(p.Detail);
            return (IReadOnlyList<string>)row;
        }).ToList();

        var covered = IsEnumerative(s) && pairs.Sum(p => p.Figures.Count + p.DetailFigures) == s.Measures.Count() ? new HashSet<int> { s.Id } : new HashSet<int>();
        return new InducedTable(header, rows, BlockOrigin.EntityPairs, covered, s.Id);
    }

    /// <summary>An entity, its figures, and any parenthesised workings after them, kept verbatim.</summary>
    private sealed record Pair(LexiconEntity Entity, List<Quantity> Figures)
    {
        public string Detail { get; set; } = "";
        public int DetailFigures { get; set; }
    }

    private static List<Pair> Pairs(Sentence s)
    {
        var text = s.Text.Replace("**", "  ");
        var bounds = new List<int> { 0 };
        foreach (Match m in ClauseBreak.Matches(text)) bounds.Add(m.Index);
        bounds.Add(text.Length);
        var pairs = new List<Pair>();
        for (var i = 0; i + 1 < bounds.Count; i++)
        {
            int a = bounds[i], b = bounds[i + 1];
            var ents = s.Entities.Where(e => e.Start >= a && e.Start < b).Select(e => e.Value).Distinct().ToList();
            var inClause = s.Measures.Where(q => q.Start >= a && q.Start < b).ToList();
            var parens = Regex.Matches(text[a..b], @"\([^()]*\)").Select(m => (Start: a + m.Index, End: a + m.Index + m.Length, Text: m.Value)).ToList();
            var figs = inClause.Where(q => !parens.Any(p => q.Start >= p.Start && q.End <= p.End)).ToList();
            if (ents.Count == 1 && figs.Count > 0)
            {
                var detail = string.Join(" ", parens.Select(p => p.Text));
                var detailFigures = inClause.Count - figs.Count;
                var existing = pairs.FirstOrDefault(p => p.Entity == ents[0]);
                if (existing is null) pairs.Add(new Pair(ents[0], figs) { Detail = detail, DetailFigures = detailFigures });
                else { existing.Figures.AddRange(figs); existing.Detail = (existing.Detail + " " + detail).Trim(); existing.DetailFigures += detailFigures; }
            }
        }
        return pairs;
    }

    // ── 3. From sentences or list items sharing one template ──────────────────────────────────

    /// <summary>
    /// Three or more consecutive items with the same skeleton once names and figures are masked
    /// ("Cost per click: ⟨E⟩ ⟨N⟩ (⟨N⟩ ÷ ⟨N⟩)") are one table: a row per item, the first figure as a
    /// column, and whatever follows it kept verbatim as a detail column.
    /// </summary>
    public static InducedTable? FromTemplate(IReadOnlyList<Sentence> items, int minRows, bool coversItems)
    {
        if (items.Count < minRows) return null;
        // The template is everything up to the first figure; what follows it may differ per item and
        // is kept verbatim as the detail column.
        var skeletons = items.Select(i => { var k = Skeleton(i); var n = k.IndexOf("⟨n⟩", StringComparison.Ordinal); return n < 0 ? k : k[..(n + 3)]; }).ToList();
        if (skeletons.Distinct().Count() != 1) return null;
        if (items.Any(i => i.Entities.Select(e => e.Value).Distinct().Count() != 1 || !i.Measures.Any())) return null;

        var first = items[0];
        var firstMeasure = first.Measures.First();
        var label = TemplateLabel(first, firstMeasure);
        var hasDetail = items.All(i => DetailAfter(i).Length > 0);

        var columns = new List<string> { EntityHeader(items.Select(i => i.Entities[0].Value)), label };
        if (hasDetail) columns.Add("Detail");
        var rows = items.Select(i =>
        {
            var row = new List<string> { i.Entities[0].Value.Name, i.Measures.First().Raw };
            if (hasDetail) row.Add(DetailAfter(i));
            return (IReadOnlyList<string>)row;
        }).ToList();
        return new InducedTable(columns, rows, BlockOrigin.Template,
            coversItems ? items.Select(i => i.Id).ToHashSet() : new HashSet<int>(), items[0].Id);
    }

    internal static string Skeleton(Sentence s)
    {
        var text = s.Text.Replace("**", "");
        var spans = s.Entities.Select(e => (e.Start, e.End, "⟨E⟩"))
            .Concat(s.Quantities.Select(q => (q.Start, q.End, "⟨N⟩")))
            .OrderByDescending(x => x.Start).ToList();
        var plain = s.Text.Replace("**", "  ");
        var sb = new StringBuilder(plain);
        foreach (var (start, end, token) in spans)
            if (start >= 0 && end <= sb.Length) { sb.Remove(start, end - start); sb.Insert(start, token); }
        return Regex.Replace(sb.ToString().ToLowerInvariant(), @"\s+", " ").Trim();
    }

    private static string TemplateLabel(Sentence s, Quantity q)
    {
        var text = s.Text.Replace("**", "  ");
        var colon = text.IndexOf(':');
        if (colon > 0 && colon < q.Start) return text[..colon].Trim();
        var metric = s.Metrics.OrderBy(m => Math.Abs(m.Start - q.Start)).FirstOrDefault();
        return metric?.Value.Label ?? "Value";
    }

    private static string DetailAfter(Sentence s)
    {
        var text = s.Text.Replace("**", "  ");
        var q = s.Measures.First();
        var entityEnd = s.Entities.Max(e => e.End);
        var from = Math.Max(q.End, entityEnd);
        return from >= text.Length ? "" : text[from..].Trim().TrimEnd('.', ';').Trim();
    }

    // ── Helpers shared by the three ────────────────────────────────────────────────────────

    /// <summary>
    /// A sentence that does little but list names and figures. Only such a sentence may be replaced
    /// by a table; anything with more to say stays as prose next to it.
    /// </summary>
    internal static bool IsEnumerative(Sentence s)
    {
        var plain = s.Text.Replace("**", "  ");
        var sb = new StringBuilder(plain);
        foreach (var (start, end) in s.Entities.Select(e => (e.Start, e.End)).Concat(s.Quantities.Select(q => (q.Start, q.End)))
                     .Concat(s.Metrics.Select(m => (m.Start, m.End))).OrderByDescending(x => x.Item1))
            if (end <= sb.Length) sb.Remove(start, end - start);
        var residue = Salience.Tokens(sb.ToString()).Where(t => !Fillers.Contains(t)).ToList();
        return residue.Count <= 4;
    }

    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "at", "then", "followed", "spent", "spend", "with", "while", "and", "next", "came", "had", "got", "each",
        "respectively", "after", "behind", "ahead", "most", "least", "highest", "lowest", "top",
    };

    internal static string EntityHeader(IEnumerable<LexiconEntity> entities)
    {
        var kinds = entities.Select(e => e.Kind).Distinct().ToList();
        return kinds.Count == 1 ? Capitalise(kinds[0]) : "Item";
    }

    private static string FigureHeader(Sentence s, IReadOnlyList<Quantity> column, int index, int width)
    {
        var metrics = s.Metrics.Select(m => m.Value).Distinct().ToList();
        if (metrics.Count == width) return metrics[index].Label;
        if (metrics.Count == 1 && index == 0) return metrics[0].Label;
        // The one metric whose unit fits the column ("1.29%, 5.68%, 12.13%" with CTR and impressions → CTR).
        var fitting = metrics.Where(m => m.Unit switch
        {
            MetricUnit.Money => column[0].Unit == QuantityUnit.Currency,
            MetricUnit.Percent => column[0].Unit == QuantityUnit.Percent,
            MetricUnit.Ratio => column[0].Unit == QuantityUnit.Multiplier,
            MetricUnit.Count => column[0].Unit == QuantityUnit.Number,
            _ => false,
        }).ToList();
        if (fitting.Count == 1) return fitting[0].Label;
        return column[0].Unit switch
        {
            QuantityUnit.Currency => "Amount",
            QuantityUnit.Percent => "Share",
            QuantityUnit.Multiplier => "Ratio",
            _ => "Value",
        };
    }

    private static bool NamedIn(string lowerText, string value, Lexicon lexicon)
    {
        var v = Normalise(value);
        if (v.Length < 2) return false;
        if (Regex.IsMatch(lowerText, @"(?<![a-z0-9])" + Regex.Escape(v) + @"(?![a-z0-9])")) return true;
        var entity = lexicon.Entities.FirstOrDefault(e => e.Aliases.Any(a => Normalise(a) == v) || Normalise(e.Name) == v);
        return entity is not null && entity.Aliases.Append(entity.Name)
            .Any(a => Regex.IsMatch(lowerText, @"(?<![a-z0-9])" + Regex.Escape(Normalise(a)) + @"(?![a-z0-9])"));
    }

    private static string Normalise(string s) => Regex.Replace(s.ToLowerInvariant().Replace('_', ' ').Replace("**", ""), @"\s+", " ").Trim();

    internal static string Display(string value, Lexicon lexicon)
    {
        var v = Normalise(value);
        var entity = lexicon.Entities.FirstOrDefault(e => e.Aliases.Any(a => Normalise(a) == v) || Normalise(e.Name) == v);
        return entity?.Name ?? value;
    }

    private static string LabelHeader(DataTable data, int col, Lexicon lexicon)
    {
        var values = data.Rows.Select(r => r[col] as string).Where(v => v is not null).Select(v => Normalise(v!)).ToList();
        var kinds = lexicon.Entities.Where(e => values.Any(v => e.Aliases.Any(a => Normalise(a) == v) || Normalise(e.Name) == v))
            .Select(e => e.Kind).Distinct().ToList();
        return kinds.Count == 1 ? Capitalise(kinds[0]) : Humanise(data.Columns[col], lexicon);
    }

    /// <summary>"TotalSpend_USD" → "Spend"; "WebConversions" → "Web conversions"; "PlatformKey" → "Platform".</summary>
    internal static string Humanise(string column, Lexicon lexicon)
    {
        var c = Regex.Replace(column, @"_(USD|BDT|EUR|GBP|AED)$", "", RegexOptions.IgnoreCase);
        c = Regex.Replace(c, @"^(Total|Sum|Avg|Count)_?", "", RegexOptions.IgnoreCase);
        c = Regex.Replace(c, @"_(" + string.Join("|", lexicon.Metrics.SelectMany(m => m.Aliases).Where(a => !a.Contains(' ')).Select(Regex.Escape)) + @")$", "", RegexOptions.IgnoreCase);
        var words = Regex.Replace(c.Replace('_', ' '), @"(?<=[a-z])(?=[A-Z])", " ").Trim();
        var lower = words.ToLowerInvariant();
        var metric = lexicon.Metrics.FirstOrDefault(m => m.Aliases.Any(a => a.Equals(lower, StringComparison.OrdinalIgnoreCase) || a.Replace(" ", "").Equals(lower.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));
        if (metric is not null) return metric.Label;
        if (lower.EndsWith(" key") || lower.EndsWith(" id")) words = words[..words.LastIndexOf(' ')];
        return Capitalise(words.ToLowerInvariant());
    }

    private static string FormatCell(object? value, string column, Lexicon lexicon)
    {
        if (ToDecimal(value) is not { } v) return value?.ToString() ?? "";
        var label = Humanise(column, lexicon);
        var metric = lexicon.Metrics.FirstOrDefault(m => m.Label == label);
        var money = column.EndsWith("_USD", StringComparison.OrdinalIgnoreCase) || metric?.Unit == MetricUnit.Money;
        var inv = CultureInfo.InvariantCulture;
        if (money) return (v < 0 ? "-$" : "$") + Math.Abs(v).ToString(Math.Abs(v) < 1 && v != 0 ? "0.0000" : "#,##0.00", inv);
        if (v == Math.Truncate(v)) return v.ToString("#,##0", inv);
        return v.ToString(Math.Abs(v) < 1 ? "0.####" : "#,##0.##", inv);
    }

    internal static decimal? ToDecimal(object? v) => v switch
    {
        null => null,
        decimal d => d,
        int i => i,
        long l => l,
        double f when !double.IsNaN(f) && !double.IsInfinity(f) => (decimal)f,
        float f when !float.IsNaN(f) && !float.IsInfinity(f) => (decimal)f,
        string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) => p,
        _ => null,
    };

    internal static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

internal static class Prose
{
    private static readonly Regex Ordinal = new(@"^(first|second|third|fourth|fifth|finally|lastly|next|also)\b[,:]?|^\d+[.)]\s", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Runs of two or more sentences opening with ordinals ("First, …", "Second, …") read as a list.</summary>
    public static IEnumerable<(int Start, int Count)> OrdinalRuns(IReadOnlyList<Sentence> sentences)
    {
        var i = 0;
        while (i < sentences.Count)
        {
            if (!Ordinal.IsMatch(sentences[i].Text.TrimStart('*', ' '))) { i++; continue; }
            var j = i;
            while (j < sentences.Count && Ordinal.IsMatch(sentences[j].Text.TrimStart('*', ' '))) j++;
            if (j - i >= 2) yield return (i, j - i);
            i = Math.Max(j, i + 1);
        }
    }

    /// <summary>"Three things stand out: a; b; c." → an intro and three items. Null when it is not such a sentence.</summary>
    public static (string Intro, IReadOnlyList<string> Items)? SemicolonList(Sentence s)
    {
        var text = s.Text;
        var colon = text.IndexOf(':');
        if (colon < 0) return null;
        var parts = text[(colon + 1)..].Split(';').Select(p => p.Trim().TrimEnd('.')).Where(p => p.Length > 0).ToList();
        if (parts.Count < 3) return null;
        // Items verbatim (only trimmed): a list is a presentation of the sentence, not a rewrite of it.
        return (text[..(colon + 1)].Trim(), parts);
    }

    /// <summary>
    /// TextTiling (Hearst, 1997) over sentences: gap scores are the cosine between the k sentences
    /// before and after each gap; a gap whose depth (how far it sits below the peaks on either side)
    /// exceeds mean − sd/2 starts a new paragraph. Applied only to long paragraphs.
    /// </summary>
    public static IReadOnlyList<int> TopicBreaks(IReadOnlyList<Sentence> sentences, int k = 2, int minParagraph = 2)
    {
        var n = sentences.Count;
        if (n < 6) return Array.Empty<int>();
        var vecs = sentences.Select(s => Bag(s.Text)).ToList();
        var gaps = new double[n - 1];
        for (var g = 0; g < n - 1; g++)
        {
            var left = Merge(vecs.Skip(Math.Max(0, g - k + 1)).Take(Math.Min(k, g + 1)));
            var right = Merge(vecs.Skip(g + 1).Take(k));
            gaps[g] = Salience.Cosine(left, right);
        }
        var depth = new double[gaps.Length];
        for (var g = 0; g < gaps.Length; g++)
        {
            double lp = gaps[g], rp = gaps[g];
            for (var l = g; l >= 0 && gaps[l] >= lp; l--) lp = gaps[l];
            for (var r = g; r < gaps.Length && gaps[r] >= rp; r++) rp = gaps[r];
            depth[g] = (lp - gaps[g]) + (rp - gaps[g]);
        }
        var mean = depth.Average();
        var sd = Math.Sqrt(depth.Select(d => (d - mean) * (d - mean)).Average());
        var breaks = new List<int>();
        var last = 0;
        foreach (var g in Enumerable.Range(0, depth.Length).Where(g => depth[g] > mean - sd / 2 && depth[g] > 0).OrderByDescending(g => depth[g]))
        {
            var at = g + 1;
            if (at - last < minParagraph || n - at < minParagraph || breaks.Any(b => Math.Abs(b - at) < minParagraph)) continue;
            breaks.Add(at);
        }
        return breaks.OrderBy(b => b).ToList();
    }

    /// <summary>
    /// Near-duplicates: word 3-shingle Jaccard ≥ 0.8, the redundancy test behind MMR
    /// (Carbonell and Goldstein, 1998). The later copy is dropped and recorded as covered.
    /// </summary>
    public static bool NearDuplicate(string a, string b)
    {
        var sa = Shingles(a);
        var sb = Shingles(b);
        if (sa.Count == 0 || sb.Count == 0) return Norm(a) == Norm(b);
        var inter = sa.Intersect(sb).Count();
        return inter / (double)(sa.Count + sb.Count - inter) >= 0.8;
    }

    private static HashSet<string> Shingles(string s)
    {
        var w = Norm(s).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 2 < w.Length; i++) set.Add($"{w[i]} {w[i + 1]} {w[i + 2]}");
        return set;
    }

    private static string Norm(string s) => Regex.Replace(s.ToLowerInvariant().Replace("**", ""), @"[^a-z0-9$%.]+", " ").Trim();

    private static Dictionary<string, double> Bag(string text)
        => Salience.Tokens(text).GroupBy(t => t).ToDictionary(g => g.Key, g => (double)g.Count());

    private static Dictionary<string, double> Merge(IEnumerable<Dictionary<string, double>> bags)
    {
        var m = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var b in bags) foreach (var (k, v) in b) m[k] = m.GetValueOrDefault(k) + v;
        return m;
    }
}
