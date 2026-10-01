# FrameworkQ.TextLayout

**Lay text out without AI.** FrameworkQ.TextLayout turns an answer, a summary or a report paragraph,
whether a person wrote it or an LLM generated it, into a structured layout:

- a **headline**;
- **key-figure cards**;
- **paragraphs** and **lists**;
- **tables**;
- **info and warning callouts**.

It is deterministic, takes a couple of milliseconds, and its only dependency is
[Markdig](https://github.com/xoofx/markdig). It targets **.NET 8** and **.NET 10**.

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
dotnet add package FrameworkQ.TextLayout
```

From a pack of this repository, into another project:

```bash
dotnet pack src/FrameworkQ.TextLayout -c Release -o ./artifacts
dotnet add package FrameworkQ.TextLayout --source ./artifacts
```

Or reference the project directly:

```bash
dotnet add reference path/to/FrameworkQ.TextLayout/src/FrameworkQ.TextLayout/FrameworkQ.TextLayout.csproj
```

```csharp
using FrameworkQ.TextLayout;
```

## Quick start

```csharp
var doc = TextLayoutEngine.Format(text, new LayoutOptions
{
    Question = "Which channel spent the most?",    // optional: sharpens the headline
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
const string text =
    "You spent $645.00 over the last 30 days for 120,000 clicks and 10 web conversions. " +
    "TikTok spent the most, at $310.00 of $645.00. " +
    "Note that today's data is partial and excluded.";

var doc = TextLayoutEngine.Format(text, new LayoutOptions
{
    Question = "Which channel spent the most?",
});
```

`LayoutRenderers.PlainText(doc)` prints:

```
You spent $645.00 over the last 30 days for 120,000 clicks and 10 web conversions.

Spend: $645.00  ·  Clicks: 120,000  ·  Web conversions: 10

TikTok spent the most, at $310.00 of $645.00.

! Note that today's data is partial and excluded.
```

`LayoutRenderers.Html(doc, HtmlStyle.Email)` is the same layout as HTML. Pasted straight into
this markdown file, a viewer renders it:

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">You spent $645.00 over the last 30 days for 120,000 clicks and 10 web conversions.</p>
<table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:separate;border-spacing:8px 0"><tr>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top"><div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Spend</div><div style="font-size:20px;font-weight:700;color:#111827">$645.00</div></td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top"><div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Clicks</div><div style="font-size:20px;font-weight:700;color:#111827">120,000</div></td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top"><div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Web conversions</div><div style="font-size:20px;font-weight:700;color:#111827">10</div></td>
</tr></table>
<p style="margin:0 0 12px">TikTok spent the most, at $310.00 of $645.00.</p>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">Note that today&#39;s data is partial and excluded.</div>
</div>

The same document, block by block:

| Order | `Kind` | `Origin` | What it holds |
|---|---|---|---|
| 1 | `Headline` | `Salience` | "You spent $645.00 over the last 30 days…" |
| 2 | `KeyFigures` | `Figures` | Spend $645.00, Clicks 120,000, Web conversions 10 |
| 3 | `Paragraph` | `Prose` | "TikTok spent the most, at $310.00 of $645.00." |
| 4 | `Callout` | `Role` | The partial-data note, `Tone = Warning` |

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
    { "type": "headline", "text": "You spent $645.00 over the last 30 days for 120,000 clicks and 10 web conversions.", "tone": null, "items": null, "metrics": null, "columns": null, "rows": null },
    { "type": "metrics", "text": null, "tone": null, "items": null, "metrics": [
        { "label": "Spend", "value": "$645.00", "note": null, "trend": null },
        { "label": "Clicks", "value": "120,000", "note": null, "trend": null },
        { "label": "Web conversions", "value": "10", "note": null, "trend": null }
      ], "columns": null, "rows": null },
    { "type": "paragraph", "text": "TikTok spent the most, at $310.00 of $645.00.", "tone": null, "items": null, "metrics": null, "columns": null, "rows": null },
    { "type": "callout", "text": "Note that today's data is partial and excluded.", "tone": "warning", "items": null, "metrics": null, "columns": null, "rows": null }
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

On a wall of TikTok sentences followed by a wall of fraud sentences, this yields a headline and
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
`LayoutRenderers.Html(doc, HtmlStyle.Email)`, pasted as HTML so the markdown renders the layout:
a headline, figure cards, paragraphs, lists, tables and callouts. `**bold**` in a table cell
becomes `<strong>`.

Markdown allows that raw HTML. GitHub's readme sanitizer removes `style` and `class`, so on
github.com the colours and card borders are dropped and the same markup shows as paragraphs,
lists and tables. A preview that keeps inline styles shows the email colours, including the amber
warning.

### Simple

Short text, default options, one thing happening.

#### 1. A one-sentence answer

One sentence is the headline. A single figure does not become a card row: cards need two different metrics.

```csharp
var doc = TextLayoutEngine.Format("You spent $645.00 over the last 30 days.");
```

Blocks: Headline / Salience

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">You spent $645.00 over the last 30 days.</p>
</div>

#### 2. Totals become figure cards

Spend, clicks and web conversions are three aggregate metrics, so they become cards under the headline.

```csharp
var doc = TextLayoutEngine.Format(
    "You spent $645.00 over the last 30 days for 120,000 clicks and 10 web conversions.");
```

Blocks: Headline / Salience → KeyFigures / Figures

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">You spent $645.00 over the last 30 days for 120,000 clicks and 10 web conversions.</p>
<table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:separate;border-spacing:8px 0">
<tr>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Spend</div>
<div style="font-size:20px;font-weight:700;color:#111827">$645.00</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Clicks</div>
<div style="font-size:20px;font-weight:700;color:#111827">120,000</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Web conversions</div>
<div style="font-size:20px;font-weight:700;color:#111827">10</div>
</td>
</tr>
</table>
</div>

#### 3. A markdown list stays a list

Items that do not share one entity-and-figure shape are kept as bullets. The intro line is the headline.

```csharp
var doc = TextLayoutEngine.Format("""
    Three things stand out this month:

    - Google Ads produced the most web conversions on the least spend.
    - Facebook's spend brought no web conversions.
    - TikTok delivered the most clicks at a low cost per click.
    """);
```

Blocks: Headline / Salience → Bullets / Markdown

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Three things stand out this month:</p>
<ul style="margin:0 0 12px;padding-left:20px">
<li>Google Ads produced the most web conversions on the least spend.</li>
<li>Facebook&#39;s spend brought no web conversions.</li>
<li>TikTok delivered the most clicks at a low cost per click.</li>
</ul>
</div>

#### 4. "First, … Second, …" becomes a list

```csharp
var doc = TextLayoutEngine.Format(
    "Spend rose this month. First, TikTok grew. Second, Facebook held. Finally, Google Ads fell.");
```

Blocks: Headline / Salience → Bullets / Enumeration

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Spend rose this month.</p>
<ul style="margin:0 0 12px;padding-left:20px">
<li>First, TikTok grew.</li>
<li>Second, Facebook held.</li>
<li>Finally, Google Ads fell.</li>
</ul>
</div>

#### 5. A caveat becomes a warning

"Partial" is a warning cue, so the note leaves the paragraph and sits at the end.

```csharp
var doc = TextLayoutEngine.Format(
    "You spent $645.00 over the last 30 days. Note that today's data is partial and excluded.");
```

Blocks: Headline / Salience → Callout (Warning) / Role

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">You spent $645.00 over the last 30 days.</p>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">Note that today&#39;s data is partial and excluded.</div>
</div>

### Moderate

One structural repair, or one option, on a short text.

#### 6. The answer is buried, and the question pulls it up

`Question` adds weight to the sentence that overlaps it. The background stays, under the answer.

```csharp
var doc = TextLayoutEngine.Format(
    "Here is some background on how the account is set up. Campaigns run on three platforms. " +
    "TikTok spent the most in the last 30 days, at $310.00 of $645.00.",
    new LayoutOptions { Question = "Which channel spent the most in the last 30 days?" });
```

Blocks: Headline / Salience → Paragraph / Prose

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">TikTok spent the most in the last 30 days, at $310.00 of $645.00.</p>
<p style="margin:0 0 12px">Here is some background on how the account is set up. Campaigns run on three platforms.</p>
</div>

#### 7. A comparison written as prose becomes a table

Three entity–figure pairs in one sentence are replaced by the table. The sentence that only leads in stays as the headline.

```csharp
var doc = TextLayoutEngine.Format(
    "Spend was led by TikTok. By channel: TikTok spent $310.00, Facebook $240.00 and Google Ads $95.00.");
```

Blocks: Headline / Salience → Table / EntityPairs

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Spend was led by TikTok.</p>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Channel</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Spend</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">TikTok</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$310.00</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Facebook</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$240.00</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Google Ads</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$95.00</td>
</tr>
</tbody>
</table>
</div>

#### 8. Workings stay in a detail column

The figure outside the brackets is the cell. The brackets are kept verbatim.

```csharp
var doc = TextLayoutEngine.Format(
    "CPC differs a lot by channel. Cost per click worked out at $0.0015 for Google Ads ($95.00 ÷ 55,000), " +
    "$0.0048 for TikTok ($310.00 ÷ 48,000) and $0.0086 for Facebook ($240.00 ÷ 17,000).");
```

Blocks: Headline / Salience → Table / EntityPairs

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">CPC differs a lot by channel.</p>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Channel</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">CPC</th>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Detail</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Google Ads</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$0.0015</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">($95.00 &#247; 55,000)</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">TikTok</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$0.0048</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">($310.00 &#247; 48,000)</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Facebook</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$0.0086</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">($240.00 &#247; 17,000)</td>
</tr>
</tbody>
</table>
</div>

#### 9. Repeated list items become one table

Three items with the same shape (`Cost per click: <channel> <amount> <note>`) collapse to one table.

```csharp
var doc = TextLayoutEngine.Format("""
    Costs per click:

    - Cost per click: Google Ads $0.0015 (lowest)
    - Cost per click: TikTok $0.0048 (middle)
    - Cost per click: Facebook $0.0086 (highest)
    """);
```

Blocks: Headline / Salience → Table / Template

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Costs per click:</p>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Channel</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Cost per click</th>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Detail</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Google Ads</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$0.0015</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">(lowest)</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">TikTok</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$0.0048</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">(middle)</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Facebook</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$0.0086</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">(highest)</td>
</tr>
</tbody>
</table>
</div>

#### 10. A markdown table is kept, and its total row becomes the cards

```csharp
var doc = TextLayoutEngine.Format("""
    Spend by channel:

    | Channel | Spend | Clicks |
    |---|---|---|
    | TikTok | $310.00 | 48,000 |
    | Facebook | $240.00 | 17,000 |
    | **Total** | **$550.00** | **65,000** |
    """);
```

Blocks: Headline / Salience → KeyFigures / Figures → Table / Markdown

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Spend by channel:</p>
<table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:separate;border-spacing:8px 0">
<tr>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Spend</div>
<div style="font-size:20px;font-weight:700;color:#111827">$550.00</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Clicks</div>
<div style="font-size:20px;font-weight:700;color:#111827">65,000</div>
</td>
</tr>
</table>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Channel</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Spend</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Clicks</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">TikTok</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$310.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">48,000</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Facebook</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$240.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">17,000</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>Total</strong>
</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>$550.00</strong>
</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>65,000</strong>
</td>
</tr>
</tbody>
</table>
</div>

### Complex

Several structures in one call: a question, a profile, a lexicon, tables, lists and callouts together.

#### 11. A channel report

A markdown table, a total row, a follow-up sentence and a warning. The question is passed so the opening comparison is the headline. Cards come from the total row, not from one channel.

```csharp
var doc = TextLayoutEngine.Format("""
    Over the last 30 days (2 Mar to 31 Mar), TikTok took the most spend and Google Ads the most clicks.

    | Channel | Spend | Clicks | Web conversions |
    |---|---|---|---|
    | TikTok | $310.00 | 48,000 | 5 |
    | Facebook | $240.00 | 17,000 | 0 |
    | Google Ads | $95.00 | 55,000 | 7 |
    | **Total** | **$645.00** | **120,000** | **12** |

    TikTok is almost half of all your spend ($310.00 of $645.00).

    The web conversion counts are tiny (0–7), so any per-conversion reading rests on very little.
    """,
    new LayoutOptions { Question = "Compare Facebook, TikTok and Google Ads on spend and clicks." });
```

Blocks: Headline / Salience → KeyFigures / Figures → Table / Markdown → Paragraph / Prose → Callout (Warning) / Role

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Over the last 30 days (2 Mar to 31 Mar), TikTok took the most spend and Google Ads the most clicks.</p>
<table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:separate;border-spacing:8px 0">
<tr>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Spend</div>
<div style="font-size:20px;font-weight:700;color:#111827">$645.00</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Clicks</div>
<div style="font-size:20px;font-weight:700;color:#111827">120,000</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Web conversions</div>
<div style="font-size:20px;font-weight:700;color:#111827">12</div>
</td>
</tr>
</table>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Channel</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Spend</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Clicks</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Web conversions</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">TikTok</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$310.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">48,000</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">5</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Facebook</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$240.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">17,000</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">0</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Google Ads</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$95.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">55,000</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">7</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>Total</strong>
</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>$645.00</strong>
</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>120,000</strong>
</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">
<strong>12</strong>
</td>
</tr>
</tbody>
</table>
<p style="margin:0 0 12px">TikTok is almost half of all your spend ($310.00 of $645.00).</p>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">The web conversion counts are tiny (0–7), so any per-conversion reading rests on very little.</div>
</div>

#### 12. A long summary, split by topic

`Profile = Insight` breaks the wall of sentences where the topic shifts, from TikTok performance to fraud screening. The closing limitation is a warning.

```csharp
var doc = TextLayoutEngine.Format(
    "TikTok spend rose to $310.00 this month. TikTok clicks reached 48,000 at a low cost. " +
    "TikTok also drove 5 web conversions. TikTok remains the largest channel by spend. " +
    "Fraud screening flagged 210 high-risk events. Fraud risk events were concentrated on bot traffic. " +
    "Most fraud risk events came from one layer. Fraud screening covered every day of the window. " +
    "These are risk scores, not confirmed fraud.",
    new LayoutOptions { Profile = LayoutProfile.Insight, Question = "Write a monthly summary." });
```

Blocks: Headline / Salience → Paragraph / Prose → Paragraph / Prose → Callout (Warning) / Role

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">TikTok spend rose to $310.00 this month.</p>
<p style="margin:0 0 12px">TikTok clicks reached 48,000 at a low cost. TikTok also drove 5 web conversions. TikTok remains the largest channel by spend.</p>
<p style="margin:0 0 12px">Fraud screening flagged 210 high-risk events. Fraud risk events were concentrated on bot traffic. Most fraud risk events came from one layer. Fraud screening covered every day of the window.</p>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">These are risk scores, not confirmed fraud.</div>
</div>

#### 13. An answer that declines to decide

The opening refusal is the headline, because it answers a "where should it go?" question by declining. Aggregate spend and clicks become cards. The channel's own $95.00 and 7 conversions stay in the paragraph. The two limitation sentences merge into one warning, and the later decision sentence is an info callout.

```csharp
var doc = TextLayoutEngine.Format(
    "Where the extra $129.00 goes is your team's call, not mine. " +
    "You spent $645.00 in the last 30 days for 120,000 clicks, and Google Ads had 7 web conversions on $95.00 of spend. " +
    "Note that the conversion counts are tiny, so treat CPA with caution. " +
    "The budget settings came back empty, so I can't see how much each campaign could absorb. " +
    "Where to move the extra budget is your team's decision.",
    new LayoutOptions { Question = "If I had 20 percent more budget, where should it go?" });
```

Blocks: Headline / Salience → KeyFigures / Figures → Paragraph / Prose → Callout (Warning) / Role → Callout (Info) / Role

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Where the extra $129.00 goes is your team&#39;s call, not mine.</p>
<table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:separate;border-spacing:8px 0">
<tr>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Spend</div>
<div style="font-size:20px;font-weight:700;color:#111827">$645.00</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Clicks</div>
<div style="font-size:20px;font-weight:700;color:#111827">120,000</div>
</td>
</tr>
</table>
<p style="margin:0 0 12px">You spent $645.00 in the last 30 days for 120,000 clicks, and Google Ads had 7 web conversions on $95.00 of spend.</p>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">Note that the conversion counts are tiny, so treat CPA with caution. The budget settings came back empty, so I can&#39;t see how much each campaign could absorb.</div>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#eef2ff;color:#3730a3">Where to move the extra budget is your team&#39;s decision.</div>
</div>

#### 14. Two periods, in a table the writer already made

The comparison sentence leads. The markdown table is kept, with its signed changes. "Not available" makes the last sentence a warning.

```csharp
var doc = TextLayoutEngine.Format("""
    Over the last 30 days (2 Mar to 31 Mar) against the 30 days before (31 Jan to 1 Mar), spend fell 6.2% while clicks rose 51.0%.

    | Metric | Last 30 days | Previous 30 days | Change | % change |
    |---|---|---|---|---|
    | Spend | $645.00 | $687.60 | -$42.60 | -6.2% |
    | Clicks | 120,000 | 79,470 | +40,530 | +51.0% |

    The previous window had no web conversions recorded, so web comparisons are not available.
    """,
    new LayoutOptions { Question = "Compare the last 30 days against the previous 30 days." });
```

Blocks: Headline / Salience → Table / Markdown → Callout (Warning) / Role

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Over the last 30 days (2 Mar to 31 Mar) against the 30 days before (31 Jan to 1 Mar), spend fell 6.2% while clicks rose 51.0%.</p>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Metric</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Last 30 days</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Previous 30 days</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Change</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">% change</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Spend</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$645.00</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$687.60</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">-$42.60</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">-6.2%</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Clicks</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">120,000</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">79,470</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">+40,530</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">+51.0%</td>
</tr>
</tbody>
</table>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">The previous window had no web conversions recorded, so web comparisons are not available.</div>
</div>

#### 15. Campaigns, with names the built-in lexicon does not know

`Lexicon.Marketing.WithEntities` teaches three campaign names. Their `Kind` (`campaign`) becomes the column header. The question makes "Spring Sale led the month." the headline. The "by campaign" sentence is replaced by a spend table. The cost-per-click list becomes a second table. Ordinals become a list. A partial-data warning and a decision note close the layout. The markdown heading is kept, after the headline and the cards, which are always placed first.

```csharp
var lexicon = Lexicon.Marketing.WithEntities(new[]
{
    new LexiconEntity("Spring Sale", "campaign", new[] { "spring sale", "spring-sale-2026" }),
    new LexiconEntity("Always On", "campaign", new[] { "always on", "always-on" }),
    new LexiconEntity("Retargeting", "campaign", new[] { "retargeting" }),
});

var doc = TextLayoutEngine.Format("""
    ## Campaigns

    Spring Sale led the month. By campaign: Spring Sale spent $310.00, Always On spent $240.00 and Retargeting spent $95.00.

    Costs per click:

    - Cost per click: Spring Sale $0.0065 (middle)
    - Cost per click: Always On $0.0141 (highest)
    - Cost per click: Retargeting $0.0017 (lowest)

    You spent $645.00 for 120,000 clicks and 12 web conversions. First, Spring Sale grew. Second, Always On held. Finally, Retargeting fell.

    Today's data is partial. The conversion counts are tiny, so treat CPA with caution. Where to move budget is your team's decision.
    """,
    new LayoutOptions
    {
        Question = "Which campaign led the month?",
        Lexicon = lexicon,
    });
```

Blocks: Headline / Salience → KeyFigures / Figures → Heading / Markdown → Table / EntityPairs → Paragraph / Prose → Table / Template → Paragraph / Prose → Bullets / Enumeration → Callout (Warning) / Role → Callout (Info) / Role

<div style="font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px">
<p style="margin:0 0 12px;font-size:18px;font-weight:600;color:#111827">Spring Sale led the month.</p>
<table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:separate;border-spacing:8px 0">
<tr>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Spend</div>
<div style="font-size:20px;font-weight:700;color:#111827">$645.00</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Clicks</div>
<div style="font-size:20px;font-weight:700;color:#111827">120,000</div>
</td>
<td style="border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top">
<div style="font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280">Web conversions</div>
<div style="font-size:20px;font-weight:700;color:#111827">12</div>
</td>
</tr>
</table>
<h3 style="margin:16px 0 6px;font-size:16px;color:#111827">Campaigns</h3>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Campaign</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Spend</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Spring Sale</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$310.00</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Always On</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$240.00</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Retargeting</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$95.00</td>
</tr>
</tbody>
</table>
<p style="margin:0 0 12px">Costs per click:</p>
<table cellspacing="0" cellpadding="0" style="margin:0 0 12px;border-collapse:collapse;font-size:14px">
<thead>
<tr>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Campaign</th>
<th style="text-align:right;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Cost per click</th>
<th style="text-align:left;padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb">Detail</th>
</tr>
</thead>
<tbody>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Spring Sale</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$0.0065</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">(middle)</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Always On</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$0.0141</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">(highest)</td>
</tr>
<tr>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">Retargeting</td>
<td style="text-align:right;padding:6px 10px;border-bottom:1px solid #e5e7eb">$0.0017</td>
<td style="text-align:left;padding:6px 10px;border-bottom:1px solid #e5e7eb">(lowest)</td>
</tr>
</tbody>
</table>
<p style="margin:0 0 12px">You spent $645.00 for 120,000 clicks and 12 web conversions.</p>
<ul style="margin:0 0 12px;padding-left:20px">
<li>First, Spring Sale grew.</li>
<li>Second, Always On held.</li>
<li>Finally, Retargeting fell.</li>
</ul>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#fef3c7;color:#92400e">Today&#39;s data is partial. The conversion counts are tiny, so treat CPA with caution.</div>
<div style="margin:0 0 12px;padding:9px 12px;border-radius:6px;background:#eef2ff;color:#3730a3">Where to move budget is your team&#39;s decision.</div>
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
dotnet pack -c Release src/FrameworkQ.TextLayout -o artifacts
```

The tests cover each component, each repair on hand-written disorganised texts, the guarantees on
a sample set, and 500 randomly assembled texts.

## License

MIT. See [LICENSE](LICENSE).
