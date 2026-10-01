using System.Globalization;

namespace TextMagic;

/// <summary>The structured layout of one piece of text. Immutable; render it with <see cref="LayoutRenderers"/>.</summary>
public sealed record LayoutDocument
{
    public IReadOnlyList<LayoutBlock> Blocks { get; init; } = Array.Empty<LayoutBlock>();

    /// <summary>True when the engine could not do better than the original paragraphs (a check failed).</summary>
    public bool IsFallback { get; init; }

    /// <summary>Why the engine fell back, or what it noticed. For logs, never for display.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

public enum BlockKind
{
    /// <summary>The direct answer or lead: one or two sentences.</summary>
    Headline,
    /// <summary>2–4 key figures shown as cards.</summary>
    KeyFigures,
    /// <summary>A section heading the text itself supplied (markdown heading).</summary>
    Heading,
    Paragraph,
    Bullets,
    Table,
    Callout,
}

public enum CalloutTone { Info, Warning }

public enum Trend { None, Up, Down, Flat }

/// <summary>Why a block exists. Recorded on every block so a wrong layout can be debugged from the output alone.</summary>
public enum BlockOrigin
{
    /// <summary>Copied from structure the text already had (a markdown table, list or heading).</summary>
    Markdown,
    /// <summary>The highest-salience sentence, moved to the top.</summary>
    Salience,
    /// <summary>Built from caller-supplied data rows whose values the text quotes.</summary>
    DataTable,
    /// <summary>Built from a sentence listing three or more entity–figure pairs.</summary>
    EntityPairs,
    /// <summary>Built from three or more sentences or list items sharing one template.</summary>
    Template,
    /// <summary>Sentences with a rhetorical role (caveat, decision note).</summary>
    Role,
    /// <summary>Sentences kept in their original order.</summary>
    Prose,
    /// <summary>Ordinal or semicolon enumerations turned into a list.</summary>
    Enumeration,
    /// <summary>Chosen figures.</summary>
    Figures,
}

/// <summary>One block. Which members are set depends on <see cref="Kind"/>.</summary>
public sealed record LayoutBlock
{
    public required BlockKind Kind { get; init; }
    public required BlockOrigin Origin { get; init; }

    /// <summary>Headline, Heading, Paragraph, Callout. May contain **bold**.</summary>
    public string? Text { get; init; }

    public CalloutTone Tone { get; init; } = CalloutTone.Info;

    /// <summary>Bullets.</summary>
    public IReadOnlyList<string>? Items { get; init; }

    /// <summary>Table.</summary>
    public IReadOnlyList<string>? Columns { get; init; }
    public IReadOnlyList<IReadOnlyList<string>>? Rows { get; init; }

    /// <summary>KeyFigures.</summary>
    public IReadOnlyList<KeyFigure>? Figures { get; init; }
}

/// <summary>A figure card: "Spend", "$645.00", "28 Aug to 26 Sep", trend.</summary>
public sealed record KeyFigure(string Label, string Value, string? Note, Trend Trend);

public enum LayoutProfile
{
    /// <summary>A short answer to a question: headline first, caveats last.</summary>
    Answer,
    /// <summary>A longer narrative (insight, report): paragraphs regrouped by topic.</summary>
    Insight,
    /// <summary>A one- or two-sentence notice (alert, recommendation).</summary>
    Alert,
}

/// <summary>How to lay text out. Everything is optional.</summary>
public sealed record LayoutOptions
{
    /// <summary>The question the text answers, if any. Sharpens the choice of headline.</summary>
    public string? Question { get; init; }

    /// <summary>Vocabulary: entities, aliases, metrics, cue phrases. <see cref="Lexicon.Marketing"/> by default.</summary>
    public Lexicon Lexicon { get; init; } = Lexicon.Marketing;

    /// <summary>
    /// Tables of exact values the text was written from (for example, the query results an answer was written from). When the
    /// text quotes three or more of a table's rows, the layout's table is built from these values
    /// rather than from the prose.
    /// </summary>
    public IReadOnlyList<DataTable> Data { get; init; } = Array.Empty<DataTable>();

    public LayoutProfile Profile { get; init; } = LayoutProfile.Answer;

    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    public int MaxKeyFigures { get; init; } = 4;

    /// <summary>Tables need at least this many rows (default 3): two items read better as prose.</summary>
    public int MinTableRows { get; init; } = 3;
}

/// <summary>A table of exact values: column names and rows of cells (string, number, date or null).</summary>
public sealed record DataTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows)
{
    public string? Name { get; init; }
}
