# Algorithms

FrameworkQ.TextLayout chains classical, unsupervised methods. No model is trained or called;
every step is a rule or a closed-form computation, so the same input always gives the same layout.

## What each step is for

Each problem below is a known research problem with a standard rule-based or unsupervised
solution. The design chains them; no single algorithm solves the whole task.

| Problem | Algorithm | Source |
|---|---|---|
| Read the structure the writer already gave (tables, lists, emphasis) | CommonMark parsing into an AST | Markdig, a .NET CommonMark processor with a full AST ([github](https://github.com/xoofx/markdig)) |
| Split prose into sentences correctly around "$1.5M", "e.g.", "2026-09-26" and "3.4K" | Punkt-style unsupervised boundary detection (Kiss and Strunk, 2006), plus an abbreviation list and protected-token rules | Standard; implemented here as rules |
| Find every quantity, with value, unit and comparator | Rule-based quantity recognition: numbers, currency, percentages, dates and ranges | Microsoft.Recognizers.Text (MIT, .NET) ([github](https://github.com/microsoft/Recognizers-Text)); quantity schema (value, unit, change) from Roy, Vieira and Roth, 2015 ([CQE overview](https://aclanthology.org/anthology-files/pdf/emnlp/2023.emnlp-main.793.pdf)) |
| Find the things being talked about (channels, campaigns, metrics, segments) | Gazetteer matching with Aho–Corasick multi-pattern search over a caller-supplied lexicon, with alias normalisation (`google_ads` → Google Ads) | Classic IE; the lexicon comes from the caller |
| Decide what each sentence is doing (the answer, a figure, a comparison, context, a caveat, a decision note, a definition) | **Argumentative-zoning-style** rhetorical classification with cue phrases | Teufel's argumentative zoning: sentence roles from 157 cue phrases and features ([RAZ](https://github.com/WING-NUS/RAZ), [Teufel and Kan](https://people.cs.pitt.edu/~litman/courses/nus062615/readings/TeufelKan.pdf)) |
| Detect contrast, concession and cause | **Explicit discourse connectives** lexicon (however, but, while, because, only, note that…) | Penn Discourse Treebank: 100 explicit connectives, disambiguated by Pitler and Nenkova, 2009 ([PDTB parser](https://arxiv.org/pdf/1011.0835)) |
| Find the sentence that is "the answer" | Salience = **position prior** + **query overlap** + **centrality** + numeric density | Lead bias ([Lead-3](https://arxiv.org/pdf/1912.11602)); TextRank ([Mihalcea and Tarau, 2004](https://web.eecs.umich.edu/~mihalcea/papers/mihalcea.emnlp04.pdf)); LexRank ([Erkan and Radev, 2004](https://arxiv.org/pdf/1109.2128)) |
| Turn "TikTok $310.00, Facebook $240.00 and Google Ads $95.00" into a table | **Entity–quantity pairing** in coordinated clauses, then pivot into an entity × metric matrix | Quantity attachment as in Roy et al.; alignment as in BriQ ([Ibrahim et al., 2019](https://www.khoury.northeastern.edu/~mirek/papers/2019-ICDE-BriQ.pdf)) |
| Turn repeated parallel sentences ("CPC was $X for A… $Y for B…") into a table column | **Template induction**: mask entities and quantities, then group sentences whose skeletons match (token Jaccard ≥ 0.8 or edit distance) | Standard template or pattern induction |
| Build tables from the actual data when the caller has it (the query results an answer was written from) | **Text–table quantity alignment**: map each figure in the text to a cell, keep the matched columns, render rows with exact values | BriQ, bridging quantities in tables and text ([PDF](https://www.khoury.northeastern.edu/~mirek/papers/2019-ICDE-BriQ.pdf)) |
| Split long text (reports, insights) into sections | **TextTiling**: block-wise lexical cohesion, with boundaries at similarity valleys | [Hearst, 1997](https://aclanthology.org/J97-1003.pdf) |
| Remove repetition | **Maximal Marginal Relevance** and near-duplicate detection (shingle Jaccard) | Carbonell and Goldstein, 1998 ([paper](https://www.researchgate.net/publication/2269571_The_Use_of_MMR_Diversity-Based_Reranking_for_Reordering_Documents_and_Producing_Summaries)) |
| Pick 2–4 key figures for the cards, without picking two versions of the same metric | Quantity salience score, then **MMR** over metric type for diversity | as above |
| Mark a card ▲ or ▼ | Direction lexicon (rose, fell, up, down…) within the clause, plus the sign of the change | Same idea as `RichText` on the page |

## Pipeline

```
text ─► 1 Parse (Markdig AST) ─► 2 Segment (paragraphs → sentences, protected tokens)
     ─► 3 Annotate (quantities · entities · metrics · connectives · direction)
     ─► 4 Classify roles (cue phrases + features → ANSWER | FIGURE | COMPARISON |
                          CONTEXT | CAVEAT | DECISION | DEFINITION | OTHER)
     ─► 5 Score salience (position + query overlap + LexRank + numeric density)
     ─► 6 Induce structure (existing markdown › data-aligned tables › entity–quantity
                            tables › template tables › prose lists)
     ─► 7 Sections (TextTiling, long text only) and dedupe (MMR, near-duplicates)
     ─► 8 Select key figures (salience + MMR over metric type, trend from direction)
     ─► 9 Assemble (layout grammar) ─► 10 Verify invariants ─► LayoutDocument
                                                              │
                         renderers: JSON (answer_layout) · HTML (email/PDF) · plain text
```

**Assembly grammar** (step 9): headline → key figures → body (sections of paragraphs, tables
and lists, in source order) → callouts (caveats as warnings, context as info) → decision note.
Constraints:
- a table needs at least 3 rows, and at most 8 columns and 40 rows;
- at most 4 key figures;
- anything that matches no rule stays a paragraph in its original position.

**Verification** (step 10), which fails closed and falls back to paragraphs:
- every sentence is covered exactly once (R3);
- every figure shown traces back to a source span or a data cell (R4);
- formatting already-formatted text gives the same layout (idempotence);
- the output is deterministic.

