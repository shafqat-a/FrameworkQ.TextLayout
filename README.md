# FrameworkQ.TextLayout

**Lay text out without AI.** FrameworkQ.TextLayout turns an answer, a summary or a report paragraph,
whether a person wrote it or an LLM generated it, into a structured layout:

- a **headline**;
- **key-figure cards**;
- **paragraphs** and **lists**;
- **tables**;
- **info and warning callouts**.

It is deterministic, takes a couple of milliseconds, and its only dependency is
[Markdig](https://github.com/xoofx/markdig). Two guarantees are checked on every call:

1. **No sentence is lost.** Every sentence of the input appears in the layout, either as text or
   as the source of a table that replaces it.
2. **No figure is invented.** Every number shown is a number in the input (as written, or rounded
   such as 120,456 → 120.5K) or a value in data you supplied.

If either check fails, you get the input's own paragraphs back, flagged `IsFallback`. `Format`
never throws.

```
dotnet add package FrameworkQ.TextLayout
```

```csharp
using FrameworkQ.TextLayout;

var doc = TextLayoutEngine.Format(text, new LayoutOptions
{
    Question = "Which channel spent the most?",    // optional: sharpens the headline
    Data = new[] { new DataTable(columns, rows) }, // optional: exact values the text was written from
    Profile = LayoutProfile.Answer,                 // Answer | Insight (long text) | Alert
});

string json  = LayoutRenderers.Json(doc);                  // { "blocks": [ ... ] } for a front end
string html  = LayoutRenderers.Html(doc);                  // class-based HTML (al-* classes)
string email = LayoutRenderers.Html(doc, HtmlStyle.Email); // inline-styled HTML for email and PDF
string plain = LayoutRenderers.PlainText(doc);             // SMS, logs, terminals
```

## What it does

| Input | Output |
|---|---|
| The answer buried mid-paragraph | Moved to the **headline**. Salience combines position, overlap with the question, LexRank centrality and figure density. |
| "TikTok spent $310.00, Facebook $240.00 and Google Ads $95.00." | A Channel / Spend **table that replaces the sentence**. Each entity is paired with its figures clause by clause. |
| "CPC was $0.0017 for Google Ads ($95.00 ÷ 55,000), …" | A table with the workings kept verbatim in a **Detail** column. |
| Three or more list items sharing one template | **One table** (template induction). |
| Rows of data you pass in `Data` | A table of the **exact** values, with only the columns the text quotes (text–table alignment). |
| A markdown table with a Total row | Kept as written; its totals become the **key-figure cards**. |
| Caveats, data gaps, "that's your team's decision" | **Callouts** in the writer's order, as warnings or notes. Roles come from cue phrases and discourse connectives. |
| "First, … Second, …" or "X: a; b; c" | **Lists**. |
| A wall of text | Split into paragraphs at its topic shifts (**TextTiling**). |
| A sentence said twice | Shown **once**. |

Every block records its `Origin` (Markdown, Salience, DataTable, EntityPairs, Template, Role,
Prose, Enumeration, Figures), so you can see why the layout looks the way it does.

## Vocabulary

Everything domain-specific lives in a `Lexicon`: entities with their aliases, metrics with their
units, and the cue phrases for roles.
- `Lexicon.Marketing` (the default) knows ad channels and marketing, web-analytics and ad-fraud metrics.
- `Lexicon.General` knows only roles and directions.
- Add your own names with `WithEntities(...)`, or build a lexicon for another domain.

```csharp
var lexicon = Lexicon.Marketing.WithEntities(new[]
{
    new LexiconEntity("Spring Sale", "campaign", new[] { "spring sale", "spring-sale-2026" }),
});
```

## The algorithms

It is a pipeline of classical, unsupervised NLP and IR methods, each solving one part of the problem:

| Step | Method |
|---|---|
| Structure the writer gave | CommonMark parsing (Markdig) |
| Sentences | Punkt-style boundary rules (Kiss and Strunk, 2006) |
| Figures | Quantity recognition: value, unit and comparator (Roy, Vieira and Roth, 2015), with exact spans |
| Entities and metrics | Aho–Corasick gazetteer matching, longest match first |
| Sentence roles | Cue phrases, as in argumentative zoning (Teufel), and explicit discourse connectives (Penn Discourse Treebank) |
| The answer | Lead bias, question overlap, LexRank (Erkan and Radev, 2004) and figure density |
| Tables | Text–table quantity alignment (after BriQ, Ibrahim et al., 2019), entity–figure pairing, template induction |
| Paragraphs | TextTiling (Hearst, 1997) |
| Repetition and card diversity | Maximal Marginal Relevance (Carbonell and Goldstein, 1998) |

[docs/ALGORITHMS.md](docs/ALGORITHMS.md) covers each step and its references.

## Front ends

`Json(doc)` gives `{ blocks: [{ type, text, tone, items, metrics, columns, rows }] }`, where
`type` is one of `headline`, `metrics`, `heading`, `paragraph`, `bullets`, `table` or `callout`.
Render the blocks as text: the only inline markup is `**bold**`. `Html(doc)` encodes everything
and is safe to insert.

## Building and testing

```
dotnet test
dotnet pack -c Release src/FrameworkQ.TextLayout
```

The tests cover each component, each repair on hand-written disorganised texts, the guarantees on
a sample set, and 500 randomly assembled texts. Targets .NET 8 and .NET 10.

## License

MIT. See [LICENSE](LICENSE).
