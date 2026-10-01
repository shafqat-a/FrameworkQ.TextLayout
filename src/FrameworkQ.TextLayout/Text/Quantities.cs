using System.Globalization;
using System.Text.RegularExpressions;

namespace FrameworkQ.TextLayout.Text;

public enum QuantityUnit { Number, Currency, Percent, PercentagePoints, Multiplier, Date, Year }

/// <summary>
/// One figure in the text: (value, unit, comparator) in the schema of Roy, Vieira and Roth (2015),
/// with its exact span so a layout can prove every figure it shows came from the text.
/// </summary>
public sealed record Quantity(int Start, int Length, string Raw, decimal Value, QuantityUnit Unit, string? Currency, int Decimals, decimal Scale)
{
    public int End => Start + Length;

    /// <summary>Dates and years locate figures; they are not figures to show.</summary>
    public bool IsMeasure => Unit is not (QuantityUnit.Date or QuantityUnit.Year);
}

/// <summary>
/// Recognises figures as written in reporting prose: $645.00, 120,000, 48.7%, +2.4 pp, 13.2x,
/// 3.58M, BDT 1,250.75, 2026-09-26, 28 Aug, ranges like 0–6. Deterministic, culture-invariant,
/// span-exact.
/// </summary>
public static class QuantityRecognizer
{
    private const string Num = @"(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?";

    private static readonly Regex IsoDate = new(@"\b(19|20)\d{2}-\d{2}-\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex DayMonth = new(
        @"\b(\d{1,2})\s+(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\b|\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\.?\s+(\d{1,2})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Figure = new(
        @"(?<![\w.])(?<sign>[+\-−])?(?<cur>[$€£৳]|(?:USD|BDT|EUR|GBP|AED)\s?)?(?<num>" + Num + @")(?:\s?(?<scale>[KkMmBb])(?![a-zA-Z]))?" +
        @"(?:\s?(?<unit>%|pp\b|x\b|×|percentage points|percent)|\s(?<cur2>USD|BDT|EUR|GBP|AED)\b)?",
        RegexOptions.Compiled);

    public static IReadOnlyList<Quantity> Find(string text)
    {
        var found = new List<Quantity>();
        if (string.IsNullOrEmpty(text)) return found;

        foreach (Match m in IsoDate.Matches(text))
            found.Add(new Quantity(m.Index, m.Length, m.Value, 0m, QuantityUnit.Date, null, 0, 1m));
        foreach (Match m in DayMonth.Matches(text))
            if (!Overlaps(found, m.Index, m.Length))
                found.Add(new Quantity(m.Index, m.Length, m.Value, 0m, QuantityUnit.Date, null, 0, 1m));

        foreach (Match m in Figure.Matches(text))
        {
            if (Overlaps(found, m.Index, m.Length)) continue;
            var numText = m.Groups["num"].Value.Replace(",", "");
            if (!decimal.TryParse(numText, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) continue;

            var decimals = numText.Contains('.') ? numText.Length - numText.IndexOf('.') - 1 : 0;
            var scale = m.Groups["scale"].Success
                ? char.ToLowerInvariant(m.Groups["scale"].Value[0]) switch { 'k' => 1_000m, 'm' => 1_000_000m, _ => 1_000_000_000m }
                : 1m;
            if (m.Groups["sign"].Value is "-" or "−") v = -v;

            var currency = m.Groups["cur"].Success ? m.Groups["cur"].Value.Trim()
                         : m.Groups["cur2"].Success ? m.Groups["cur2"].Value : null;
            var unitText = m.Groups["unit"].Value;
            var unit = currency is not null ? QuantityUnit.Currency
                     : unitText is "%" or "percent" ? QuantityUnit.Percent
                     : unitText is "pp" or "percentage points" ? QuantityUnit.PercentagePoints
                     : unitText is "x" or "×" ? QuantityUnit.Multiplier
                     : (scale == 1m && decimals == 0 && v is >= 1990 and <= 2100 && !numText.Contains(',')) ? QuantityUnit.Year
                     : QuantityUnit.Number;

            found.Add(new Quantity(m.Index, m.Length, m.Value.TrimEnd(), v * scale, unit, currency, decimals, scale));
        }
        return found.OrderBy(q => q.Start).ToList();
    }

    /// <summary>
    /// True when <paramref name="shown"/> is <paramref name="source"/> as written or rounded at
    /// the precision shown: 146.5K is 120,000; $604 is $645.00.
    /// </summary>
    public static bool SameFigure(decimal source, Quantity shown)
    {
        if (Math.Abs(source) == Math.Abs(shown.Value)) return true;
        var rounded = Math.Round(Math.Abs(source) / shown.Scale, shown.Decimals, MidpointRounding.AwayFromZero) * shown.Scale;
        return rounded == Math.Abs(shown.Value);
    }

    private static bool Overlaps(List<Quantity> list, int start, int length)
        => list.Any(q => start < q.End && q.Start < start + length);
}
