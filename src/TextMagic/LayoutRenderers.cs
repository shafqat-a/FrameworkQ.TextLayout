using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TextMagic;

public enum HtmlStyle
{
    /// <summary>al-* classes, styled by the app's AnswerLayout.css (theme tokens).</summary>
    Classes,
    /// <summary>Inline styles and tables only: survives email clients and PDF converters.</summary>
    Email,
}

/// <summary>
/// Turns a <see cref="LayoutDocument"/> into JSON (the API's <c>answer_layout</c> shape, which the
/// page renders), HTML, or plain text. Every piece of text is HTML-encoded; the only markup carried
/// through is **bold**.
/// </summary>
public static class LayoutRenderers
{
    // ── JSON ─────────────────────────────────────────────────────────────────

    /// <summary>The wire object: { blocks: [{ type, text, tone, items, metrics, columns, rows }] }.</summary>
    public static object ToWire(LayoutDocument doc) => new
    {
        blocks = doc.Blocks.Select(b => new
        {
            type = TypeName(b.Kind),
            text = b.Text,
            tone = b.Kind == BlockKind.Callout ? (b.Tone == CalloutTone.Warning ? "warning" : "info") : null,
            items = b.Items,
            metrics = b.Figures?.Select(f => new { label = f.Label, value = f.Value, note = f.Note, trend = f.Trend == Trend.None ? null : f.Trend.ToString().ToLowerInvariant() }),
            columns = b.Columns,
            rows = b.Rows,
        }),
    };

    public static string Json(LayoutDocument doc) => JsonSerializer.Serialize(ToWire(doc));

    public static string TypeName(BlockKind kind) => kind switch
    {
        BlockKind.KeyFigures => "metrics",
        _ => kind.ToString().ToLowerInvariant(),
    };

    // ── HTML ─────────────────────────────────────────────────────────────────

    public static string Html(LayoutDocument doc, HtmlStyle style = HtmlStyle.Classes)
    {
        var e = style == HtmlStyle.Email;
        var sb = new StringBuilder();
        sb.Append(e ? "<div style=\"font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.55;font-size:15px\">" : "<div class=\"al\">");
        foreach (var b in doc.Blocks)
        {
            switch (b.Kind)
            {
                case BlockKind.Headline:
                    sb.Append(e ? "<p style=\"margin:0 0 12px;font-size:18px;font-weight:600;color:#111827\">" : "<p class=\"al-headline\">")
                      .Append(Inline(b.Text)).Append("</p>");
                    break;
                case BlockKind.Heading:
                    sb.Append(e ? "<h3 style=\"margin:16px 0 6px;font-size:16px;color:#111827\">" : "<h3 class=\"al-heading\">")
                      .Append(Inline(b.Text)).Append("</h3>");
                    break;
                case BlockKind.Paragraph:
                    sb.Append(e ? "<p style=\"margin:0 0 12px\">" : "<p class=\"al-paragraph\">").Append(Inline(b.Text)).Append("</p>");
                    break;
                case BlockKind.Bullets:
                    sb.Append(e ? "<ul style=\"margin:0 0 12px;padding-left:20px\">" : "<ul class=\"al-bullets\">");
                    foreach (var i in b.Items ?? Array.Empty<string>()) sb.Append("<li>").Append(Inline(i)).Append("</li>");
                    sb.Append("</ul>");
                    break;
                case BlockKind.Callout:
                {
                    var warn = b.Tone == CalloutTone.Warning;
                    sb.Append(e
                        ? $"<div style=\"margin:0 0 12px;padding:9px 12px;border-radius:6px;background:{(warn ? "#fef3c7" : "#eef2ff")};color:{(warn ? "#92400e" : "#3730a3")}\">"
                        : $"<div class=\"al-callout {(warn ? "al-callout-warning" : "al-callout-info")}\" role=\"note\">")
                      .Append(Inline(b.Text)).Append("</div>");
                    break;
                }
                case BlockKind.KeyFigures:
                    if (e)
                    {
                        sb.Append("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" style=\"margin:0 0 12px;border-collapse:separate;border-spacing:8px 0\"><tr>");
                        foreach (var f in b.Figures ?? Array.Empty<KeyFigure>())
                            sb.Append("<td style=\"border:1px solid #e5e7eb;border-radius:8px;padding:8px 12px;vertical-align:top\">")
                              .Append("<div style=\"font-size:11px;font-weight:600;text-transform:uppercase;color:#6b7280\">").Append(Enc(f.Label)).Append("</div>")
                              .Append("<div style=\"font-size:20px;font-weight:700;color:#111827\">").Append(Enc(f.Value)).Append(Arrow(f.Trend, e)).Append("</div>")
                              .Append(f.Note is null ? "" : $"<div style=\"font-size:12px;color:#6b7280\">{Enc(f.Note)}</div>")
                              .Append("</td>");
                        sb.Append("</tr></table>");
                    }
                    else
                    {
                        sb.Append("<div class=\"al-metrics\">");
                        foreach (var f in b.Figures ?? Array.Empty<KeyFigure>())
                            sb.Append("<div class=\"al-metric\"><span class=\"al-metric-label\">").Append(Enc(f.Label)).Append("</span>")
                              .Append("<span class=\"al-metric-value\">").Append(Enc(f.Value)).Append(Arrow(f.Trend, e)).Append("</span>")
                              .Append(f.Note is null ? "" : $"<span class=\"al-metric-note\">{Enc(f.Note)}</span>")
                              .Append("</div>");
                        sb.Append("</div>");
                    }
                    break;
                case BlockKind.Table:
                {
                    var cols = b.Columns ?? Array.Empty<string>();
                    var rows = b.Rows ?? Array.Empty<IReadOnlyList<string>>();
                    var right = Enumerable.Range(0, cols.Count).Select(c => rows.Count > 0 && rows.All(r => c >= r.Count || r[c].Length == 0 || Numeric(r[c]))).ToList();
                    sb.Append(e ? "<table cellspacing=\"0\" cellpadding=\"0\" style=\"margin:0 0 12px;border-collapse:collapse;font-size:14px\">" : "<div class=\"al-table-wrap\"><table class=\"al-table\">");
                    sb.Append("<thead><tr>");
                    for (var c = 0; c < cols.Count; c++)
                        sb.Append(e ? $"<th style=\"text-align:{(right[c] ? "right" : "left")};padding:6px 10px;background:#f3f4f6;border-bottom:1px solid #e5e7eb\">" : $"<th{(right[c] ? " class=\"al-num\"" : "")}>")
                          .Append(Inline(cols[c])).Append("</th>");
                    sb.Append("</tr></thead><tbody>");
                    foreach (var r in rows)
                    {
                        sb.Append("<tr>");
                        for (var c = 0; c < cols.Count; c++)
                            sb.Append(e ? $"<td style=\"text-align:{(right[c] ? "right" : "left")};padding:6px 10px;border-bottom:1px solid #e5e7eb\">" : $"<td{(right[c] ? " class=\"al-num\"" : "")}>")
                              .Append(Inline(c < r.Count ? r[c] : "")).Append("</td>");
                        sb.Append("</tr>");
                    }
                    sb.Append(e ? "</tbody></table>" : "</tbody></table></div>");
                    break;
                }
            }
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    // ── Plain text ───────────────────────────────────────────────────────────

    public static string PlainText(LayoutDocument doc)
    {
        var sb = new StringBuilder();
        foreach (var b in doc.Blocks)
        {
            switch (b.Kind)
            {
                case BlockKind.KeyFigures:
                    sb.AppendLine(string.Join("  ·  ", (b.Figures ?? Array.Empty<KeyFigure>()).Select(f => $"{f.Label}: {f.Value}{(f.Note is null ? "" : $" ({f.Note})")}")));
                    break;
                case BlockKind.Bullets:
                    foreach (var i in b.Items ?? Array.Empty<string>()) sb.Append("• ").AppendLine(Strip(i));
                    break;
                case BlockKind.Table:
                {
                    var cols = b.Columns ?? Array.Empty<string>();
                    var rows = b.Rows ?? Array.Empty<IReadOnlyList<string>>();
                    var widths = cols.Select((c, i) => Math.Max(Strip(c).Length, rows.Select(r => i < r.Count ? Strip(r[i]).Length : 0).DefaultIfEmpty(0).Max())).ToList();
                    sb.AppendLine(string.Join("  ", cols.Select((c, i) => Strip(c).PadRight(widths[i]))).TrimEnd());
                    foreach (var r in rows) sb.AppendLine(string.Join("  ", cols.Select((_, i) => (i < r.Count ? Strip(r[i]) : "").PadRight(widths[i]))).TrimEnd());
                    break;
                }
                case BlockKind.Callout:
                    sb.Append(b.Tone == CalloutTone.Warning ? "! " : "Note: ").AppendLine(Strip(b.Text));
                    break;
                default:
                    sb.AppendLine(Strip(b.Text));
                    break;
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly Regex Bold = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);

    private static string Inline(string? text) => Bold.Replace(Enc(text ?? ""), "<strong>$1</strong>");
    private static string Enc(string s) => WebUtility.HtmlEncode(s);
    private static string Strip(string? s) => (s ?? "").Replace("**", "");

    private static string Arrow(Trend t, bool email) => t switch
    {
        Trend.Up => email ? " <span style=\"color:#059669;font-size:13px\">▲</span>" : " <span class=\"al-trend al-trend-up\">▲</span>",
        Trend.Down => email ? " <span style=\"color:#b91c1c;font-size:13px\">▼</span>" : " <span class=\"al-trend al-trend-down\">▼</span>",
        _ => "",
    };

    private static bool Numeric(string s) => Regex.IsMatch(s.Replace("**", "").Trim(), @"^[-+]?[$€£৳]?\s?[\d,]+(\.\d+)?\s?(%|[KMB]|x)?$");
}
