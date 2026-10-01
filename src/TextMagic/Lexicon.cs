namespace TextMagic;

/// <summary>A thing the text talks about: a channel, campaign, segment. Matched by any alias, shown by name.</summary>
public sealed record LexiconEntity(string Name, string Kind, IReadOnlyList<string> Aliases);

/// <summary>A measure: its display label, the words that name it, and how its values are written.</summary>
public sealed record LexiconMetric(string Label, IReadOnlyList<string> Aliases, MetricUnit Unit = MetricUnit.Any);

public enum MetricUnit { Any, Money, Count, Percent, Ratio }

/// <summary>
/// The vocabulary the engine reads text with. Everything domain-specific lives here, so the engine
/// itself is not a marketing engine: pass another lexicon and it lays out any domain's text.
///
/// The cue lists follow two research traditions. Rhetorical roles come from cue phrases, as in
/// argumentative zoning (Teufel). Contrast and concession come from explicit discourse connectives,
/// as in the Penn Discourse Treebank. Both are lists of surface words, which is what makes the
/// engine deterministic.
/// </summary>
public sealed record Lexicon
{
    public IReadOnlyList<LexiconEntity> Entities { get; init; } = Array.Empty<LexiconEntity>();
    public IReadOnlyList<LexiconMetric> Metrics { get; init; } = Array.Empty<LexiconMetric>();

    /// <summary>A limitation or warning: data gaps, small samples, partial periods.</summary>
    public IReadOnlyList<string> CaveatCues { get; init; } = Array.Empty<string>();

    /// <summary>Within caveats, the ones serious enough to be a warning rather than a note.</summary>
    public IReadOnlyList<string> WarningCues { get; init; } = Array.Empty<string>();

    /// <summary>"This is your decision": a deliberate refusal to recommend.</summary>
    public IReadOnlyList<string> DecisionCues { get; init; } = Array.Empty<string>();

    /// <summary>How a figure is defined or computed.</summary>
    public IReadOnlyList<string> DefinitionCues { get; init; } = Array.Empty<string>();

    /// <summary>Explicit discourse connectives of contrast or concession (PDTB).</summary>
    public IReadOnlyList<string> ContrastConnectives { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> UpWords { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DownWords { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FlatWords { get; init; } = Array.Empty<string>();

    /// <summary>Words that mark a figure as the whole rather than a part ("total", "overall").</summary>
    public IReadOnlyList<string> TotalWords { get; init; } = Array.Empty<string>();

    /// <summary>The same lexicon with extra entities, typically the caller's campaign or segment names.</summary>
    public Lexicon WithEntities(IEnumerable<LexiconEntity> extra)
        => this with { Entities = Entities.Concat(extra).ToList() };

    /// <summary>A lexicon with nothing domain-specific: roles and directions only.</summary>
    public static Lexicon General { get; } = new()
    {
        CaveatCues = new[]
        {
            "note that", "note:", "keep in mind", "bear in mind", "caveat", "be aware", "one thing to keep in mind",
            "only just", "just a few", "very few", "tiny", "small sample", "small samples", "very little", "too small", "thin", "partial", "incomplete", "not complete",
            "limited to", "not available", "unavailable", "cannot", "can't", "could not", "couldn't", "missing",
            "gap", "gaps", "rests on", "rest on", "fair warning", "worth flagging", "a warning", "be careful", "thin", "noisy", "unstable", "estimate", "approximate", "not confirmed", "uncertain",
            "treat with caution", "with caution", "caution", "doesn't include", "does not include", "excluding",
            "not comparable", "no data", "lag", "lags", "stale", "older snapshot", "not yet", "not present",
            "no figures", "isn't in", "is not in", "not in the data", "not included", "no rows", "not shown",
        },
        WarningCues = new[]
        {
            "tiny", "small sample", "small samples", "very few", "too small", "partial", "incomplete", "not complete", "missing", "gap", "gaps",
            "unavailable", "not available", "cannot", "can't", "not confirmed", "stale", "not comparable", "no data",
            "unstable", "noisy", "rests on",
        },
        DecisionCues = new[]
        {
            "your call", "your team's call", "the team's call", "team's call", "team’s call", "your decision", "team's decision", "team’s decision", "decision for the team",
            "decision for your team", "up to you", "can't recommend", "cannot recommend", "won't recommend",
            "not something i can decide", "that's a decision", "that is a decision", "the call is yours",
        },
        DefinitionCues = new[]
        {
            "is calculated", "are calculated", "calculated as", "computed as", "is computed", "defined as", "means",
            "÷", " / ", "divided by", "is the share", "counts only", "is measured", "measured as",
        },
        ContrastConnectives = new[]
        {
            "however", "but", "although", "though", "while", "whereas", "yet", "on the other hand", "in contrast",
            "even though", "nevertheless", "still",
        },
        UpWords = new[]
        {
            "up", "rose", "risen", "rising", "increase", "increased", "increasing", "grew", "grown", "growing",
            "gain", "gained", "climbed", "jumped", "higher", "improved", "improving", "doubled",
        },
        DownWords = new[]
        {
            "down", "fell", "fallen", "falling", "decrease", "decreased", "decreasing", "declined", "declining",
            "dropped", "dropping", "lower", "shrank", "slipped", "dipped", "halved", "cut",
        },
        FlatWords = new[] { "flat", "unchanged", "steady", "stable", "level" },
        TotalWords = new[] { "total", "overall", "in all", "altogether", "combined", "across all", "all channels", "whole" },
    };

    /// <summary>A digital-marketing vocabulary: ad channels and the performance, web-analytics and ad-fraud metrics.</summary>
    public static Lexicon Marketing { get; } = General with
    {
        Entities = new[]
        {
            new LexiconEntity("Facebook", "channel", new[] { "facebook", "meta", "fb" }),
            new LexiconEntity("Instagram", "channel", new[] { "instagram", "ig" }),
            new LexiconEntity("TikTok", "channel", new[] { "tiktok", "tik tok" }),
            new LexiconEntity("Google Ads", "channel", new[] { "google ads", "google_ads", "googleads", "google" }),
            new LexiconEntity("YouTube", "channel", new[] { "youtube" }),
            new LexiconEntity("LinkedIn", "channel", new[] { "linkedin" }),
            new LexiconEntity("Snapchat", "channel", new[] { "snapchat" }),
            new LexiconEntity("Bing Ads", "channel", new[] { "bing ads", "microsoft ads" }),
        },
        Metrics = new[]
        {
            new LexiconMetric("Spend", new[] { "spend", "spent", "spending", "cost", "ad spend", "spend_usd" }, MetricUnit.Money),
            new LexiconMetric("Revenue", new[] { "revenue", "web revenue", "webrevenue", "webrevenue_usd" }, MetricUnit.Money),
            new LexiconMetric("Impressions", new[] { "impressions", "impression" }, MetricUnit.Count),
            new LexiconMetric("Clicks", new[] { "clicks", "click" }, MetricUnit.Count),
            new LexiconMetric("Reach", new[] { "reach", "unique reach", "people reached" }, MetricUnit.Count),
            new LexiconMetric("Frequency", new[] { "frequency" }, MetricUnit.Ratio),
            new LexiconMetric("CTR", new[] { "ctr", "click-through rate", "click through rate" }, MetricUnit.Percent),
            new LexiconMetric("CPC", new[] { "cpc", "cost per click" }, MetricUnit.Money),
            new LexiconMetric("CPM", new[] { "cpm", "cost per thousand", "cost per mille" }, MetricUnit.Money),
            new LexiconMetric("CPA", new[] { "cpa", "cost per conversion", "cost per acquisition", "cost per enquiry", "cost per lead", "cost per web conversion" }, MetricUnit.Money),
            new LexiconMetric("ROAS", new[] { "roas", "return on ad spend" }, MetricUnit.Ratio),
            new LexiconMetric("Web conversions", new[] { "web conversions", "site conversions", "site-tracked conversions", "webconversions", "web conversion", "enquiries", "enquiry" }, MetricUnit.Count),
            new LexiconMetric("Platform conversions", new[] { "platform-reported conversions", "platform conversions", "platform-attributed conversions", "adplatformconversions", "ad-platform conversions" }, MetricUnit.Count),
            new LexiconMetric("Conversions", new[] { "conversions", "conversion" }, MetricUnit.Count),
            new LexiconMetric("Conversion rate", new[] { "conversion rate", "cvr" }, MetricUnit.Percent),
            new LexiconMetric("Leads", new[] { "leads", "lead" }, MetricUnit.Count),
            new LexiconMetric("Sessions", new[] { "sessions", "web sessions", "websessions", "visits" }, MetricUnit.Count),
            new LexiconMetric("Engagements", new[] { "engagements", "ad engagements", "engagement" }, MetricUnit.Count),
            new LexiconMetric("Video views", new[] { "video views", "views" }, MetricUnit.Count),
            new LexiconMetric("Scored events", new[] { "scored events", "risk-scored events", "scored click events", "totalriskscoredevents" }, MetricUnit.Count),
            new LexiconMetric("Suspicious events", new[] { "suspicious events", "suspicious", "medhighcriticalriskevents" }, MetricUnit.Count),
            new LexiconMetric("High-risk events", new[] { "high-risk events", "high risk events", "high or critical", "highcriticalriskevents" }, MetricUnit.Count),
            new LexiconMetric("Suspicious rate", new[] { "suspicious rate", "invalid traffic rate" }, MetricUnit.Percent),
            new LexiconMetric("Campaigns", new[] { "campaigns", "ad campaigns" }, MetricUnit.Count),
        },
    };
}
