using TextMagic.Analysis;
using TextMagic.Text;

namespace TextMagic;

/// <summary>
/// Lays text out without AI. The pipeline (docs/ALGORITHMS.md):
///
///   parse (CommonMark) → segment → annotate (figures, entities, metrics, connectives)
///   → classify roles (cue phrases) → score salience (position, question, LexRank, figures)
///   → induce structure (data tables, entity–figure tables, template tables, lists)
///   → regroup long paragraphs (TextTiling) and drop near-duplicates (MMR)
///   → choose key figures (MMR) → assemble → VERIFY
///
/// Verification is what makes it safe to use anywhere: every sentence of the input appears exactly
/// once (as text, or as the source of a table row) and every figure shown is a figure in the input
/// or in the caller's data. If either check fails, the result is the input's own paragraphs, flagged
/// <see cref="LayoutDocument.IsFallback"/>. The engine never throws for bad input.
/// </summary>
public static class TextLayoutEngine
{
    public static LayoutDocument Format(string? text, LayoutOptions? options = null)
    {
        options ??= new LayoutOptions();
        if (string.IsNullOrWhiteSpace(text)) return new LayoutDocument();

        IReadOnlyList<SourceUnit> units;
        try { units = SourceReader.Read(text, options.Lexicon); }
        catch (Exception ex) { return Plain(text, $"parse failed: {ex.GetType().Name}"); }

        try
        {
            var doc = Assemble(units, text, options);
            var problems = Verifier.Check(doc, units, text, options);
            return problems.Count == 0 ? doc : Fallback(units, problems);
        }
        catch (Exception ex)
        {
            return Fallback(units, new[] { $"layout failed: {ex.GetType().Name}: {ex.Message}" });
        }
    }

    // ── Assembly ───────────────────────────────────────────────────────────────────────────

    private static LayoutDocument Assemble(IReadOnlyList<SourceUnit> units, string text, LayoutOptions o)
    {
        var lexicon = o.Lexicon;
        var prose = units.OfType<ProseUnit>().SelectMany(u => u.Sentences).ToList();
        var listItems = units.OfType<ListUnit>().SelectMany(u => u.Items).ToList();

        RoleClassifier.Classify(prose, lexicon);
        RoleClassifier.Classify(listItems, lexicon);
        Salience.Score(prose, o.Question);

        // Headline: the most salient sentence that is not a caveat, decision note or definition.
        // When the text is nothing but those (a refusal, a "no data"), its first sentence leads.
        var headline = prose.Where(s => s.Role is not (Role.Caveat or Role.Decision or Role.Definition) && !RoleClassifier.IsIntro(s))
                           .OrderByDescending(s => s.Salience).ThenBy(s => s.Id).FirstOrDefault()
                       ?? prose.FirstOrDefault();
        if (o.Profile == LayoutProfile.Alert) headline = prose.FirstOrDefault();
        // An answer that opens by declining to decide ("That's your team's call — here is what the data
        // says") has answered a should-question: that opening IS the headline.
        if (prose.FirstOrDefault() is { Role: Role.Decision } opening) headline = opening;

        // Near-duplicates: keep the first copy.
        var duplicates = new HashSet<int>();
        for (var i = 0; i < prose.Count; i++)
            for (var j = 0; j < i; j++)
                if (!duplicates.Contains(prose[j].Id) && Prose.NearDuplicate(prose[i].Text, prose[j].Text)) { duplicates.Add(prose[i].Id); break; }

        // Tables from the caller's data first (exact values), then from the prose itself.
        var hasMarkdownTable = units.OfType<TableUnit>().Any();
        var induced = new List<InducedTable>();
        if (!hasMarkdownTable)
            foreach (var d in o.Data)
                if (Tables.FromData(prose, text, d, lexicon, o.MinTableRows) is { } t) induced.Add(t);
        var markdownTables = units.OfType<TableUnit>().ToList();
        foreach (var s in prose)
        {
            if (induced.Any(t => t.CoversSentences.Contains(s.Id) || t.AnchorSentence == s.Id)) continue;
            if (Tables.FromEntityPairs(s, o.MinTableRows) is not { } t) continue;
            // The writer's own table already holds these figures: a second table would only repeat it.
            var mdFigures = markdownTables.SelectMany(m => m.Rows.SelectMany(r => r)).SelectMany(QuantityRecognizer.Find).Where(q => q.IsMeasure).ToList();
            var repeated = t.Rows.Count(r => r.Count > 1 && QuantityRecognizer.Find(r[1]).FirstOrDefault(q => q.IsMeasure) is { } f
                                             && mdFigures.Any(m => QuantityRecognizer.SameFigure(m.Value, f)));
            if (repeated >= 2) continue;
            induced.Add(t);
        }
        // A headline is never swallowed by a table; the table sits next to it instead.
        if (headline is not null)
            induced = induced.Select(t => t.CoversSentences.Contains(headline.Id)
                ? t with { CoversSentences = t.CoversSentences.Where(id => id != headline.Id).ToHashSet() } : t).ToList();
        var coveredByTable = induced.SelectMany(t => t.CoversSentences).ToHashSet();

        var blocks = new List<LayoutBlock>();
        var callouts = new List<(Sentence S, CalloutTone Tone)>();

        if (headline is not null)
            blocks.Add(new LayoutBlock { Kind = BlockKind.Headline, Origin = BlockOrigin.Salience, Text = headline.Text });

        if (o.Profile != LayoutProfile.Alert)
        {
            var tablesForFigures = markdownTables.Select(m => (m.Columns, m.Rows))
                .Concat(induced.Select(t => (t.Columns, t.Rows))).ToList();
            var figures = KeyFigures.Select(prose.Where(s => !duplicates.Contains(s.Id)).ToList(), headline, o.MaxKeyFigures, tablesForFigures);
            if (figures.Count > 0) blocks.Add(new LayoutBlock { Kind = BlockKind.KeyFigures, Origin = BlockOrigin.Figures, Figures = figures });
        }

        var placedTables = new HashSet<InducedTable>();
        void PlaceTablesAnchoredAt(int sentenceId)
        {
            foreach (var t in induced.Where(t => !placedTables.Contains(t) && (t.AnchorSentence == sentenceId || t.CoversSentences.Contains(sentenceId))))
            {
                blocks.Add(TableBlock(t.Columns, t.Rows, t.Origin));
                placedTables.Add(t);
            }
        }

        foreach (var unit in units)
        {
            switch (unit)
            {
                case HeadingUnit h:
                    blocks.Add(new LayoutBlock { Kind = BlockKind.Heading, Origin = BlockOrigin.Markdown, Text = h.Text });
                    break;
                case TableUnit t:
                    blocks.Add(TableBlock(t.Columns, t.Rows, BlockOrigin.Markdown));
                    break;
                case ListUnit l:
                    if (Tables.FromTemplate(l.Items, o.MinTableRows, coversItems: true) is { } templ)
                        blocks.Add(TableBlock(templ.Columns, templ.Rows, BlockOrigin.Template));
                    else
                        blocks.Add(new LayoutBlock { Kind = BlockKind.Bullets, Origin = BlockOrigin.Markdown, Items = l.Items.Select(i => i.Text).ToList() });
                    break;
                case ProseUnit p:
                    EmitProse(p.Sentences);
                    break;
            }
        }
        foreach (var t in induced.Where(t => !placedTables.Contains(t))) blocks.Add(TableBlock(t.Columns, t.Rows, t.Origin));

        // Callouts last: warnings, then notes, then decision notes, consecutive ones merged.
        foreach (var group in GroupCallouts(callouts))
            blocks.Add(new LayoutBlock { Kind = BlockKind.Callout, Origin = BlockOrigin.Role, Tone = group.Tone, Text = group.Text });

        return new LayoutDocument { Blocks = blocks };

        void EmitProse(IReadOnlyList<Sentence> sentences)
        {
            // Sentences that stay as body text, in order.
            var body = new List<Sentence>();
            foreach (var s in sentences)
            {
                PlaceTablesBefore(s);
                if (headline is not null && s.Id == headline.Id) continue;
                if (duplicates.Contains(s.Id) || coveredByTable.Contains(s.Id)) continue;
                if (s.Role is Role.Caveat or Role.Decision)
                {
                    callouts.Add((s, s.Role == Role.Caveat && s.Warning ? CalloutTone.Warning : CalloutTone.Info));
                    continue;
                }
                body.Add(s);
            }
            FlushBody(body);

            void PlaceTablesBefore(Sentence s)
            {
                if (body.Count > 0 && induced.Any(t => !placedTables.Contains(t) && (t.AnchorSentence == s.Id || t.CoversSentences.Contains(s.Id))))
                {
                    FlushBody(body);
                    body.Clear();
                }
                PlaceTablesAnchoredAt(s.Id);
            }
        }

        void FlushBody(List<Sentence> body)
        {
            if (body.Count == 0) return;
            var list = body.ToList();

            // Ordinal runs ("First, … Second, …") become lists.
            var runs = Prose.OrdinalRuns(list).ToList();
            var i = 0;
            var paragraph = new List<Sentence>();
            while (i < list.Count)
            {
                var run = runs.FirstOrDefault(r => r.Start == i);
                if (run.Count > 0)
                {
                    EmitParagraph(paragraph); paragraph.Clear();
                    blocks.Add(new LayoutBlock { Kind = BlockKind.Bullets, Origin = BlockOrigin.Enumeration, Items = list.Skip(i).Take(run.Count).Select(s => s.Text).ToList() });
                    i += run.Count;
                    continue;
                }
                if (Prose.SemicolonList(list[i]) is { } semi)
                {
                    EmitParagraph(paragraph); paragraph.Clear();
                    blocks.Add(new LayoutBlock { Kind = BlockKind.Paragraph, Origin = BlockOrigin.Enumeration, Text = semi.Intro });
                    blocks.Add(new LayoutBlock { Kind = BlockKind.Bullets, Origin = BlockOrigin.Enumeration, Items = semi.Items });
                    i++;
                    continue;
                }
                paragraph.Add(list[i]);
                i++;
            }
            EmitParagraph(paragraph);
        }

        void EmitParagraph(List<Sentence> sentences)
        {
            if (sentences.Count == 0) return;
            // Keep the writer's paragraphs; split a long one at its topic shifts (TextTiling).
            foreach (var para in sentences.GroupBy(s => s.Paragraph).Select(g => g.ToList()))
            {
                var breaks = para.Count >= 6 || o.Profile == LayoutProfile.Insight ? Prose.TopicBreaks(para) : Array.Empty<int>();
                var from = 0;
                foreach (var at in breaks.Append(para.Count))
                {
                    var chunk = para.Skip(from).Take(at - from).ToList();
                    if (chunk.Count > 0)
                        blocks.Add(new LayoutBlock { Kind = BlockKind.Paragraph, Origin = BlockOrigin.Prose, Text = string.Join(" ", chunk.Select(s => s.Text)) });
                    from = at;
                }
            }
        }
    }

    /// <summary>Callouts in the writer's order; consecutive sentences of one paragraph and one tone are one callout.</summary>
    private static IEnumerable<(CalloutTone Tone, string Text)> GroupCallouts(List<(Sentence S, CalloutTone Tone)> callouts)
    {
        var ordered = callouts.OrderBy(c => c.S.Id).ToList();
        var i = 0;
        while (i < ordered.Count)
        {
            var j = i + 1;
            while (j < ordered.Count && ordered[j].Tone == ordered[i].Tone && ordered[j].S.Role == ordered[i].S.Role && ordered[j].S.Id == ordered[j - 1].S.Id + 1) j++;
            yield return (ordered[i].Tone, string.Join(" ", ordered.Skip(i).Take(j - i).Select(c => c.S.Text)));
            i = j;
        }
    }

    private static LayoutBlock TableBlock(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows, BlockOrigin origin)
        => new() { Kind = BlockKind.Table, Origin = origin, Columns = columns, Rows = rows };

    // ── Fallbacks ──────────────────────────────────────────────────────────────────────────

    /// <summary>The input's own structure, unchanged: paragraphs, its tables, its lists, its headings.</summary>
    private static LayoutDocument Fallback(IReadOnlyList<SourceUnit> units, IReadOnlyList<string> why)
    {
        var blocks = new List<LayoutBlock>();
        foreach (var u in units)
            blocks.Add(u switch
            {
                HeadingUnit h => new LayoutBlock { Kind = BlockKind.Heading, Origin = BlockOrigin.Markdown, Text = h.Text },
                TableUnit t => TableBlock(t.Columns, t.Rows, BlockOrigin.Markdown),
                ListUnit l => new LayoutBlock { Kind = BlockKind.Bullets, Origin = BlockOrigin.Markdown, Items = l.Items.Select(i => i.Text).ToList() },
                ProseUnit p => new LayoutBlock { Kind = BlockKind.Paragraph, Origin = BlockOrigin.Prose, Text = string.Join(" ", p.Sentences.Select(s => s.Text)) },
                _ => throw new InvalidOperationException(),
            });
        return new LayoutDocument { Blocks = blocks, IsFallback = true, Diagnostics = why };
    }

    private static LayoutDocument Plain(string text, string why) => new()
    {
        Blocks = new[] { new LayoutBlock { Kind = BlockKind.Paragraph, Origin = BlockOrigin.Prose, Text = text.Trim() } },
        IsFallback = true,
        Diagnostics = new[] { why },
    };
}

/// <summary>The two invariants every layout must satisfy before it is returned.</summary>
internal static class Verifier
{
    public static IReadOnlyList<string> Check(LayoutDocument doc, IReadOnlyList<SourceUnit> units, string text, LayoutOptions o)
    {
        var problems = new List<string>();

        // 1. Coverage: every sentence of the input is shown, or tabulated, or a recorded duplicate.
        //    Checked on content: each sentence's text must appear in some text block, unless a table
        //    holds all of its figures (a replaced enumeration) or an earlier sentence is its near-twin.
        var shownText = string.Join("\n", doc.Blocks.SelectMany(BlockTexts));
        var tableCells = doc.Blocks.Where(b => b.Kind == BlockKind.Table).SelectMany(b => (b.Rows ?? Array.Empty<IReadOnlyList<string>>()).SelectMany(r => r).Concat(b.Columns ?? Array.Empty<string>())).ToList();
        var cellFigures = tableCells.SelectMany(QuantityRecognizer.Find).Where(q => q.IsMeasure).ToList();
        var prose = units.OfType<ProseUnit>().SelectMany(u => u.Sentences).ToList();
        foreach (var s in prose)
        {
            if (shownText.Contains(s.Text, StringComparison.Ordinal)) continue;
            // Shown as an intro and a list: every piece around its colon and semicolons is shown.
            var pieces = s.Text.Split(':', ';').Select(x => x.Trim().TrimEnd('.').Trim()).Where(x => x.Length > 0).ToList();
            if (pieces.Count > 1 && pieces.All(x => shownText.Contains(x, StringComparison.Ordinal))) continue;
            var tabulated = s.Measures.Any() && s.Measures.All(q => cellFigures.Any(c => QuantityRecognizer.SameFigure(q.Value, c)));
            var twin = prose.TakeWhile(p => p.Id < s.Id).Any(p => Prose.NearDuplicate(p.Text, s.Text) && shownText.Contains(p.Text, StringComparison.Ordinal));
            if (!tabulated && !twin) problems.Add($"sentence {s.Id} is not shown: \"{Short(s.Text)}\"");
        }
        foreach (var l in units.OfType<ListUnit>())
            foreach (var item in l.Items)
            {
                if (shownText.Contains(item.Text, StringComparison.Ordinal)) continue;
                if (!(item.Entities.Count > 0 && tableCells.Contains(item.Entities[0].Value.Name))) problems.Add($"list item not shown: \"{Short(item.Text)}\"");
            }

        // 2. Figures: every figure shown is a figure of the input (as written or rounded) or a data cell.
        var allowed = QuantityRecognizer.Find(text).Where(q => q.IsMeasure).Select(q => q.Value).ToList();
        allowed.AddRange(o.Data.SelectMany(d => d.Rows).SelectMany(r => r).Select(Tables.ToDecimal).Where(v => v is not null).Select(v => v!.Value));
        foreach (var piece in doc.Blocks.SelectMany(BlockTexts).Concat(tableCells))
            foreach (var q in QuantityRecognizer.Find(piece))
            {
                if (!q.IsMeasure) { if (!text.Contains(q.Raw, StringComparison.OrdinalIgnoreCase) && q.Unit != QuantityUnit.Year) problems.Add($"date not in input: {q.Raw}"); continue; }
                if (!allowed.Any(v => QuantityRecognizer.SameFigure(v, q))) problems.Add($"figure not in input: {q.Raw}");
            }
        return problems;
    }

    internal static IEnumerable<string> BlockTexts(LayoutBlock b)
    {
        if (b.Text is { } t) yield return t;
        foreach (var i in b.Items ?? Array.Empty<string>()) yield return i;
        foreach (var f in b.Figures ?? Array.Empty<KeyFigure>()) { yield return f.Value; if (f.Note is { } n) yield return n; }
    }

    private static string Short(string s) => s.Length <= 60 ? s : s[..60] + "…";
}
