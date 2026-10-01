# Changelog

## 1.0.1 — 2026-10-02

Renamed the project from FrameworkQ.TextLayout to TextMagic.

- Repository, package id, assembly and namespace are `TextMagic`.
- No layout behaviour changed.

## 1.0.0 — 2026-10-01

First release.

- `TextLayoutEngine.Format`: headline, key figures, headings, paragraphs, lists, tables and
  callouts, from plain or markdown text, with optional question, data tables, profile and lexicon.
- Verified layouts: no lost sentence and no invented figure, or a flagged fallback.
- Renderers: JSON, HTML (class-based or inline-styled for email), plain text.
- `Lexicon.Marketing` and `Lexicon.General`.
- Targets .NET 8 and .NET 10.
