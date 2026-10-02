using System.Text.Json;

namespace TextMagic.Pages;

/// <summary>One filled-in call, used by the sample chips.</summary>
public sealed record Sample(string Name, string Text, string? Question, string Profile, string Lexicon, string? DataJson);

public static class Samples
{
    public static IReadOnlyList<Sample> All { get; } =
    [
        new(
            "Bakery",
            "The bakery took in $1,240.00 over the last 7 days for 860 loaves and 410 customers. " +
            "Sourdough sold the most, at 310 loaves of 860. " +
            "Note that Sunday's till is partial and excluded.",
            "What did the bakery take in over the last 7 days?",
            "Answer",
            "Bakery",
            null),
        new(
            "Loaves",
            "Sales were led by sourdough. By loaf: Sourdough took in $310.00, Rye $240.00 and Focaccia $95.00.",
            "Which loaf sold the most?",
            "Answer",
            "Bakery",
            null),
        new(
            "Channels",
            "Spend was led by TikTok. By channel: TikTok spent $310.00, Facebook $240.00 and Google Ads $95.00.",
            "Which channel spent the most?",
            "Answer",
            "Marketing",
            null),
        new(
            "Rows",
            "Spend was led by TikTok. Over the last 30 days TikTok spent $310.00, " +
            "Facebook spent $240.00 and Google Ads spent $95.00, while clicks were strongest on TikTok.",
            "Which channel spent the most?",
            "Answer",
            "Marketing",
            """
            [
              {
                "columns": ["PlatformKey", "TotalSpend_USD", "TotalClicks"],
                "rows": [
                  ["tiktok", 310.0012345, 48000],
                  ["facebook", 240.00, 17000],
                  ["google_ads", 95.0034, 55000]
                ]
              }
            ]
            """),
        new(
            "Cakes",
            """
            ## Weekend cakes

            Black Forest led the case. By cake: Black Forest took in $310.00, Lemon Tart took in $240.00 and Carrot Cake took in $95.00.

            Costs per cake:

            - Cost per cake: Black Forest $6.20 (middle)
            - Cost per cake: Lemon Tart $4.10 (lowest)
            - Cost per cake: Carrot Cake $7.50 (highest)

            The case took in $645.00 for 86 cakes and 120 customers. First, Black Forest grew. Second, Lemon Tart held. Finally, Carrot Cake fell.

            Today's count is partial. The Saturday slices are tiny, so treat the ranking with caution. Where to move the Sunday bake is your team's decision.
            """,
            "Which cake led the case?",
            "Answer",
            "Cakes",
            null),
        new(
            "Question",
            "Here is how the week was staffed. Two bakers covered the early shift. Sourdough sold the most, at 310 loaves of 860.",
            "Which loaf sold the most this week?",
            "Answer",
            "Bakery",
            null),
        new(
            "Alert",
            "Spend went up week over week: $151.20 in the last 7 days against $126.40 in the 7 days before, an increase of $24.80 (19.6%).",
            null,
            "Alert",
            "Marketing",
            null),
    ];

    public static Lexicon LexiconFor(string name) => name switch
    {
        "General" => Lexicon.General,
        "Bakery" => BakeryLexicon,
        "Cakes" => CakesLexicon,
        _ => Lexicon.Marketing,
    };

    public static LayoutProfile ProfileFor(string name) => name switch
    {
        "Insight" => LayoutProfile.Insight,
        "Alert" => LayoutProfile.Alert,
        _ => LayoutProfile.Answer,
    };

    public static HtmlStyle StyleFor(string name) =>
        name == "Email" ? HtmlStyle.Email : HtmlStyle.Classes;

    /// <summary>
    /// A JSON array of tables, or one table object: <c>{ "columns": [...], "rows": [[...]] }</c>.
    /// Numbers stay numbers so the engine can format them. Blank input means no source rows.
    /// </summary>
    public static IReadOnlyList<DataTable> ParseData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { throw new ArgumentException("Source rows are not valid JSON."); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object) return [ReadTable(root)];
            if (root.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Source rows must be a JSON array of tables, or one table object.");
            return root.EnumerateArray().Select(ReadTable).ToList();
        }
    }

    private static DataTable ReadTable(JsonElement table)
    {
        if (table.ValueKind != JsonValueKind.Object
            || !table.TryGetProperty("columns", out var columnsEl)
            || columnsEl.ValueKind != JsonValueKind.Array
            || !table.TryGetProperty("rows", out var rowsEl)
            || rowsEl.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Each table needs a \"columns\" array and a \"rows\" array.");

        var columns = columnsEl.EnumerateArray().Select(c => c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : c.ToString()).ToList();
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var row in rowsEl.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array) throw new ArgumentException("Each row must be an array of cells.");
            var cells = new List<object?>();
            foreach (var cell in row.EnumerateArray())
            {
                cells.Add(cell.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => cell.GetString(),
                    JsonValueKind.Number => Number(cell),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => throw new ArgumentException("A cell must be a string, a number, or null."),
                });
            }
            rows.Add(cells);
        }

        var name = table.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        return new DataTable(columns, rows) { Name = name };
    }

    private static object Number(JsonElement cell)
    {
        if (cell.TryGetInt32(out var i)) return i;
        if (cell.TryGetInt64(out var l)) return l;
        return cell.GetDecimal();
    }

    private static readonly Lexicon BakeryLexicon = Lexicon.General with
    {
        Entities =
        [
            new LexiconEntity("Sourdough", "loaf", ["sourdough"]),
            new LexiconEntity("Rye", "loaf", ["rye"]),
            new LexiconEntity("Focaccia", "loaf", ["focaccia"]),
        ],
        Metrics =
        [
            new LexiconMetric("Sales", ["sales", "sale", "took in", "takings"], MetricUnit.Money),
            new LexiconMetric("Loaves", ["loaves"], MetricUnit.Count),
            new LexiconMetric("Customers", ["customers", "customer"], MetricUnit.Count),
            new LexiconMetric("Unit cost", ["cost per loaf", "unit cost"], MetricUnit.Money),
        ],
    };

    private static readonly Lexicon CakesLexicon = BakeryLexicon.WithEntities(
    [
        new LexiconEntity("Black Forest", "cake", ["black forest"]),
        new LexiconEntity("Lemon Tart", "cake", ["lemon tart"]),
        new LexiconEntity("Carrot Cake", "cake", ["carrot cake"]),
    ]);
}
