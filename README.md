# TextMagic

**Lay text out without AI.** TextMagic turns an answer, a summary or a report paragraph,
whether a person wrote it or an LLM generated it, into a structured layout:

- a **headline**;
- **key-figure cards**;
- **paragraphs** and **lists**;
- **tables**;
- **info and warning callouts**.

It is deterministic, takes a couple of milliseconds, and its only dependency is
[Markdig](https://github.com/xoofx/markdig). It targets **.NET 8** and **.NET 10**.

Try it in the browser: [shafqat-a.github.io/TextMagic](https://shafqat-a.github.io/TextMagic/).
Paste text, and any rows the text was written from, and the page returns the HTML.
Nothing is uploaded. The page is [`src/TextMagic.Pages`](src/TextMagic.Pages), published by
[`.github/workflows/pages.yml`](.github/workflows/pages.yml).

Two guarantees are checked on every call:

1. **No sentence is lost.** Every sentence of the input appears in the layout, either as text or
   as the source of a table that replaces it.
2. **No figure is invented.** Every number shown is a number in the input (as written, or rounded
   such as 120,456 → 120.5K) or a value in data you supplied.

If either check fails, you get the input's own paragraphs back, flagged `IsFallback`. `Format`
never throws.

## Install

From NuGet:

```bash
dotnet add package TextMagic
```

From a pack of this repository, into another project:

```bash
dotnet pack src/TextMagic -c Release -o ./artifacts
dotnet add package TextMagic --source ./artifacts
```

Or reference the project directly:

```bash
dotnet add reference path/to/TextMagic/src/TextMagic/TextMagic.csproj
```

```csharp
using TextMagic;
```

## Quick start

```csharp
var doc = TextLayoutEngine.Format(text, new LayoutOptions
{
    Question = "Which loaf sold the most?",        // optional: sharpens the headline
    Data = new[] { new DataTable(columns, rows) }, // optional: exact values the text was written from
    Profile = LayoutProfile.Answer,                 // Answer | Insight | Alert
});

string json  = LayoutRenderers.Json(doc);                  // { "blocks": [ ... ] } for a front end
string html  = LayoutRenderers.Html(doc);                  // class-based HTML (al-* classes)
string email = LayoutRenderers.Html(doc, HtmlStyle.Email); // inline-styled HTML for email and PDF
string plain = LayoutRenderers.PlainText(doc);             // SMS, logs, terminals
```

`text` may be plain prose or CommonMark (headings, lists, tables, `**bold**`). `options` may be
omitted. `null` or whitespace returns an empty document: `Blocks` is empty and `IsFallback` is
false.

Fifteen graded calls, each followed by the email HTML that call produced, are in [Samples](#samples).

### A worked example

```csharp
var bakery = Lexicon.General with
{
    Entities = new[]
    {
        new LexiconEntity("Sourdough", "loaf", new[] { "sourdough" }),
        new LexiconEntity("Rye", "loaf", new[] { "rye" }),
        new LexiconEntity("Focaccia", "loaf", new[] { "focaccia" }),
    },
    Metrics = new[]
    {
        new LexiconMetric("Sales", new[] { "sales", "sale", "took in", "takings" }, MetricUnit.Money),
        new LexiconMetric("Loaves", new[] { "loaves" }, MetricUnit.Count),
        new LexiconMetric("Customers", new[] { "customers", "customer" }, MetricUnit.Count),
        new LexiconMetric("Unit cost", new[] { "cost per loaf", "unit cost" }, MetricUnit.Money),
    },
};

const string text =
    "The bakery took in $1,240.00 over the last 7 days for 860 loaves and 410 customers. " +
    "Sourdough sold the most, at 310 loaves of 860. " +
    "Note that Sunday's till is partial and excluded.";

var doc = TextLayoutEngine.Format(text, new LayoutOptions
{
    Question = "What did the bakery take in over the last 7 days?",
    Lexicon = bakery,   // the default lexicon is the marketing one; this call opts out
});
```

`LayoutRenderers.PlainText(doc)` prints:

```
The bakery took in $1,240.00 over the last 7 days for 860 loaves and 410 customers.

Sales: $1,240.00  ·  Loaves: 860  ·  Customers: 410

Sourdough sold the most, at 310 loaves of 860.

! Note that Sunday's till is partial and excluded.
```

`LayoutRenderers.Html(doc, HtmlStyle.Email)` is the same layout as HTML. Pasted straight into
this markdown file, a viewer renders it:

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">The bakery took in $1,240.00 over the last 7 days for 860 loaves and 410 customers.</p>
<table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:separate;border-spacing:8px 0">
<tr>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Sales</div>
<div style="font-size:20px;font-weight:700;color:#111827">$1,240.00</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Loaves</div>
<div style="font-size:20px;font-weight:700;color:#111827">860</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Customers</div>
<div style="font-size:20px;font-weight:700;color:#111827">410</div>
</td>
</tr>
</table>
<p style="margin:0 0 12px">Sourdough sold the most, at 310 loaves of 860.</p>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">Note that Sunday&#39;s till is partial and excluded.</div>
</div>

The same document, block by block:

| Order | `Kind` | `Origin` | What it holds |
|---|---|---|---|
| 1 | `Headline` | `Salience` | "The bakery took in $1,240.00 over the last 7 days…" |
| 2 | `KeyFigures` | `Figures` | Sales $1,240.00, Loaves 860, Customers 410 |
| 3 | `Paragraph` | `Prose` | "Sourdough sold the most, at 310 loaves of 860." |
| 4 | `Callout` | `Role` | The partial-till note, `Tone = Warning` |

## Read the document

`Format` returns a `LayoutDocument`.

| Member | Meaning |
|---|---|
| `Blocks` | The layout, in display order. |
| `IsFallback` | `true` when a check failed and the blocks are the input's own paragraphs, lists, tables and headings. |
| `Diagnostics` | Why it fell back, or what it noticed. Write these to a log. Leave them off the page. |

Walk the blocks when you render them yourself:

```csharp
foreach (var block in doc.Blocks)
{
    switch (block.Kind)
    {
        case BlockKind.Headline:
        case BlockKind.Heading:
        case BlockKind.Paragraph:
            Use(block.Text);
            break;
        case BlockKind.Callout:
            Use(block.Text, block.Tone);          // Info or Warning
            break;
        case BlockKind.Bullets:
            Use(block.Items);
            break;
        case BlockKind.Table:
            Use(block.Columns, block.Rows);
            break;
        case BlockKind.KeyFigures:
            Use(block.Figures);                   // Label, Value, Note, Trend
            break;
    }
}
```

Which members are set depends on `Kind`:

| `Kind` | Members in use |
|---|---|
| `Headline`, `Heading`, `Paragraph` | `Text` |
| `Callout` | `Text`, `Tone` (`Info` or `Warning`) |
| `Bullets` | `Items` |
| `Table` | `Columns`, `Rows` |
| `KeyFigures` | `Figures`: `Label`, `Value`, `Note`, `Trend` (`None`, `Up`, `Down`, `Flat`) |

`Text` and list items may contain `**bold**`. That is the only inline markup.

Every block also has an `Origin`, so a surprising layout can be explained from the output alone:

| `Origin` | The block came from |
|---|---|
| `Markdown` | A heading, list or table already in the text |
| `Salience` | The sentence chosen as the headline |
| `DataTable` | Rows you passed in `LayoutOptions.Data` |
| `EntityPairs` | One sentence listing three or more entity–figure pairs |
| `Template` | Three or more sentences or list items sharing one shape |
| `Role` | A caveat or a "that's your decision" sentence |
| `Prose` | Sentences kept in the writer's order |
| `Enumeration` | "First, … Second, …" or "X: a; b; c" |
| `Figures` | The key-figure cards |

## Render it

### JSON

`LayoutRenderers.Json(doc)` serialises `LayoutRenderers.ToWire(doc)`. Each block has the same
seven fields. Unused ones are `null`.

```json
{
  "blocks": [
    { "type": "headline", "text": "The bakery took in $1,240.00 over the last 7 days for 860 loaves and 410 customers.", "tone": null, "items": null, "metrics": null, "columns": null, "rows": null },
    { "type": "metrics", "text": null, "tone": null, "items": null, "metrics": [
        { "label": "Sales", "value": "$1,240.00", "note": null, "trend": null },
        { "label": "Loaves", "value": "860", "note": null, "trend": null },
        { "label": "Customers", "value": "410", "note": null, "trend": null }
      ], "columns": null, "rows": null },
    { "type": "paragraph", "text": "Sourdough sold the most, at 310 loaves of 860.", "tone": null, "items": null, "metrics": null, "columns": null, "rows": null },
    { "type": "callout", "text": "Note that Sunday's till is partial and excluded.", "tone": "warning", "items": null, "metrics": null, "columns": null, "rows": null }
  ]
}
```

`type` is `headline`, `metrics`, `heading`, `paragraph`, `bullets`, `table` or `callout`.
`KeyFigures` is sent as `metrics`. `tone` is `info` or `warning`, and only on a callout.
`trend` on a metric is `up`, `down` or `flat`.

The JSON is the visible layout. `IsFallback`, `Diagnostics` and `Origin` stay on the
`LayoutDocument`. Check those in C# before you decide how to present the payload.

### HTML for an app

`LayoutRenderers.Html(doc)` encodes every string. `**bold**` becomes `<strong>`. A `<script>` in
the input is text, not markup. The root is `<div class="al">`.

| Block | Element |
|---|---|
| Headline | `p.al-headline` |
| Heading | `h3.al-heading` |
| Paragraph | `p.al-paragraph` |
| Bullets | `ul.al-bullets` |
| Callout | `div.al-callout.al-callout-info` or `.al-callout-warning`, with `role="note"` |
| Key figures | `div.al-metrics` > `div.al-metric`, with `.al-metric-label`, `.al-metric-value`, `.al-metric-note` |
| Trend arrow | `span.al-trend.al-trend-up` (▲) or `.al-trend-down` (▼) |
| Table | `div.al-table-wrap` > `table.al-table`; numeric columns use `th.al-num` and `td.al-num` |

Style those classes in your own stylesheet. A starting point:

```css
.al { color: #1f2937; line-height: 1.55; font-size: 15px; }
.al-headline { margin: 0 0 12px; font-size: 18px; font-weight: 600; color: #111827; }
.al-heading { margin: 16px 0 6px; font-size: 16px; }
.al-paragraph { margin: 0 0 12px; }
.al-bullets { margin: 0 0 12px; padding-left: 20px; }
.al-callout { margin: 0 0 12px; padding: 9px 12px; border-radius: 6px; }
.al-callout-info { background: #eef2ff; color: #3730a3; }
.al-callout-warning { background: #fef3c7; color: #92400e; }
.al-metrics { display: flex; gap: 8px; margin: 0 0 12px; }
.al-metric { border: 1px solid #e5e7eb; border-radius: 8px; padding: 8px 12px; }
.al-metric-label { display: block; font-size: 11px; font-weight: 600; text-transform: uppercase; color: #6b7280; }
.al-metric-value { display: block; font-size: 20px; font-weight: 700; }
.al-metric-note { display: block; font-size: 12px; color: #6b7280; }
.al-trend-up { color: #059669; font-size: 13px; }
.al-trend-down { color: #b91c1c; font-size: 13px; }
.al-table { border-collapse: collapse; font-size: 14px; margin: 0 0 12px; }
.al-table th, .al-table td { padding: 6px 10px; border-bottom: 1px solid #e5e7eb; text-align: left; }
.al-table th { background: #f3f4f6; }
.al-num { text-align: right; }
```

### HTML for email and PDF

`LayoutRenderers.Html(doc, HtmlStyle.Email)` uses inline styles and a presentational table for the
figure cards. There are no classes. The colours match the stylesheet above.

### Plain text

`LayoutRenderers.PlainText(doc)` strips `**bold**`. Bullets use `•`. A warning callout is prefixed
with `! `, an info callout with `Note: `. Tables are aligned with spaces.

## Options

Everything on `LayoutOptions` is optional.

```csharp
var doc = TextLayoutEngine.Format(text, new LayoutOptions
{
    Question = "Which channel spent the most?",
    Profile = LayoutProfile.Answer,
    Lexicon = Lexicon.Marketing,
    Data = Array.Empty<DataTable>(),
    MaxKeyFigures = 4,   // cards per document; fewer than 2 means no card row
    MinTableRows = 3,    // two items stay as prose
});
```

| Property | Default | Effect |
|---|---|---|
| `Question` | `null` | Extra weight for a sentence that overlaps the question, when choosing the headline. |
| `Profile` | `Answer` | `Answer`, `Insight` or `Alert`. See below. |
| `Lexicon` | `Lexicon.Marketing` | Entities, metrics and cue phrases. See [Vocabulary](#vocabulary). |
| `Data` | empty | Exact rows the text was written from. See [Pass the source rows](#pass-the-source-rows). |
| `MaxKeyFigures` | `4` | Upper bound on figure cards. The engine emits a card row only when it can show at least two different metrics. |
| `MinTableRows` | `3` | A comparison becomes a table only at this many rows. Two items stay as prose. |

Numbers are read with invariant culture: `,` is a thousands separator and `.` is the decimal point
(`$1,250.75`, `120,000`, `4.1%`, `13.2x`, `+2.4 pp`, `3.58M`, `BDT 1,250.75`, `2026-09-26`).

### Profiles

**`Answer`** (the default). A short reply. The most salient sentence becomes the headline, unless
the text opens by declining to decide ("Where the extra budget goes is your team's call"), in
which case that opening is the headline. Caveats and decision notes move to callouts at the end,
still in the writer's order. A paragraph of six or more sentences is split where the topic shifts.

**`Insight`**. A longer report. Body sentences are regrouped at topic shifts even when the
paragraph is short. Pass `Lexicon.General` when the text is outside the marketing vocabulary.

```csharp
var doc = TextLayoutEngine.Format(report, new LayoutOptions
{
    Profile = LayoutProfile.Insight,
    Lexicon = Lexicon.General,
});
```

On a wall of sourdough sentences followed by a wall of oven-fault sentences, this yields a headline and
two paragraphs, one per topic.

**`Alert`**. A one- or two-sentence notice. The first sentence is the headline, and no key-figure
cards are added.

```csharp
var doc = TextLayoutEngine.Format(
    "Spend went up week over week: $151.20 in the last 7 days against $126.40 in the 7 days before, an increase of $24.80 (19.6%).",
    new LayoutOptions { Profile = LayoutProfile.Alert });
// one Headline block, nothing else
```

### Pass the source rows

When the prose was written from a query result, pass that result. If the text quotes three or more
rows and does not already contain a markdown table, the layout gains a table built from the cell
values. Only columns the text actually quotes are kept. Names and headers come from the lexicon:
`tiktok` is shown as TikTok, `TotalSpend_USD` as Spend.

```csharp
var data = new DataTable(
    new[] { "PlatformKey", "TotalSpend_USD", "TotalClicks" },
    new[]
    {
        new object?[] { "tiktok", 310.0012345m, 48000L },
        new object?[] { "facebook", 240.00m, 17000L },
        new object?[] { "google_ads", 95.0034m, 55000L },
    });

var doc = TextLayoutEngine.Format(
    "Spend was led by TikTok. Over the last 30 days TikTok spent $310.00, " +
    "Facebook spent $240.00 and Google Ads spent $95.00, while clicks were strongest on TikTok.",
    new LayoutOptions { Data = new[] { data } });
```

The table is `Origin = DataTable`, columns `Channel` and `Spend` (clicks were never quoted as
figures, so that column is left out), and the money cells are the data values formatted as
currency (`310.0012345` → `$310.00`). Cells may be `string`, an integer type, `decimal`, `double`,
`float` or `null`.

A markdown table in the text is kept as the writer wrote it, including a **Total** row. That total
row supplies the key-figure cards. `Data` is not applied on top of a markdown table.

### A sentence that is already a table

Three or more entity–figure pairs in one sentence become a table and the sentence is removed,
unless that sentence is the headline. The headline is kept, and the table sits beside it.

```csharp
var doc = TextLayoutEngine.Format(
    "Spend was led by TikTok. By channel: TikTok spent $310.00, Facebook $240.00 and Google Ads $95.00.");
```

```
Spend was led by TikTok.

Channel     Spend
TikTok      $310.00
Facebook    $240.00
Google Ads  $95.00
```

Parenthetical workings stay in a **Detail** column:

```csharp
// "CPC was $0.0015 for Google Ads ($95.00 ÷ 55,000), $0.0048 for TikTok ($310.00 ÷ 48,000)
//  and $0.0086 for Facebook ($240.00 ÷ 17,000)."
// columns: Channel, CPC, Detail
// row:     Google Ads, $0.0015, ($95.00 ÷ 55,000)
```

Three or more list items that share one shape become one table (`Origin = Template`):

```markdown
Costs per click:

- Cost per click: Google Ads $0.0015 (lowest)
- Cost per click: TikTok $0.0048 (middle)
- Cost per click: Facebook $0.0086 (highest)
```

Columns: `Channel`, `Cost per click`, `Detail`.

## Vocabulary

Domain words live in a `Lexicon`: entities and their aliases, metrics and their units, and the cue
phrases that mark a caveat, a warning, a decision note or a definition.

- `Lexicon.Marketing` is the default. It knows ad channels (Facebook, TikTok, Google Ads, and
  others) and marketing, web-analytics and ad-fraud metrics (Spend, Clicks, CPC, ROAS, and others).
- `Lexicon.General` knows roles and directions only. Use it for text outside that vocabulary.
- Add names with `WithEntities`. `Kind` is the column header when every row is the same kind of thing.

```csharp
var lexicon = Lexicon.Marketing.WithEntities(new[]
{
    new LexiconEntity("Spring Sale", "campaign", new[] { "spring sale", "spring-sale-2026" }),
    new LexiconEntity("Always On", "campaign", new[] { "always on", "always-on" }),
    new LexiconEntity("Retargeting", "campaign", new[] { "retargeting" }),
});

var doc = TextLayoutEngine.Format(
    "Spring Sale spent $310.00, Always On spent $240.00 and Retargeting spent $95.00.",
    new LayoutOptions { Lexicon = lexicon });
// table columns: Campaign, Amount
```

Matching is whole-word and longest-alias-first, so "Google Ads" wins over "Google".

To cover another domain entirely, start from `Lexicon.General` and set `Entities`, `Metrics` and,
where the default English cues are wrong, `CaveatCues`, `WarningCues`, `DecisionCues` and
`DefinitionCues`. A metric is a `LexiconMetric(label, aliases, unit)` with `MetricUnit` of `Any`,
`Money`, `Count`, `Percent` or `Ratio`.

## What the layout does

| Input | Output |
|---|---|
| The answer buried mid-paragraph | Moved to the **headline**. Salience combines position, overlap with the question, LexRank centrality and figure density. |
| "TikTok spent $310.00, Facebook $240.00 and Google Ads $95.00." | A Channel / Spend **table that replaces the sentence**. Each entity is paired with its figures clause by clause. |
| "CPC was $0.0015 for Google Ads ($95.00 ÷ 55,000), …" | A table with the workings kept verbatim in a **Detail** column. |
| Three or more list items sharing one template | **One table** (template induction). |
| Rows of data you pass in `Data` | A table of the **exact** values, with only the columns the text quotes. |
| A markdown table with a Total row | Kept as written; its totals become the **key-figure cards**. |
| Caveats, data gaps, "that's your team's decision" | **Callouts** in the writer's order, as warnings or notes. |
| "First, … Second, …" or "X: a; b; c" | **Lists**. |
| A wall of text | Split into paragraphs at its topic shifts. |
| A sentence said twice | Shown **once**. |

Cards show the whole, not one channel's share. "You spent $645.00 for 120,000 clicks and 10 web
conversions. TikTok spent $310.00…" produces cards for Spend, Clicks and Web conversions. TikTok's
$310.00 stays in the prose or in a table.

## When it falls back

`Format` catches a parse or layout failure and returns the input's own structure with
`IsFallback = true`. The same flag is set when verification finds a missing sentence or a figure
that is neither in the input nor in `Data`. Render `Blocks` either way. They are safe to show.
Read `Diagnostics` in the log.

```csharp
var doc = TextLayoutEngine.Format(text, options);
if (doc.IsFallback)
    logger.LogWarning("Text layout fell back: {Reasons}", string.Join("; ", doc.Diagnostics));

return LayoutRenderers.Json(doc);
```

## Samples

Each sample below was run through `TextLayoutEngine.Format` before it was written down. None of
them fell back. The block line is `Kind / Origin` in display order. Under it is
`LayoutRenderers.Html(doc, HtmlStyle.Email)`, pasted as HTML so the markdown renders the layout.

The calls are a week at a bakery, plus one weekend at the cake case. They use the `bakery` lexicon
from the worked example wherever a loaf name or a sales figure has to be recognised.
`Lexicon.Marketing` remains the default for a call that does not pass one. `**bold**` in a table
cell becomes `<strong>`.

Markdown allows that raw HTML. GitHub's readme sanitizer removes `style` and `class`, so on
github.com the colours and card borders are dropped and the same markup shows as paragraphs,
lists and tables. A preview that keeps inline styles shows the email colours, including the amber
warning.

### Simple

Short text, and the default options except where a loaf name has to be read.

#### 1. A one-sentence answer

One sentence is the headline. A single figure does not become a card row: cards need two different metrics.

```csharp
var doc = TextLayoutEngine.Format("The bakery took in $1,240.00 over the last 7 days.");
```

Blocks: Headline / Salience

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">The bakery took in $1,240.00 over the last 7 days.</p>
</div>

#### 2. Totals become figure cards

Sales, loaves and customers are three metrics in `bakery`, so they become cards under the headline.

```csharp
var doc = TextLayoutEngine.Format(
    "The bakery took in $1,240.00 over the last 7 days for 860 loaves and 410 customers.",
    new LayoutOptions { Lexicon = bakery });
```

Blocks: Headline / Salience → KeyFigures / Figures

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">The bakery took in $1,240.00 over the last 7 days for 860 loaves and 410 customers.</p>
<table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:separate;border-spacing:8px 0">
<tr>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Sales</div>
<div style="font-size:20px;font-weight:700;color:#111827">$1,240.00</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Loaves</div>
<div style="font-size:20px;font-weight:700;color:#111827">860</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Customers</div>
<div style="font-size:20px;font-weight:700;color:#111827">410</div>
</td>
</tr>
</table>
</div>

#### 3. A markdown list stays a list

Items that do not share one entity-and-figure shape are kept as bullets. The intro line is the headline.

```csharp
var doc = TextLayoutEngine.Format("""
    Three things stood out this week:

    - The sourdough sold out before noon.
    - The rye stayed on the shelf until close.
    - The focaccia went mostly at lunch.
    """);
```

Blocks: Headline / Salience → Bullets / Markdown

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Three things stood out this week:</p>
<ul style="margin:0 0 12px;padding-left:20px">
<li>The sourdough sold out before noon.</li>
<li>The rye stayed on the shelf until close.</li>
<li>The focaccia went mostly at lunch.</li>
</ul>
</div>

#### 4. "First, … Second, …" becomes a list

```csharp
var doc = TextLayoutEngine.Format(
    "Saturday was the busy day. First, the morning queue reached the door. Second, the lunch rush held. Finally, the afternoon eased off.");
```

Blocks: Headline / Salience → Bullets / Enumeration

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Saturday was the busy day.</p>
<ul style="margin:0 0 12px;padding-left:20px">
<li>First, the morning queue reached the door.</li>
<li>Second, the lunch rush held.</li>
<li>Finally, the afternoon eased off.</li>
</ul>
</div>

#### 5. A caveat becomes a warning

"Partial" is a warning cue, so the note leaves the paragraph and sits at the end. The cue lists live on `Lexicon.General`, which the marketing default inherits, so this call passes no lexicon.

```csharp
var doc = TextLayoutEngine.Format(
    "The bakery took in $1,240.00 over the last 7 days. Note that Sunday's till is partial and excluded.");
```

Blocks: Headline / Salience → Callout (Warning) / Role

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">The bakery took in $1,240.00 over the last 7 days.</p>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">Note that Sunday&#39;s till is partial and excluded.</div>
</div>

### Moderate

One structural repair, or one option, on a short text.

#### 6. The answer is buried, and the question pulls it up

`Question` adds weight to the sentence that overlaps it. The staffing note stays, under the answer.

```csharp
var doc = TextLayoutEngine.Format(
    "Here is how the week was staffed. Two bakers covered the early shift. " +
    "Sourdough sold the most, at 310 loaves of 860.",
    new LayoutOptions
    {
        Question = "Which loaf sold the most this week?",
        Lexicon = bakery,
    });
```

Blocks: Headline / Salience → Paragraph / Prose

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Sourdough sold the most, at 310 loaves of 860.</p>
<p style="margin:0 0 12px">Here is how the week was staffed. Two bakers covered the early shift.</p>
</div>

#### 7. A comparison written as prose becomes a table

Three loaf–sales pairs in one sentence are replaced by the table. The sentence that only leads in stays as the headline. `Kind` on the entity (`loaf`) becomes the first column.

```csharp
var doc = TextLayoutEngine.Format(
    "Sales were led by sourdough. By loaf: Sourdough took in $310.00, Rye $240.00 and Focaccia $95.00.",
    new LayoutOptions { Lexicon = bakery });
```

Blocks: Headline / Salience → Table / EntityPairs

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Sales were led by sourdough.</p>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Loaf</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Sales</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Sourdough</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$310.00</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Rye</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$240.00</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Focaccia</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$95.00</td>
</tr>
</tbody>
</table>
</div>

#### 8. Workings stay in a detail column

The figure outside the brackets is the cell. The brackets are kept verbatim. "Cost per loaf" is the `Unit cost` metric, so that is the column name.

```csharp
var doc = TextLayoutEngine.Format(
    "The cost of a loaf differs. Cost per loaf worked out at $0.42 for Focaccia ($95.00 ÷ 226), " +
    "$1.72 for Sourdough ($310.00 ÷ 180) and $2.18 for Rye ($240.00 ÷ 110).",
    new LayoutOptions { Lexicon = bakery });
```

Blocks: Headline / Salience → Table / EntityPairs

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">The cost of a loaf differs.</p>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Loaf</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Unit cost</th>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Detail</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Focaccia</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$0.42</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">($95.00 &#247; 226)</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Sourdough</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$1.72</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">($310.00 &#247; 180)</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Rye</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$2.18</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">($240.00 &#247; 110)</td>
</tr>
</tbody>
</table>
</div>

#### 9. Repeated list items become one table

Three items with the same shape (`Cost per loaf: <loaf> <amount> <note>`) collapse to one table.

```csharp
var doc = TextLayoutEngine.Format("""
    Costs per loaf:

    - Cost per loaf: Focaccia $0.42 (lowest)
    - Cost per loaf: Sourdough $1.72 (middle)
    - Cost per loaf: Rye $2.18 (highest)
    """,
    new LayoutOptions { Lexicon = bakery });
```

Blocks: Headline / Salience → Table / Template

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Costs per loaf:</p>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Loaf</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Cost per loaf</th>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Detail</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Focaccia</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$0.42</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">(lowest)</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Sourdough</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$1.72</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">(middle)</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Rye</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$2.18</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">(highest)</td>
</tr>
</tbody>
</table>
</div>

#### 10. A markdown table is kept, and its total row becomes the cards

The card labels are the column headers. No custom lexicon is required.

```csharp
var doc = TextLayoutEngine.Format("""
    Sales by loaf:

    | Loaf | Sales | Loaves |
    |---|---|---|
    | Sourdough | $310.00 | 180 |
    | Rye | $240.00 | 110 |
    | **Total** | **$550.00** | **290** |
    """);
```

Blocks: Headline / Salience → KeyFigures / Figures → Table / Markdown

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Sales by loaf:</p>
<table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:separate;border-spacing:8px 0">
<tr>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Sales</div>
<div style="font-size:20px;font-weight:700;color:#111827">$550.00</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Loaves</div>
<div style="font-size:20px;font-weight:700;color:#111827">290</div>
</td>
</tr>
</table>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Loaf</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Sales</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Loaves</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Sourdough</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$310.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">180</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Rye</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$240.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">110</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>Total</strong>
</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>$550.00</strong>
</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>290</strong>
</td>
</tr>
</tbody>
</table>
</div>

### Complex

Several structures in one call: a question, a profile, a lexicon, tables, lists and callouts together.

#### 11. A week at the counter

A markdown table, a total row, a follow-up sentence and a warning. The question is passed so the opening comparison is the headline. Cards come from the total row, not from one loaf.

```csharp
var doc = TextLayoutEngine.Format("""
    Over the last 7 days (2 Mar to 8 Mar), sourdough took the most sales and focaccia the most loaves.

    | Loaf | Sales | Loaves | Customers |
    |---|---|---|---|
    | Sourdough | $620.00 | 180 | 140 |
    | Rye | $480.00 | 110 | 90 |
    | Focaccia | $140.00 | 226 | 70 |
    | **Total** | **$1,240.00** | **516** | **300** |

    Sourdough is half of the week's sales ($620.00 of $1,240.00).

    The customer counts for focaccia are tiny (70), so any per-customer reading rests on very little.
    """,
    new LayoutOptions
    {
        Question = "Compare sourdough, rye and focaccia on sales and loaves.",
        Lexicon = bakery,
    });
```

Blocks: Headline / Salience → KeyFigures / Figures → Table / Markdown → Paragraph / Prose → Callout (Warning) / Role

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Over the last 7 days (2 Mar to 8 Mar), sourdough took the most sales and focaccia the most loaves.</p>
<table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:separate;border-spacing:8px 0">
<tr>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Sales</div>
<div style="font-size:20px;font-weight:700;color:#111827">$1,240.00</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Loaves</div>
<div style="font-size:20px;font-weight:700;color:#111827">516</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Customers</div>
<div style="font-size:20px;font-weight:700;color:#111827">300</div>
</td>
</tr>
</table>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Loaf</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Sales</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Loaves</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Customers</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Sourdough</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$620.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">180</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">140</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Rye</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$480.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">110</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">90</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Focaccia</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$140.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">226</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">70</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>Total</strong>
</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>$1,240.00</strong>
</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>516</strong>
</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>300</strong>
</td>
</tr>
</tbody>
</table>
<p style="margin:0 0 12px">Sourdough is half of the week&#39;s sales ($620.00 of $1,240.00).</p>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">The customer counts for focaccia are tiny (70), so any per-customer reading rests on very little.</div>
</div>

#### 12. A long note, split by topic

`Profile = Insight` breaks the wall of sentences where the topic shifts, from sourdough to the oven log. The closing limitation is a warning.

```csharp
var doc = TextLayoutEngine.Format(
    "Sourdough sales rose to $620.00 this week. Sourdough loaves reached 180 at a steady pace. " +
    "Sourdough also drew 140 customers. Sourdough remains the largest loaf by sales. " +
    "The oven log flagged 4 high-heat faults. The oven faults were concentrated on the Monday bake. " +
    "Most oven faults came from one deck. The oven check covered every day of the week. " +
    "These fault notes are observations, not confirmed failures.",
    new LayoutOptions
    {
        Profile = LayoutProfile.Insight,
        Question = "Write the weekly bake note.",
        Lexicon = bakery,
    });
```

Blocks: Headline / Salience → Paragraph / Prose → Paragraph / Prose → Callout (Warning) / Role

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Sourdough sales rose to $620.00 this week.</p>
<p style="margin:0 0 12px">Sourdough loaves reached 180 at a steady pace. Sourdough also drew 140 customers. Sourdough remains the largest loaf by sales.</p>
<p style="margin:0 0 12px">The oven log flagged 4 high-heat faults. The oven faults were concentrated on the Monday bake. Most oven faults came from one deck. The oven check covered every day of the week.</p>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">These fault notes are observations, not confirmed failures.</div>
</div>

#### 13. An answer that declines to decide

The opening refusal is the headline, because it answers a "where should it go?" question by declining. Sales and loaves become cards. Focaccia's own $140.00 and 70 customers stay in the paragraph. The two limitation sentences merge into one warning, and the later decision sentence is an info callout.

```csharp
var doc = TextLayoutEngine.Format(
    "Where the extra $129.00 goes is your team's call, not mine. " +
    "The bakery took in $1,240.00 in the last 7 days for 860 loaves, and focaccia had 70 customers on $140.00 of sales. " +
    "Note that the customer counts are tiny, so treat the ranking with caution. " +
    "The supplier list came back empty, so I can't see how much flour each loaf could use. " +
    "Where to put the extra money is your team's decision.",
    new LayoutOptions
    {
        Question = "If we had $129 more for ingredients, where should it go?",
        Lexicon = bakery,
    });
```

Blocks: Headline / Salience → KeyFigures / Figures → Paragraph / Prose → Callout (Warning) / Role → Callout (Info) / Role

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Where the extra $129.00 goes is your team&#39;s call, not mine.</p>
<table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:separate;border-spacing:8px 0">
<tr>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Sales</div>
<div style="font-size:20px;font-weight:700;color:#111827">$1,240.00</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Loaves</div>
<div style="font-size:20px;font-weight:700;color:#111827">860</div>
</td>
</tr>
</table>
<p style="margin:0 0 12px">The bakery took in $1,240.00 in the last 7 days for 860 loaves, and focaccia had 70 customers on $140.00 of sales.</p>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">Note that the customer counts are tiny, so treat the ranking with caution. The supplier list came back empty, so I can&#39;t see how much flour each loaf could use.</div>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#eef2ff;color:#3730a3">Where to put the extra money is your team&#39;s decision.</div>
</div>

#### 14. Two weeks, in a table the writer already made

The comparison sentence leads. The markdown table is kept, with its signed changes. "Not available" makes the last sentence a warning.

```csharp
var doc = TextLayoutEngine.Format("""
    Over the last 7 days (2 Mar to 8 Mar) against the 7 days before (23 Feb to 1 Mar), sales fell 6.2% while loaves rose 18.0%.

    | Line | Last 7 days | Previous 7 days | Change | % change |
    |---|---|---|---|---|
    | Sales | $1,240.00 | $1,322.00 | -$82.00 | -6.2% |
    | Loaves | 860 | 730 | +130 | +18.0% |

    The previous week had no wholesale orders recorded, so a wholesale comparison is not available.
    """,
    new LayoutOptions
    {
        Question = "Compare the last 7 days against the previous 7 days.",
        Lexicon = bakery,
    });
```

Blocks: Headline / Salience → Table / Markdown → Callout (Warning) / Role

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Over the last 7 days (2 Mar to 8 Mar) against the 7 days before (23 Feb to 1 Mar), sales fell 6.2% while loaves rose 18.0%.</p>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Line</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Last 7 days</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Previous 7 days</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Change</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">% change</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Sales</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$1,240.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$1,322.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">-$82.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">-6.2%</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Loaves</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">860</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">730</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">+130</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">+18.0%</td>
</tr>
</tbody>
</table>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">The previous week had no wholesale orders recorded, so a wholesale comparison is not available.</div>
</div>

#### 15. Cakes, with names the loaf list does not know

`WithEntities` adds three cakes. Their `Kind` (`cake`) becomes the column header. The question makes "Black Forest led the case." the headline, and the "by cake" sentence is replaced by a sales table. The cost list becomes a second table. Ordinals become a list. A partial-count warning and a decision note close the layout. The markdown heading is kept, after the headline and the cards, which are always placed first.

```csharp
var cakes = bakery.WithEntities(new[]
{
    new LexiconEntity("Black Forest", "cake", new[] { "black forest" }),
    new LexiconEntity("Lemon Tart", "cake", new[] { "lemon tart" }),
    new LexiconEntity("Carrot Cake", "cake", new[] { "carrot cake" }),
});

var doc = TextLayoutEngine.Format("""
    ## Weekend cakes

    Black Forest led the case. By cake: Black Forest took in $310.00, Lemon Tart took in $240.00 and Carrot Cake took in $95.00.

    Costs per cake:

    - Cost per cake: Black Forest $6.20 (middle)
    - Cost per cake: Lemon Tart $4.10 (lowest)
    - Cost per cake: Carrot Cake $7.50 (highest)

    The case took in $645.00 for 86 cakes and 120 customers. First, Black Forest grew. Second, Lemon Tart held. Finally, Carrot Cake fell.

    Today's count is partial. The Saturday slices are tiny, so treat the ranking with caution. Where to move the Sunday bake is your team's decision.
    """,
    new LayoutOptions
    {
        Question = "Which cake led the case?",
        Lexicon = cakes,
    });
```

Blocks: Headline / Salience → KeyFigures / Figures → Heading / Markdown → Table / EntityPairs → Paragraph / Prose → Table / Template → Paragraph / Prose → Bullets / Enumeration → Callout (Warning) / Role → Callout (Info) / Role

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Black Forest led the case.</p>
<table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:separate;border-spacing:8px 0">
<tr>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Sales</div>
<div style="font-size:20px;font-weight:700;color:#111827">$645.00</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Customers</div>
<div style="font-size:20px;font-weight:700;color:#111827">120</div>
</td>
</tr>
</table>
<h3 style="margin:16px 0 6px;font-size:16px;color:#111827">Weekend cakes</h3>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Cake</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Sales</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Black Forest</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$310.00</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Lemon Tart</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$240.00</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Carrot Cake</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$95.00</td>
</tr>
</tbody>
</table>
<p style="margin:0 0 12px">Costs per cake:</p>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Cake</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Cost per cake</th>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Detail</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Black Forest</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$6.20</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">(middle)</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Lemon Tart</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$4.10</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">(lowest)</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Carrot Cake</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$7.50</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">(highest)</td>
</tr>
</tbody>
</table>
<p style="margin:0 0 12px">The case took in $645.00 for 86 cakes and 120 customers.</p>
<ul style="margin:0 0 12px;padding-left:20px">
<li>First, Black Forest grew.</li>
<li>Second, Lemon Tart held.</li>
<li>Finally, Carrot Cake fell.</li>
</ul>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">Today&#39;s count is partial. The Saturday slices are tiny, so treat the ranking with caution.</div>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#eef2ff;color:#3730a3">Where to move the Sunday bake is your team&#39;s decision.</div>
</div>

## The algorithms

Each step is a classical, unsupervised NLP or IR method:

| Step | Method |
|---|---|
| Structure the writer gave | CommonMark parsing ([Markdig](https://github.com/xoofx/markdig)) |
| Sentences | Punkt-style boundary rules ([Kiss and Strunk, 2006](https://aclanthology.org/J06-4003/)) |
| Figures | Quantity recognition: value, unit and comparator ([Roy, Vieira and Roth, 2015](https://aclanthology.org/Q15-1001/)), with exact spans |
| Entities and metrics | [Aho–Corasick](https://cr.yp.to/bib/1975/aho.pdf) gazetteer matching, longest match first |
| Sentence roles | Cue phrases, as in argumentative zoning ([Teufel and Moens, 2002](https://aclanthology.org/J02-4002/)), and explicit discourse connectives ([Penn Discourse Treebank](https://aclanthology.org/L08-1093/)) |
| The answer | Lead bias ([Zhu et al., 2021](https://arxiv.org/abs/1912.11602)), question overlap, LexRank ([Erkan and Radev, 2004](https://arxiv.org/abs/1109.2128)) and figure density |
| Tables | Text–table quantity alignment (after BriQ, [Ibrahim et al., 2019](https://www.khoury.northeastern.edu/~mirek/papers/2019-ICDE-BriQ.pdf)), entity–figure pairing, template induction |
| Paragraphs | TextTiling ([Hearst, 1997](https://aclanthology.org/J97-1003/)) |
| Repetition and card diversity | Maximal Marginal Relevance ([Carbonell and Goldstein, 1998](https://www.cs.cmu.edu/~jgc/publication/The_Use_MMR_Diversity_Based_LTMIR_1998.pdf)) |

[docs/ALGORITHMS.md](docs/ALGORITHMS.md) covers each step and its references.

## Building and testing

```bash
dotnet test
dotnet pack -c Release src/TextMagic -o artifacts
```

The tests cover each component, each repair on hand-written disorganised texts, the guarantees on
a sample set, and 500 randomly assembled texts.

## License

MIT. See [LICENSE](LICENSE).
