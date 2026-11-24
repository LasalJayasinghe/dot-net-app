using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using dotnetApp.Domain.Entities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace dotnetApp.Application.Services.Fundamentals;

/// <summary>What <see cref="CseReportExtractor"/> read from one interim / annual report.</summary>
public sealed class ReportExtraction
{
    /// <summary>Net assets per share for the group (consolidated) where the report has one.</summary>
    public decimal? NavPerShare { get; set; }

    /// <summary>"Group", "Company" or "Single" — which statement <see cref="NavPerShare"/> came from.</summary>
    public string? NavEntity { get; set; }

    public FundamentalStatus NavStatus { get; set; }

    /// <summary>Equity attributable to the parent's owners, as printed (in the report's own unit).</summary>
    public decimal? EquityAsPrinted { get; set; }

    /// <summary>Multiplier from the printed unit to rupees (1, 1,000 or 1,000,000), when it could be inferred.</summary>
    public decimal? EquityUnit { get; set; }

    /// <summary>Equity (in rupees) ÷ shares in issue — the independent check on <see cref="NavPerShare"/>.</summary>
    public decimal? NavFromEquity { get; set; }

    /// <summary>Percentage of shares held by the public (CSE minimum-float disclosure).</summary>
    public decimal? PublicFloatPercent { get; set; }

    // ── Earnings per share (basic, attributable to the parent's owners) ──

    /// <summary>"Group", "Company" or "Single" — which income statement the EPS figures came from.</summary>
    public string? EpsEntity { get; set; }

    public FundamentalStatus EpsStatus { get; set; }

    /// <summary>Length of the year-to-date period in months (3, 6, 9 or 12); null when the report has no YTD column.</summary>
    public int? EpsYtdMonths { get; set; }

    public decimal? EpsYtdCurrent { get; set; }

    /// <summary>The same YTD period one year earlier (as printed in this report).</summary>
    public decimal? EpsYtdPrior { get; set; }

    public decimal? EpsQuarterCurrent { get; set; }
    public decimal? EpsQuarterPrior { get; set; }

    /// <summary>The last full financial year's EPS, when the report prints it beside the YTD columns.</summary>
    public decimal? EpsLastFullYear { get; set; }

    /// <summary>Human-readable trail of what was found and why a status was given.</summary>
    public List<string> Notes { get; } = new();
}

/// <summary>
/// Reads headline figures out of a CSE interim / annual financial statement PDF.
///
/// Plain text extraction scrambles these reports (labels drift away from their numbers), so this works
/// on word positions instead: words are grouped into rows by baseline, numbers are assigned to the
/// Group / Company / Bank column whose header sits above them, and the result is cross-checked against
/// equity ÷ shares in issue. Anything that can't be confirmed is reported as <see cref="FundamentalStatus.Unverified"/>
/// rather than silently trusted.
/// </summary>
public static class CseReportExtractor
{
    /// <summary>NAV and equity ÷ shares must agree within this fraction to count as verified.</summary>
    private const decimal VerifyTolerance = 0.03m;

    private static readonly Regex NavLabel = new(
        @"net\s+assets?\s+(value\s+)?per\s+(an\s+)?(ordinary\s+)?share",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Not anchored at the start: margin text (a date, "Colombo") sometimes shares the row's baseline.
    private static readonly Regex EquityAttributableLabel = new(
        @"\bequity\s+attributable\s+to\s+(the\s+)?(equity\s+holders|owners|shareholders|ordinary\s+shareholders)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TotalEquityLabel = new(
        @"\btotal\s+(shareholders'?\s+)?equity\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>An "attributable to…" label that already names its owner (so it is not wrapped).</summary>
    private static readonly Regex CompleteOwnerLabel = new(
        @"(parent|company|bank|group|holders|owners|shareholders|entity)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NonControllingLabel = new(
        @"non[\s-]*controlling\s+interests?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PublicFloatLabel = new(
        @"public\s+(share)?holding",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A decimal percentage within the same sentence as "public holding" / "held by the public".</summary>
    private static readonly Regex PublicFloatSentence = new(
        @"(?:percentage\s+of\s+(?:the\s+)?public\s+(?:share)?holding|held\s+by\s+the\s+public|public\s+holding\s+of\s+the\s+company).{0,80}?(\d{1,3}\.\d+)\s*%",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumberPattern = new(
        @"^\(?-?\d[\d,]*(\.\d+)?\)?%?$",
        RegexOptions.Compiled);

    private static readonly string[] GroupWords = { "group", "consolidated" };
    private static readonly string[] CompanyWords = { "company", "bank" };

    /// <param name="pdf">The report.</param>
    /// <param name="sharesInIssue">All share classes (voting + non-voting) — the denominator for the cross-checks.</param>
    /// <param name="ytdMonthsHint">
    /// Months from the financial-year start to the report's period end. Used when a column is headed only
    /// "For the period ended …" without naming the months.
    /// </param>
    public static ReportExtraction Extract(Stream pdf, long? sharesInIssue, int? ytdMonthsHint = null)
    {
        using var doc = PdfDocument.Open(pdf);
        var pages = doc.GetPages().Select(p => new PageRows(p.Number, BuildRows(p.GetWords()))).ToList();
        var result = new ReportExtraction();

        ExtractNav(pages, sharesInIssue, result);
        ExtractEps(pages, sharesInIssue, ytdMonthsHint ?? YtdMonthsFromText(pages), result);
        ExtractPublicFloat(pages, result);
        return result;
    }

    // ── Earnings per share ─────────────────────────────────────────────────────────────────────────

    /// <summary>EPS rows: "Basic earnings per share (Rs.)", "Earnings per share (EPS)", "Earnings/(loss) per Share".</summary>
    private static readonly Regex EpsLabel = new(
        @"earnings?\s*(/\s*\(?loss\)?\s*)?\s*per\s+(ordinary\s+)?share",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Profit attributable to the parent's owners — the cross-check for EPS.</summary>
    private static readonly Regex OwnersProfitLabel = new(
        @"(equity\s+holders|owners|shareholders)\s+of\s+the\s+(parent|company|bank)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>EPS uses weighted-average shares, so allow a little more drift than NAV.</summary>
    private const decimal EpsTolerance = 0.06m;

    private static void ExtractEps(List<PageRows> pages, long? shares, int? ytdMonthsHint, ReportExtraction result)
    {
        var figures = new List<EpsFigure>();

        foreach (var page in pages)
        {
            for (var i = 0; i < page.Rows.Count; i++)
            {
                var row = page.Rows[i];
                var label = row.LabelText;
                var isEps = EpsLabel.IsMatch(label);

                // "Earnings per share" as a heading with the figures on a "Basic" row beneath it.
                if (!isEps && label.StartsWith("basic", StringComparison.OrdinalIgnoreCase))
                    isEps = Enumerable.Range(Math.Max(0, i - 2), Math.Min(2, i))
                        .Any(j => EpsLabel.IsMatch(page.Rows[j].LabelText));
                if (!isEps) continue;

                // Continuing-operations splits, diluted-only rows and dividends are not headline EPS.
                if (Regex.IsMatch(label, @"continuing|discontinued|dividend", RegexOptions.IgnoreCase)) continue;
                if (label.Contains("diluted", StringComparison.OrdinalIgnoreCase) &&
                    !label.Contains("basic", StringComparison.OrdinalIgnoreCase)) continue;

                static List<NumberToken> PerShare(Row r) =>
                    r.Numbers.Where(n => n.IsDecimal && !n.IsPercent && Math.Abs(n.Value) < 100_000).ToList();

                var values = PerShare(row);
                // Label on its own row, figures on the unlabelled row beneath ("…Per Share (Rs.)" / "3.00 2.51 …").
                if (values.Count == 0 && i + 1 < page.Rows.Count && page.Rows[i + 1].LabelText.Length == 0)
                    values = PerShare(page.Rows[i + 1]);
                if (values.Count == 0) continue;

                var periods = PeriodHeaders(page, i, ytdMonthsHint);
                foreach (var (entity, entityValues) in SplitByEntity(page, i, values))
                {
                    foreach (var block in AssignPeriods(entityValues, periods))
                    {
                        // Each block reads current, prior[, change %] left to right. Four or more values
                        // under one period means two periods were merged — don't guess which is which.
                        if (block.Values.Count >= 4) continue;
                        var ordered = block.Values.OrderBy(v => v.Left).ToList();
                        var current = ordered[0];
                        var prior = ordered.Count > 1 ? ordered[1] : null;
                        var verified = VerifyEps(page, i, current, shares);
                        figures.Add(new EpsFigure(entity, block.Months, current.Value, prior?.Value, verified, page.Number));
                    }
                }
            }
        }

        if (figures.Count == 0)
        {
            result.Notes.Add("No earnings-per-share row found.");
            return;
        }

        result.Notes.Add("EPS candidates: " + string.Join("; ", figures.Select(f =>
            $"{f.Entity} {f.Months?.ToString() ?? "?"}m {f.Current}/{f.Prior} p{f.Page}{(f.Verified ? " ✓" : "")}")));

        static int Rank(string entity) => entity switch { "Group" => 0, "Single" => 1, _ => 2 };
        var bestRank = figures.Min(f => Rank(f.Entity));
        var pool = figures.Where(f => Rank(f.Entity) == bestRank).ToList();

        // Prefer verified figures; among equals, the first printed (main statement before notes).
        EpsFigure? Pick(Func<EpsFigure, bool> which) =>
            pool.Where(which).OrderByDescending(f => f.Verified).ThenBy(f => f.Page).FirstOrDefault();

        var quarter = Pick(f => f.Months == 3);

        // Year-to-date is the longest period shorter than a year. A 12-month column printed beside a
        // shorter YTD is the last full financial year (some companies show it for reference); a
        // 12-month column on its own means this is the year-end report and it *is* the YTD.
        var ytd = Pick(f => f.Months is > 3 and < 12);
        var fullYear = Pick(f => f.Months == 12);
        if (ytd == null && fullYear != null)
        {
            ytd = fullYear;
            fullYear = null;
        }
        if (fullYear != null) result.EpsLastFullYear = fullYear.Current;

        result.EpsEntity = pool[0].Entity;
        if (quarter != null)
        {
            result.EpsQuarterCurrent = quarter.Current;
            result.EpsQuarterPrior = quarter.Prior;
        }
        if (ytd != null)
        {
            result.EpsYtdMonths = ytd.Months;
            result.EpsYtdCurrent = ytd.Current;
            result.EpsYtdPrior = ytd.Prior;
        }

        var used = new[] { quarter, ytd }.Where(f => f != null).ToList();
        result.EpsStatus = used.Count > 0 && used.All(f => f!.Verified) ? FundamentalStatus.Verified
            : used.Count > 0 ? FundamentalStatus.Unverified
            : FundamentalStatus.NotFound;
        result.Notes.Add($"EPS ({result.EpsEntity}): quarter {quarter?.Current}/{quarter?.Prior}, " +
                         $"{ytd?.Months}m {ytd?.Current}/{ytd?.Prior} — {result.EpsStatus}.");
    }

    /// <summary>
    /// EPS × shares should equal the owners' profit printed in the same column above it. Companies with
    /// no minority interests print only "Profit for the period", which is then the owners' profit.
    /// </summary>
    private static bool VerifyEps(PageRows page, int epsRow, NumberToken eps, long? shares)
    {
        if (shares is not > 0 || eps.Value == 0) return false;

        foreach (var label in new[] { OwnersProfitLabel, PeriodProfitLabel })
        {
            for (var i = epsRow - 1; i >= Math.Max(0, epsRow - 40); i--)
            {
                var row = page.Rows[i];
                if (!label.IsMatch(row.LabelText)) continue;
                var amounts = Amounts(row);
                if (amounts.Count == 0) continue;

                var profit = amounts.OrderBy(n => Math.Abs(n.CenterX - eps.CenterX)).First().Value;
                if (Evaluate(profit, eps.Value, shares.Value).Diff <= EpsTolerance) return true;
            }
        }
        return false;
    }

    private static readonly Regex MonthsEndedPhrase = new(
        @"\b(three|six|nine|twelve|0?3|0?6|0?9|12)\s+months?\s+(ended|to)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The report's year-to-date length, from how it describes itself elsewhere ("For the six months
    /// ended…" in the cash-flow statement or notes): the most frequent length longer than a quarter.
    /// </summary>
    private static int? YtdMonthsFromText(List<PageRows> pages)
    {
        var counts = new Dictionary<int, int>();
        foreach (var row in pages.SelectMany(p => p.Rows))
        foreach (Match m in MonthsEndedPhrase.Matches(row.FullText))
        {
            var months = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "three" or "3" or "03" => 3,
                "six" or "6" or "06" => 6,
                "nine" or "9" or "09" => 9,
                _ => 12,
            };
            counts[months] = counts.GetValueOrDefault(months) + 1;
        }

        var longer = counts.Where(kv => kv.Key > 3).OrderByDescending(kv => kv.Value).ToList();
        if (longer.Count > 0) return longer[0].Key;
        return counts.ContainsKey(3) ? 3 : null;
    }

    private static readonly Regex PeriodProfitLabel = new(
        @"^profit\s*(/\s*\(?loss\)?)?\s*(after\s+tax\s+)?for\s+the\s+(period|quarter|year)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Period headers above a row: each "quarter / three months" or "six / nine / twelve months / year"
    /// phrase with the horizontal centre of the span it heads.
    /// </summary>
    private static List<(int Months, double CenterX)> PeriodHeaders(PageRows page, int rowIndex, int? ytdMonthsHint)
    {
        // Header blocks often stack a generic subtitle ("For the period ended 30 September") under the
        // real column header ("Quarter ended | Period ended"). Look a few header rows past the first one
        // that names a period and keep the row naming the most periods.
        List<(int, double)>? best = null;
        var headerRowsSeen = 0;
        for (var i = rowIndex - 1; i >= 0 && headerRowsSeen < 5; i--)
        {
            var row = page.Rows[i];
            if (row.Numbers.Any(n => n.Raw.Contains(',')))
            {
                if (best != null) break; // back in the table body above the header block
                continue;                // amounts, not a header
            }
            if (best != null) headerRowsSeen++;

            var words = row.Words;
            var keys = new List<(int Index, int Months)>();
            for (var w = 0; w < words.Count; w++)
            {
                var months = PeriodMonths(words, w, ytdMonthsHint);
                if (months != null) keys.Add((w, months.Value));
            }
            if (keys.Count == 0) continue;

            // A phrase starts at its "For the" (if present) and runs until the next phrase starts.
            var starts = keys.Select(k =>
            {
                var s = k.Index;
                while (s > 0 && s > k.Index - 2 && words[s - 1].Text is "For" or "for" or "the" or "The") s--;
                return s;
            }).ToList();

            var phrases = new List<(int, double)>();
            for (var k = 0; k < keys.Count; k++)
            {
                var from = starts[k];
                var to = k + 1 < keys.Count ? starts[k + 1] - 1 : words.Count - 1;
                phrases.Add((keys[k].Months, (words[from].Left + words[to].Right) / 2));
            }
            if (best == null || phrases.Count > best.Count) best = phrases;
        }
        return best ?? new List<(int, double)>();
    }

    /// <summary>Months named by a period keyword at <paramref name="w"/> ("Quarter", "Six months", "Year ended").</summary>
    private static int? PeriodMonths(List<Tok> words, int w, int? ytdMonthsHint)
    {
        var t = words[w].Text.Trim(',', '.').ToLowerInvariant();
        var next = w + 1 < words.Count ? words[w + 1].Text.ToLowerInvariant() : "";
        var monthsNext = next.StartsWith("month");

        if (t == "quarter") return 3;
        if (t == "year" && next.StartsWith("ended")) return 12;
        // "For the period ended 30th September" names no length: it is year-to-date.
        if (t == "period" && next.StartsWith("ended")) return ytdMonthsHint;
        if (!monthsNext) return null;
        return t switch
        {
            "three" or "3" or "03" => 3,
            "six" or "6" or "06" => 6,
            "nine" or "9" or "09" => 9,
            "twelve" or "12" => 12,
            _ => null,
        };
    }

    /// <summary>Groups values under the nearest period header; with no headers, one block of unknown length.</summary>
    private static IEnumerable<(int? Months, List<NumberToken> Values)> AssignPeriods(
        List<NumberToken> values, List<(int Months, double CenterX)> periods)
    {
        if (periods.Count == 0)
        {
            yield return (null, values);
            yield break;
        }
        foreach (var g in values.GroupBy(v => periods.OrderBy(p => Math.Abs(p.CenterX - v.CenterX)).First()))
            yield return (g.Key.Months, g.ToList());
    }

    // ── NAV per share ──────────────────────────────────────────────────────────────────────────────

    private static void ExtractNav(List<PageRows> pages, long? shares, ReportExtraction result)
    {
        var candidates = new List<NavCandidate>();

        foreach (var page in pages)
        {
            for (var i = 0; i < page.Rows.Count; i++)
            {
                var row = page.Rows[i];
                var labelText = row.LabelText;

                // A wrapped label ("Net assets value per" / "share (Rs.)") is matched with the next row.
                var matched = NavLabel.IsMatch(labelText);
                var valueRow = row;
                if (!matched && i + 1 < page.Rows.Count &&
                    labelText.Contains("net asset", StringComparison.OrdinalIgnoreCase) &&
                    NavLabel.IsMatch(labelText + " " + page.Rows[i + 1].LabelText))
                {
                    matched = true;
                    if (!row.Numbers.Any(n => n.IsDecimal)) valueRow = page.Rows[i + 1];
                }
                if (!matched) continue;

                // Prose ("Net assets per share have been calculated…") carries no per-share decimals.
                var values = valueRow.Numbers.Where(n => n.IsDecimal && !n.IsPercent).ToList();
                if (values.Count == 0) continue;

                foreach (var (entity, nums) in SplitByEntity(page, i, values))
                {
                    var current = nums.OrderBy(n => n.Left).First();
                    candidates.Add(new NavCandidate(page, i, entity, current.Value, current.CenterX));
                }
            }
        }

        if (candidates.Count == 0)
        {
            result.Notes.Add("No 'net assets per share' row found.");
            return;
        }

        // The entity decides first — the group figure is what the market values, so an unverified group
        // NAV still beats a verified company-only one. Within that entity, prefer one that verifies.
        static int Rank(string entity) => entity switch { "Group" => 0, "Single" => 1, _ => 2 };
        var bestRank = candidates.Min(c => Rank(c.Entity));
        var pool = candidates.Where(c => Rank(c.Entity) == bestRank).OrderBy(c => c.Page.Number).ToList();

        foreach (var c in pool)
        {
            var check = CheckAgainstEquity(c, shares);
            if (check is { Verified: true })
            {
                Apply(result, c, check, FundamentalStatus.Verified);
                return;
            }
        }

        // NAV printed on a summary page has no equity row beside it: accept it if any equity figure in
        // the report implies the same per-share value.
        foreach (var c in pool)
        {
            var check = CheckAgainstAnyEquity(pages, c.Value, shares);
            if (check is { Verified: true })
            {
                Apply(result, c, check, FundamentalStatus.Verified);
                return;
            }
        }

        var best = pool[0];
        Apply(result, best, CheckAgainstEquity(best, shares), FundamentalStatus.Unverified);
    }

    private static void Apply(ReportExtraction result, NavCandidate c, EquityCheck? check, FundamentalStatus status)
    {
        result.NavPerShare = c.Value;
        result.NavEntity = c.Entity;
        result.NavStatus = status;
        result.Notes.Add($"NAV {c.Value} ({c.Entity}) on page {c.Page.Number}.");

        if (check != null)
        {
            result.EquityAsPrinted = check.Equity;
            result.EquityUnit = check.Unit;
            result.NavFromEquity = check.Implied;
            result.Notes.Add(check.Verified
                ? $"Verified: equity ÷ shares = {check.Implied:0.00}."
                : $"Equity ÷ shares = {check.Implied:0.00} does not match {c.Value}.");
        }
        else
        {
            result.Notes.Add("No equity row next to it to cross-check.");
        }
    }

    /// <summary>
    /// Checks a NAV candidate against the equity rows above it in the same statement, taking the amount
    /// in the same column. "Equity attributable to owners" is the right numerator and wins; "Total
    /// equity" (which includes minority interests) is only used when no attributable row exists.
    /// </summary>
    private static EquityCheck? CheckAgainstEquity(NavCandidate c, long? shares)
    {
        if (shares is not > 0) return null;

        // Nearest rows first (Reverse), then by how good a numerator each kind is.
        var checks = EquityRows(c.Page, c.RowIndex)
            .Reverse()
            .Select(eq => (eq.Kind, Check: Evaluate(
                eq.Amounts.OrderBy(n => Math.Abs(n.CenterX - c.CenterX)).First().Value, c.Value, shares.Value)))
            .OrderBy(x => x.Kind)
            .ToList();

        return checks.FirstOrDefault(x => x.Check.Verified).Check ?? checks.FirstOrDefault().Check;
    }

    /// <summary>Any equity amount anywhere in the report that implies this NAV (any column, any page).</summary>
    private static EquityCheck? CheckAgainstAnyEquity(List<PageRows> pages, decimal nav, long? shares)
    {
        if (shares is not > 0) return null;

        EquityCheck? best = null;
        foreach (var page in pages)
        foreach (var eq in EquityRows(page, page.Rows.Count))
        foreach (var amount in eq.Amounts)
        {
            var check = Evaluate(amount.Value, nav, shares.Value);
            if (best == null || check.Diff < best.Diff) best = check;
        }
        return best;
    }

    /// <summary>
    /// Equity rows on a page above <paramref name="beforeRow"/>, resolving the three layouts seen in CSE
    /// reports: amounts on the label row; a wrapped label whose amounts sit on the next row
    /// ("…Owners of the" / "Company 22,453,082"); and a section heading whose total is the first
    /// unlabelled subtotal under its components.
    /// </summary>
    private static IEnumerable<EquityRow> EquityRows(PageRows page, int beforeRow)
    {
        for (var i = 0; i < beforeRow; i++)
        {
            var row = page.Rows[i];
            var attributable = EquityAttributableLabel.IsMatch(row.LabelText);
            var total = !attributable && TotalEquityLabel.IsMatch(row.LabelText);
            if (!attributable && !total) continue;

            var nums = Amounts(row);
            if (nums.Count == 0 && attributable)
            {
                // Wrapped label ("…Owners of the" / "Company 22,453,082"): only when the label is
                // visibly unfinished, so a complete heading doesn't swallow "Stated capital" below it.
                if (i + 1 < beforeRow && !CompleteOwnerLabel.IsMatch(row.LabelText))
                {
                    var next = page.Rows[i + 1];
                    var words = next.LabelText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    if (words is > 0 and <= 3 && Amounts(next).Count > 0) nums = Amounts(next);
                }

                // Section heading: first unlabelled subtotal below it.
                for (var j = i + 1; nums.Count == 0 && j < beforeRow; j++)
                {
                    var below = page.Rows[j];
                    if (below.LabelText.Length == 0 && Amounts(below).Count > 0) nums = Amounts(below);
                }
            }
            if (nums.Count == 0) continue;

            if (attributable)
            {
                yield return new EquityRow(i, EquityKind.Attributable, nums);
                continue;
            }

            // Total equity includes minority interests. If the non-controlling interest row sits just
            // above it, total − NCI (column by column) is the attributable figure.
            for (var j = i - 1; j >= Math.Max(0, i - 6); j--)
            {
                if (!NonControllingLabel.IsMatch(page.Rows[j].LabelText)) continue;
                var derived = SubtractByColumn(nums, Amounts(page.Rows[j]));
                if (derived.Count > 0) yield return new EquityRow(i, EquityKind.Derived, derived);
                break;
            }
            yield return new EquityRow(i, EquityKind.Total, nums);
        }
    }

    /// <summary>total − nci for each column where both have an amount (matched by x position).</summary>
    private static List<NumberToken> SubtractByColumn(List<NumberToken> totals, List<NumberToken> nci) =>
        totals
            .Select(t => (t, n: nci.Where(n => Math.Abs(n.CenterX - t.CenterX) < 20)
                                   .OrderBy(n => Math.Abs(n.CenterX - t.CenterX))
                                   .FirstOrDefault()))
            .Where(x => x.n != null)
            .Select(x => x.t with { Value = x.t.Value - x.n!.Value })
            .ToList();

    /// <summary>Equity ÷ shares in the unit (rupees, thousands, millions) that best matches the NAV.</summary>
    private static EquityCheck Evaluate(decimal equity, decimal nav, long shares)
    {
        EquityCheck? best = null;
        foreach (var unit in new[] { 1m, 1_000m, 1_000_000m })
        {
            var implied = Math.Round(equity * unit / shares, 4);
            var diff = nav == 0 ? decimal.MaxValue : Math.Abs(implied - nav) / Math.Abs(nav);
            if (best == null || diff < best.Diff)
                best = new EquityCheck(equity, unit, implied, diff, diff <= VerifyTolerance);
        }
        return best!;
    }

    private static List<NumberToken> Amounts(Row row) =>
        row.Numbers.Where(n => !n.IsPercent && Math.Abs(n.Value) >= 1000).ToList();

    // ── Public float ───────────────────────────────────────────────────────────────────────────────

    private static void ExtractPublicFloat(List<PageRows> pages, ReportExtraction result)
    {
        foreach (var page in pages)
        {
            foreach (var row in page.Rows)
            {
                // Table form: "Public shareholding (%)   72.37   72.40" — first column is the current date.
                if (PublicFloatLabel.IsMatch(row.LabelText))
                {
                    var pct = row.Numbers
                        .Where(n => n.IsDecimal && n.Value > 0 && n.Value <= 100)
                        .OrderBy(n => n.Left)
                        .FirstOrDefault();
                    if (pct != null)
                    {
                        result.PublicFloatPercent = pct.Value;
                        return;
                    }
                }

                // Sentence form: "Percentage of public holding as at September 30, 2026 - 99.82%"
                // or "Percentage of shares held by the public 3.358%".
                var m = PublicFloatSentence.Match(row.FullText);
                if (m.Success &&
                    decimal.TryParse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) &&
                    v is > 0 and <= 100)
                {
                    result.PublicFloatPercent = v;
                    return;
                }
            }
        }
    }

    // ── Column / entity assignment ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits a row's numbers between the Group and Company/Bank columns, using the nearest header row
    /// above that names an entity. Only when that row names both is the row split; a row naming one
    /// (or none — then the page heading decides, "Consolidated" → Group) takes all the numbers. Looking
    /// further up for a two-entity row is wrong: it finds prose ("the Group and the Company") and splits
    /// period columns (quarter | year-to-date) as if they were entities.
    /// </summary>
    private static IEnumerable<(string Entity, List<NumberToken> Numbers)> SplitByEntity(
        PageRows page, int rowIndex, List<NumberToken> values)
    {
        List<(string Entity, double X)>? nearest = null, both = null;
        for (var i = rowIndex - 1; i >= 0 && nearest == null; i--)
        {
            var row = page.Rows[i];
            // Header rows hold words and maybe dates/years, never amounts.
            if (row.Numbers.Any(n => n.IsDecimal || n.Raw.Contains(','))) continue;
            if (row.Words.Count > 12) continue;

            var found = row.Words
                .Select(w => (Word: w, Entity: EntityOf(w.Text)))
                .Where(x => x.Entity != null)
                .Select(x => (x.Entity!, x.Word.CenterX))
                .ToList();
            if (found.Count == 0) continue;
            nearest = found;
            if (found.Select(f => f.Item1).Distinct().Count() > 1) both = found;
        }

        if (both == null)
        {
            var entity = nearest?[0].Entity ?? (PageIsConsolidated(page, rowIndex) ? "Group" : "Single");
            yield return (entity, values);
            yield break;
        }

        // Bank/Group tables are symmetric (current, prior, change % for each), so an even count splits
        // cleanly in half in header order. That is more robust than nearest-header when headers are
        // left-aligned over their block rather than centred.
        var ordered = values.OrderBy(v => v.Left).ToList();
        var leftFirst = both.OrderBy(h => h.X).Select(h => h.Entity).Distinct().ToList();
        if (leftFirst.Count == 2 && ordered.Count % 2 == 0)
        {
            var half = ordered.Count / 2;
            yield return (leftFirst[0], ordered.Take(half).ToList());
            yield return (leftFirst[1], ordered.Skip(half).ToList());
            yield break;
        }

        foreach (var g in ordered.GroupBy(v => both.OrderBy(h => Math.Abs(h.X - v.CenterX)).First().Entity))
            yield return (g.Key, g.ToList());
    }

    private static string? EntityOf(string word)
    {
        var w = word.Trim().Trim(',', '.', ':').ToLowerInvariant();
        if (GroupWords.Contains(w)) return "Group";
        if (CompanyWords.Contains(w)) return "Company";
        return null;
    }

    /// <summary>A heading above the row such as "Consolidated statement of financial position / income statement".</summary>
    private static bool PageIsConsolidated(PageRows page, int rowIndex) =>
        page.Rows.Take(rowIndex).Any(r =>
            r.LabelText.Contains("consolidated", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(r.LabelText, @"statement|financial\s+position|balance\s+sheet", RegexOptions.IgnoreCase));

    // ── Row building ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Groups words that share a baseline into left-to-right rows, top of page first.</summary>
    private static List<Row> BuildRows(IEnumerable<Word> words)
    {
        var rows = new List<List<Tok>>();
        foreach (var w in words
                     .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                     .OrderByDescending(w => w.BoundingBox.Bottom)
                     .ThenBy(w => w.BoundingBox.Left))
        {
            var tok = new Tok(Undouble(w.Text), w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom);
            var row = rows.LastOrDefault();
            if (row != null && Math.Abs(row[0].Bottom - tok.Bottom) < 2.5) row.Add(tok);
            else rows.Add(new List<Tok> { tok });
        }

        return rows.Select(r => new Row(MergeFragments(r.OrderBy(t => t.Left).ToList()))).ToList();
    }

    /// <summary>
    /// Some PDFs place a number's leading digit as a separate glyph run ("1" + "68.97" = "168.97").
    /// Numeric fragments that touch (gap under ~1pt) are joined back together.
    /// </summary>
    private static List<Tok> MergeFragments(List<Tok> toks)
    {
        var merged = new List<Tok>();
        foreach (var t in toks)
        {
            var prev = merged.LastOrDefault();
            if (prev != null && t.Left - prev.Right < 1.0 && IsNumericFragment(prev.Text) && IsNumericFragment(t.Text))
                merged[^1] = prev with { Text = prev.Text + t.Text, Right = t.Right };
            else
                merged.Add(t);
        }
        return merged;
    }

    private static bool IsNumericFragment(string s) => s.All(c => char.IsDigit(c) || c is ',' or '.' or '(' or ')' or '-');

    /// <summary>
    /// Fake-bold text is drawn twice, so every glyph comes out doubled: "BBaannkk", "1155,,113300".
    /// Collapse a token when every pair repeats and it carries a doubled letter or separator — a plain
    /// "1100" stays as it is, since it could be a real number.
    /// </summary>
    private static string Undouble(string s)
    {
        if (s.Length < 4 || s.Length % 2 != 0) return s;
        for (var i = 0; i < s.Length; i += 2)
            if (s[i] != s[i + 1]) return s;
        if (!s.Any(c => char.IsLetter(c) || c is ',' or '.')) return s;
        return new string(s.Where((_, i) => i % 2 == 0).ToArray());
    }

    // ── Types ──────────────────────────────────────────────────────────────────────────────────────

    private sealed record Tok(string Text, double Left, double Right, double Bottom)
    {
        public double CenterX => (Left + Right) / 2;
    }

    private sealed record NumberToken(string Raw, decimal Value, double Left, double CenterX, bool IsDecimal, bool IsPercent);

    private sealed class Row
    {
        public List<Tok> Words { get; }
        public List<NumberToken> Numbers { get; }

        /// <summary>The row's words (its label), without numbers or nil dashes.</summary>
        public string LabelText { get; }

        /// <summary>Every token in reading order — for figures stated inside sentences.</summary>
        public string FullText { get; }

        public Row(List<Tok> words)
        {
            Words = words;
            FullText = string.Join(" ", words.Select(w => w.Text));
            Numbers = new List<NumberToken>();

            // Values are the numbers to the right of the last word. Numbers before that — section
            // numbers ("10.3 Net assets…") or note references — belong to the label area.
            // Lone letters are vertical margin text ("S", "t", "a" of a rotated title) sharing the
            // baseline; they must not push the row's values into its label area.
            var lastWord = -1;
            for (var i = 0; i < words.Count; i++)
            {
                var t = words[i].Text.Trim();
                if (!IsNil(t) && t.Length > 1 && !NumberPattern.IsMatch(t)) lastWord = i;
            }

            var label = new List<string>();
            for (var i = 0; i < words.Count; i++)
            {
                var w = words[i];
                if (IsNil(w.Text)) continue;
                if (i > lastWord && TryNumber(w, out var n)) Numbers.Add(n);
                else if (!NumberPattern.IsMatch(w.Text.Trim()) && !IsStrayLetter(w.Text)) label.Add(w.Text);
            }
            LabelText = string.Join(" ", label);
        }

        /// <summary>A lone dash is how these statements print "nil".</summary>
        private static bool IsNil(string s) => s.Trim() is "-" or "–" or "—";

        /// <summary>A single letter on its own is a fragment of rotated margin text, not part of a label.</summary>
        private static bool IsStrayLetter(string s) => s.Trim() is { Length: 1 } t && char.IsLetter(t[0]);

        private static bool TryNumber(Tok t, out NumberToken n)
        {
            n = null!;
            var s = t.Text.Trim();
            if (!NumberPattern.IsMatch(s)) return false;

            var isPercent = s.EndsWith('%');
            var negative = s.StartsWith('(') && s.EndsWith(')') || s.StartsWith('-');
            var clean = s.Trim('(', ')', '%', '-').Replace(",", "");
            if (!decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return false;

            n = new NumberToken(s, negative ? -v : v, t.Left, t.CenterX, clean.Contains('.'), isPercent);
            return true;
        }
    }

    private sealed record PageRows(int Number, List<Row> Rows);

    private sealed record NavCandidate(PageRows Page, int RowIndex, string Entity, decimal Value, double CenterX);

    private sealed record EquityCheck(decimal Equity, decimal Unit, decimal Implied, decimal Diff, bool Verified);

    private sealed record EpsFigure(string Entity, int? Months, decimal Current, decimal? Prior, bool Verified, int Page);

    /// <summary>Equity numerators in order of preference for NAV: attributable, total − NCI, total.</summary>
    private enum EquityKind { Attributable = 0, Derived = 1, Total = 2 }

    private sealed record EquityRow(int RowIndex, EquityKind Kind, List<NumberToken> Amounts);
}
