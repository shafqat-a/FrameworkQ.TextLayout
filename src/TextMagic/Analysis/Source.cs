using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using TextMagic.Text;

namespace TextMagic.Analysis;

internal enum Role { Answer, Figure, Comparison, Context, Caveat, Decision, Definition, Other }

/// <summary>One sentence and everything the engine knows about it.</summary>
internal sealed class Sentence
{
    public required int Id { get; init; }
    public required int Paragraph { get; init; }
    public required string Text { get; init; }

    public IReadOnlyList<Quantity> Quantities { get; set; } = Array.Empty<Quantity>();
    public IReadOnlyList<PhraseHit<LexiconEntity>> Entities { get; set; } = Array.Empty<PhraseHit<LexiconEntity>>();
    public IReadOnlyList<PhraseHit<LexiconMetric>> Metrics { get; set; } = Array.Empty<PhraseHit<LexiconMetric>>();
    public bool HasContrast { get; set; }
    public Trend Direction { get; set; }
    public bool IsTotal { get; set; }

    public Role Role { get; set; } = Role.Other;
    public bool Warning { get; set; }
    public double Salience { get; set; }

    /// <summary>Measures only: what a reader counts as "the figures" in this sentence.</summary>
    public IEnumerable<Quantity> Measures => Quantities.Where(q => q.IsMeasure);
}

/// <summary>The input as units: prose paragraphs (as sentences), headings, lists and tables.</summary>
internal abstract record SourceUnit;
internal sealed record ProseUnit(IReadOnlyList<Sentence> Sentences) : SourceUnit;
internal sealed record HeadingUnit(string Text) : SourceUnit;
internal sealed record ListUnit(IReadOnlyList<Sentence> Items) : SourceUnit;
internal sealed record TableUnit(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows) : SourceUnit;

/// <summary>Reads text into units with a CommonMark parser, then annotates every sentence.</summary>
internal static class SourceReader
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();

    public static IReadOnlyList<SourceUnit> Read(string text, Lexicon lexicon)
    {
        var src = (text ?? "").Replace("\r\n", "\n");
        var doc = Markdown.Parse(src, Pipeline);
        var units = new List<SourceUnit>();
        var annotator = new Annotator(lexicon);
        var sentenceId = 0;
        var paragraph = 0;

        foreach (var block in doc)
        {
            switch (block)
            {
                case HeadingBlock h:
                    units.Add(new HeadingUnit(Inline(Raw(src, h)).TrimStart('#', ' ')));
                    break;
                case Table t:
                {
                    var rows = new List<IReadOnlyList<string>>();
                    IReadOnlyList<string>? header = null;
                    foreach (var r in t.OfType<TableRow>())
                    {
                        var cells = r.OfType<TableCell>().Select(c => Inline(Raw(src, c))).ToList();
                        if (r.IsHeader && header is null) header = cells; else rows.Add(cells);
                    }
                    if (header is not null) units.Add(new TableUnit(header, rows));
                    break;
                }
                case ListBlock l:
                    units.Add(new ListUnit(l.OfType<ListItemBlock>().Select(i => Inline(Raw(src, i))).Where(s => s.Length > 0)
                        .Select(s => annotator.Annotate(new Sentence { Id = sentenceId++, Paragraph = -1, Text = s })).ToList()));
                    break;
                case ThematicBreakBlock:
                    break;
                default:
                {
                    var raw = block is LeafBlock or QuoteBlock or FencedCodeBlock ? Raw(src, block) : Raw(src, block);
                    var sentences = SentenceSegmenter.Split(Inline(raw))
                        .Select(s => annotator.Annotate(new Sentence { Id = sentenceId++, Paragraph = paragraph, Text = s }))
                        .ToList();
                    if (sentences.Count > 0) { units.Add(new ProseUnit(sentences)); paragraph++; }
                    break;
                }
            }
        }
        return units;
    }

    private static string Raw(string src, Block b) => b.Span.Length <= 0 ? "" : src.Substring(b.Span.Start, Math.Min(b.Span.Length, src.Length - b.Span.Start));
    private static string Raw(string src, TableCell c) => c.Span.Length <= 0 ? "" : src.Substring(c.Span.Start, Math.Min(c.Span.Length, src.Length - c.Span.Start));

    private static readonly Regex Link = new(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Italic = new(@"(?<![*\w])\*(?!\*)([^*\n]+?)\*(?!\*)|(?<![_\w])_([^_\n]+?)_(?![_\w])", RegexOptions.Compiled);

    /// <summary>The inline markdown the layout keeps is **bold**; links keep their text, the rest is plain.</summary>
    internal static string Inline(string raw)
    {
        var s = raw.Trim();
        s = Regex.Replace(s, @"^\s*(?:[-*+•]|\d+[.)])\s+", "", RegexOptions.Multiline); // a list item's marker
        s = s.Trim('|').Trim();
        s = Link.Replace(s, "$1");
        s = Italic.Replace(s, m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        s = s.Replace("`", "").Replace("__", "**");
        return s.Trim();
    }
}

/// <summary>Quantities, entities, metrics, connectives and direction for one sentence.</summary>
internal sealed class Annotator
{
    private readonly Lexicon _lexicon;
    private readonly PhraseMatcher<LexiconEntity> _entities;
    private readonly PhraseMatcher<LexiconMetric> _metrics;
    private readonly PhraseMatcher<string> _contrast;
    private readonly PhraseMatcher<Trend> _direction;
    private readonly PhraseMatcher<bool> _total;

    public Annotator(Lexicon lexicon)
    {
        _lexicon = lexicon;
        _entities = new(lexicon.Entities.SelectMany(e => e.Aliases.Append(e.Name).Select(a => (a, e))));
        _metrics = new(lexicon.Metrics.SelectMany(m => m.Aliases.Append(m.Label).Select(a => (a, m))));
        _contrast = new(lexicon.ContrastConnectives.Select(c => (c, c)));
        _direction = new(lexicon.UpWords.Select(w => (w, Trend.Up))
            .Concat(lexicon.DownWords.Select(w => (w, Trend.Down)))
            .Concat(lexicon.FlatWords.Select(w => (w, Trend.Flat))));
        _total = new(lexicon.TotalWords.Select(w => (w, true)));
    }

    public Sentence Annotate(Sentence s)
    {
        var plain = s.Text.Replace("**", "  "); // same length, so spans still line up
        s.Entities = _entities.FindAll(plain);
        s.Metrics = _metrics.FindAll(plain);
        // A number inside a name ("Spring Sale 2026", "1st Edition") is part of the name, not a figure.
        s.Quantities = QuantityRecognizer.Find(plain)
            .Where(q => !s.Entities.Any(e => q.Start < e.End && e.Start < q.End))
            .ToList();
        s.HasContrast = _contrast.FindAll(plain).Count > 0;
        var dirs = _direction.FindAll(plain).Select(h => h.Value).Distinct().ToList();
        s.Direction = dirs.Count == 1 ? dirs[0] : Trend.None;
        s.IsTotal = _total.FindAll(plain).Count > 0;
        return s;
    }

    public Lexicon Lexicon => _lexicon;
}
