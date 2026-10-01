using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using TextMagic;
using TextMagic.Analysis;
using TextMagic.Text;
using Xunit;

namespace TextMagic.Tests;

/// <summary>
/// TextMagic: each algorithm on its own, the repairs it makes to disorganised text, and
/// the two guarantees on a sample set and on randomly generated text.
/// </summary>
public sealed class TextLayoutTests
{
    // ── Components ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sentences_split_at_real_boundaries_only()
    {
        SentenceSegmenter.Split("Spend was $1.5M in Q3. CPC fell to $0.0048, e.g. on TikTok. Dates use 2026-09-26. Done")
            .Should().Equal("Spend was $1.5M in Q3.", "CPC fell to $0.0048, e.g. on TikTok.", "Dates use 2026-09-26.", "Done");
        SentenceSegmenter.Split("TikTok spent $310.00 (48.7% of spend. More below). Next one.")
            .Should().HaveCount(2, "a full stop inside brackets does not end the sentence");
    }

    [Fact]
    public void Quantities_carry_value_unit_and_exact_span()
    {
        var q = QuantityRecognizer.Find("You spent $645.00 for 120,000 clicks, 4.1% CTR, 13.2x ROAS, +2.4 pp, 3.58M impressions and BDT 1,250.75 on 2026-09-26 (28 Aug), in 2026.");

        q.Where(x => x.IsMeasure).Select(x => (x.Raw, x.Value, x.Unit)).Should().Equal(
            ("$645.00", 645.00m, QuantityUnit.Currency), ("120,000", 120000m, QuantityUnit.Number),
            ("4.1%", 4.1m, QuantityUnit.Percent), ("13.2x", 13.2m, QuantityUnit.Multiplier),
            ("+2.4 pp", 2.4m, QuantityUnit.PercentagePoints), ("3.58M", 3_580_000m, QuantityUnit.Number),
            ("BDT 1,250.75", 1250.75m, QuantityUnit.Currency));
        q.Count(x => x.Unit == QuantityUnit.Date).Should().Be(2);
        q.Should().Contain(x => x.Unit == QuantityUnit.Year && x.Raw == "2026");
        QuantityRecognizer.SameFigure(3_581_204m, q.Single(x => x.Raw == "3.58M")).Should().BeTrue("3.58M is 3,581,204 rounded");
        QuantityRecognizer.SameFigure(640m, q.Single(x => x.Raw == "$645.00")).Should().BeFalse();
    }

    [Fact]
    public void Phrases_match_whole_words_longest_first()
    {
        var m = new PhraseMatcher<string>(new[] { ("google", "g"), ("google ads", "ga"), ("meta", "fb"), ("conversions", "c"), ("web conversions", "wc") });

        m.FindAll("Google Ads beat Meta; metadata aside, web conversions rose").Select(h => h.Value)
            .Should().Equal("ga", "fb", "wc");
    }

    [Fact]
    public void Lexrank_ranks_the_sentence_others_agree_with_highest()
    {
        var bags = new[] { "spend clicks tiktok", "spend clicks facebook", "spend clicks google", "weather today" }
            .Select(s => Salience.Tokens(s).GroupBy(t => t).ToDictionary(g => g.Key, g => (double)g.Count())).ToList();

        var c = Salience.LexRank(bags);

        c[3].Should().BeLessThan(c[0], "an isolated sentence is not central");
    }

    [Fact]
    public void Text_tiling_breaks_a_wall_of_text_at_its_topic_shift()
    {
        var text = string.Join(" ",
            "TikTok spend rose to $310.00 this month.", "TikTok clicks reached 48,000 at a low cost.", "TikTok also drove 4 web conversions.",
            "TikTok remains the largest channel by spend.",
            "Fraud screening flagged 213 high-risk events.", "Fraud risk events were concentrated on bot traffic.", "Most fraud risk events came from one layer.",
            "Fraud screening covered every day of the window.");

        var doc = TextLayoutEngine.Format(text, new LayoutOptions { Profile = LayoutProfile.Insight, Lexicon = Lexicon.General });

        doc.IsFallback.Should().BeFalse();
        doc.Blocks.Count(b => b.Kind == BlockKind.Paragraph).Should().BeGreaterThan(1, "the TikTok and fraud halves become separate paragraphs");
    }

    // ── Repairs to disorganised text ────────────────────────────────────────────────────────

    [Fact]
    public void A_buried_answer_is_moved_to_the_headline()
    {
        const string text = "Here is some background on how the account is set up. Campaigns run on three platforms. "
                          + "TikTok spent the most in the last 30 days, at $310.00 of $645.00.";

        var doc = TextLayoutEngine.Format(text, new LayoutOptions { Question = "Which channel spent the most in the last 30 days?" });

        doc.Blocks[0].Kind.Should().Be(BlockKind.Headline);
        doc.Blocks[0].Text.Should().StartWith("TikTok spent the most");
    }

    [Fact]
    public void A_comparison_written_as_prose_becomes_a_table_that_replaces_it()
    {
        const string text = "Spend was led by TikTok. By channel: TikTok spent $310.00, Facebook $240.00 and Google Ads $95.00.";

        var doc = TextLayoutEngine.Format(text);

        var table = doc.Blocks.Single(b => b.Kind == BlockKind.Table);
        table.Origin.Should().Be(BlockOrigin.EntityPairs);
        table.Columns.Should().Equal("Channel", "Spend");
        table.Rows!.Select(r => (r[0], r[1])).Should().Equal(("TikTok", "$310.00"), ("Facebook", "$240.00"), ("Google Ads", "$95.00"));
        LayoutRenderers.PlainText(doc).Should().NotContain("Facebook $240.00 and Google Ads", "the listing sentence is replaced, not repeated");
    }

    [Fact]
    public void Workings_in_brackets_become_a_detail_column()
    {
        const string text = "Cost per click worked out at $0.0015 for Google Ads ($95.00 ÷ 55,000), $0.0048 for TikTok ($310.00 ÷ 48,000) "
                          + "and $0.0086 for Facebook ($240.00 ÷ 17,000).";

        var table = TextLayoutEngine.Format("CPC differs a lot by channel. " + text).Blocks.Single(b => b.Kind == BlockKind.Table);

        table.Columns.Should().Equal("Channel", "CPC", "Detail");
        table.Rows![0].Should().Equal("Google Ads", "$0.0015", "($95.00 ÷ 55,000)");
    }

    [Fact]
    public void Repeated_list_items_become_one_table()
    {
        const string text = "Costs per click:\n\n- Cost per click: Google Ads $0.0015 (lowest)\n- Cost per click: TikTok $0.0048 (middle)\n- Cost per click: Facebook $0.0086 (highest)";

        var table = TextLayoutEngine.Format(text).Blocks.Single(b => b.Kind == BlockKind.Table);

        table.Origin.Should().Be(BlockOrigin.Template);
        table.Columns.Should().Equal("Channel", "Cost per click", "Detail");
        table.Rows![2].Should().Equal("Facebook", "$0.0086", "(highest)");
    }

    [Fact]
    public void Caveats_and_decision_notes_move_to_callouts_in_the_writers_order()
    {
        const string text = "You spent $645.00 over the last 30 days. Note that today's data is partial and excluded. "
                          + "Clicks reached 120,000. The conversion counts are tiny, so treat CPA with caution. "
                          + "Where to move budget is your team's decision.";

        var doc = TextLayoutEngine.Format(text);

        var callouts = doc.Blocks.Where(b => b.Kind == BlockKind.Callout).ToList();
        callouts.Should().HaveCount(3);
        callouts[0].Text.Should().StartWith("Note that today's data is partial");
        callouts[1].Tone.Should().Be(CalloutTone.Warning, "tiny counts are a warning");
        callouts[2].Text.Should().Contain("your team's decision");
        doc.Blocks.Last().Kind.Should().Be(BlockKind.Callout);
    }

    [Fact]
    public void An_answer_that_declines_to_decide_keeps_that_as_its_headline()
    {
        const string text = "Where the extra budget goes is your team's call. You spent $645.00 in the last 30 days, and Google Ads had 6 web conversions.";

        TextLayoutEngine.Format(text).Blocks[0].Text.Should().StartWith("Where the extra budget goes is your team's call");
    }

    [Fact]
    public void Ordinal_and_semicolon_enumerations_become_lists()
    {
        var doc = TextLayoutEngine.Format("Spend rose. First, TikTok grew. Second, Facebook held. Finally, Google Ads fell.");
        doc.Blocks.Should().Contain(b => b.Kind == BlockKind.Bullets && b.Origin == BlockOrigin.Enumeration && b.Items!.Count == 3);

        var semi = TextLayoutEngine.Format("Spend rose by $10.50. Three things stand out: TikTok grew; Facebook held; Google Ads fell.");
        semi.Blocks.Should().Contain(b => b.Kind == BlockKind.Bullets && b.Items!.SequenceEqual(new[] { "TikTok grew", "Facebook held", "Google Ads fell" }));
    }

    [Fact]
    public void A_repeated_sentence_is_shown_once()
    {
        var doc = TextLayoutEngine.Format("You spent $645.00 last month. TikTok led spend. You spent $645.00 last month.");

        LayoutRenderers.PlainText(doc).Split("You spent $645.00 last month.").Length.Should().Be(2, "one occurrence");
    }

    [Fact]
    public void Key_figures_are_totals_on_different_metrics_never_one_channels_share()
    {
        const string text = "You spent $645.00 over the last 30 days for 120,000 clicks and 10 web conversions. "
                          + "TikTok spent $310.00 and Facebook spent $240.00.";

        var cards = TextLayoutEngine.Format(text).Blocks.Single(b => b.Kind == BlockKind.KeyFigures).Figures!;

        cards.Select(c => (c.Label, c.Value)).Should().Equal(("Spend", "$645.00"), ("Clicks", "120,000"), ("Web conversions", "10"));
    }

    [Fact]
    public void A_markdown_tables_total_row_supplies_the_cards()
    {
        const string text = "Spend by channel:\n\n| Channel | Spend | Clicks |\n|---|---|---|\n| TikTok | $310.00 | 48,000 |\n| Facebook | $240.00 | 17,000 |\n| **Total** | **$550.00** | **65,000** |";

        var doc = TextLayoutEngine.Format(text);

        doc.Blocks.Single(b => b.Kind == BlockKind.KeyFigures).Figures!.Select(f => (f.Label, f.Value))
            .Should().Equal(("Spend", "$550.00"), ("Clicks", "65,000"));
        doc.Blocks.Single(b => b.Kind == BlockKind.Table).Origin.Should().Be(BlockOrigin.Markdown);
    }

    [Fact]
    public void Data_rows_the_text_quotes_become_a_table_of_exact_values()
    {
        const string text = "Over the last 30 days TikTok spent $310.00, Facebook spent $240.00 and Google Ads spent $95.00, while clicks were strongest on TikTok.";
        var data = new DataTable(new[] { "PlatformKey", "TotalSpend_USD", "TotalClicks" }, new[]
        {
            new object?[] { "tiktok", 310.0012345m, 48000L },
            new object?[] { "facebook", 240.00m, 17000L },
            new object?[] { "google_ads", 95.0034m, 55000L },
        });

        var doc = TextLayoutEngine.Format("Spend was led by TikTok. " + text, new LayoutOptions { Data = new[] { data } });

        var table = doc.Blocks.Single(b => b.Kind == BlockKind.Table);
        table.Origin.Should().Be(BlockOrigin.DataTable);
        table.Columns.Should().Equal("Channel", "Spend");
        table.Rows!.Select(r => (r[0], r[1])).Should().Equal(("TikTok", "$310.00"), ("Facebook", "$240.00"), ("Google Ads", "$95.00"));
        doc.IsFallback.Should().BeFalse();
    }

    // ── Safety ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Html_is_encoded_and_only_bold_survives()
    {
        var doc = TextLayoutEngine.Format("You spent **$645.00** <script>alert(1)</script> last month.");

        var html = LayoutRenderers.Html(doc);
        html.Should().Contain("<strong>$645.00</strong>").And.Contain("&lt;script&gt;").And.NotContain("<script>");
        LayoutRenderers.Html(doc, HtmlStyle.Email).Should().Contain("style=\"").And.NotContain("class=");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("|||\n|-|\n|")]
    [InlineData("((((((( $ % 1,2,3 ))))")]
    public void Odd_input_never_throws(string text)
    {
        var act = () => TextLayoutEngine.Format(text);
        act.Should().NotThrow();
    }

    [Fact]
    public void The_json_shape_is_the_one_the_page_renders()
    {
        var json = JsonDocument.Parse(LayoutRenderers.Json(TextLayoutEngine.Format("You spent $645.00 for 120,000 clicks. Note: today is partial."))).RootElement;

        var blocks = json.GetProperty("blocks").EnumerateArray().ToList();
        blocks[0].GetProperty("type").GetString().Should().Be("headline");
        blocks.Select(b => b.GetProperty("type").GetString()).Should().Contain(new[] { "metrics", "callout" });
        blocks[0].EnumerateObject().Select(p => p.Name).Should().Equal("type", "text", "tone", "items", "metrics", "columns", "rows");
    }

    // ── The invariants, on the sample set and on generated text ────────────────────────────

    public static IEnumerable<object[]> Samples()
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples.json"))).RootElement
            .EnumerateArray().Select(e => new object[] { e.GetProperty("id").GetString()!, e.GetProperty("question").GetString()!, e.GetProperty("text").GetString()! });

    [Theory]
    [MemberData(nameof(Samples))]
    public void Every_sample_is_laid_out_without_losing_a_sentence_or_inventing_a_figure(string id, string question, string text)
    {
        var doc = TextLayoutEngine.Format(text, new LayoutOptions { Question = question });

        doc.IsFallback.Should().BeFalse($"{id}: {string.Join(" | ", doc.Diagnostics)}");
        doc.Blocks.Should().NotBeEmpty();
        LayoutRenderers.Json(TextLayoutEngine.Format(text, new LayoutOptions { Question = question }))
            .Should().Be(LayoutRenderers.Json(doc), "deterministic");
    }

    /// <summary>
    /// Text assembled at random from the building blocks real answers use (leads, figures, channel
    /// listings, workings, caveats, decision notes, lists, tables): the engine must never throw, and
    /// whenever it does not fall back, both guarantees hold (Verifier runs inside Format).
    /// </summary>
    [Fact]
    public void Random_texts_never_break_the_guarantees()
    {
        var rng = new Random(20261001);
        string[] channels = { "TikTok", "Facebook", "Google Ads", "YouTube", "Instagram" };
        string Money() => "$" + (rng.Next(1, 90000) / 100m).ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture);
        string Count() => rng.Next(1, 200000).ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture);
        var parts = new Func<string>[]
        {
            () => $"You spent {Money()} over the last 30 days.",
            () => $"{channels[rng.Next(channels.Length)]} spent the most at {Money()}.",
            () => $"By channel: {channels[0]} {Money()}, {channels[1]} {Money()} and {channels[2]} {Money()}.",
            () => $"Clicks reached {Count()} and web conversions {rng.Next(0, 40)}.",
            () => $"Cost per click was $0.00{rng.Next(10, 99)} for {channels[3]} ({Money()} ÷ {Count()}), $0.00{rng.Next(10, 99)} for {channels[4]} ({Money()} ÷ {Count()}) and $0.0{rng.Next(10, 99)} for {channels[1]} ({Money()} ÷ {Count()}).",
            () => "Note that today's data is partial.",
            () => $"The conversion counts are tiny, so treat CPA with caution.",
            () => "Where the budget goes is your team's decision.",
            () => $"First, {channels[0]} grew. Second, {channels[1]} held. Finally, {channels[2]} fell.",
            () => $"\n\n| Channel | Spend |\n|---|---|\n| {channels[0]} | {Money()} |\n| {channels[1]} | {Money()} |\n| **Total** | **{Money()}** |\n\n",
            () => $"\n\n- Cost per click: {channels[0]} {Money()} (a)\n- Cost per click: {channels[1]} {Money()} (b)\n- Cost per click: {channels[2]} {Money()} (c)\n\n",
            () => "Spend rose 12.5% while CTR fell 0.4 pp, and ROAS reached 3.2x.",
        };

        var fallbacks = 0;
        for (var n = 0; n < 500; n++)
        {
            var text = string.Join(" ", Enumerable.Range(0, rng.Next(1, 9)).Select(_ => parts[rng.Next(parts.Length)]()));
            LayoutDocument doc = null!;
            var act = () => doc = TextLayoutEngine.Format(text);
            act.Should().NotThrow(text);
            if (doc.IsFallback) fallbacks++;
        }
        fallbacks.Should().BeLessThan(25, "almost every generated text is laid out, not passed through");
    }

    [Fact]
    public void Laying_out_takes_milliseconds()
    {
        var samples = Samples().Select(c => ((string)c[1], (string)c[2])).ToList();
        foreach (var (q, t) in samples) TextLayoutEngine.Format(t, new LayoutOptions { Question = q }); // warm-up
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++) foreach (var (q, t) in samples) TextLayoutEngine.Format(t, new LayoutOptions { Question = q });
        (sw.Elapsed.TotalMilliseconds / (10.0 * samples.Count)).Should().BeLessThan(20, "mean per text, with a wide margin for CI machines");
    }
}
