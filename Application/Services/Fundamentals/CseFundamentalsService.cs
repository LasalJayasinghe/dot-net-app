using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.DTOs;
using dotnetApp.Domain.Common;
using dotnetApp.Domain.Entities;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace dotnetApp.Application.Services.Fundamentals;

/// <summary>
/// Reads CSE interim financial statements (cse.lk <c>financials</c> listing → PDF on cdn.cse.lk →
/// <see cref="CseReportExtractor"/>), stores the figures, and combines them with live prices into
/// NAV, P/BV, trailing EPS, P/E, ROE, dividend yield and public float.
/// </summary>
public class CseFundamentalsService
{
    /// <summary>Bump when <see cref="CseReportExtractor"/> improves so stored reports are re-read.</summary>
    public const int ExtractorVersion = 1;

    /// <summary>
    /// Newest reports read per company: enough to reach the last year-end report that trailing EPS
    /// needs (a Q3 report is three reports after it).
    /// </summary>
    private const int MaxReportsPerSync = 5;

    private const string ApiBase = "https://www.cse.lk/api/";
    private const string CdnBase = "https://cdn.cse.lk/";

    /// <summary>One sync at a time across the job and admin endpoints — cse.lk is a shared, fragile source.</summary>
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    private static readonly Regex PeriodEndInTitle = new(
        @"ended\s+(\d{1,2})(?:st|nd|rd|th)?\s+([A-Za-z]+),?\s+(\d{4})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CseFundamentalsService> _logger;

    public CseFundamentalsService(HttpClient http, AppDbContext db, IMemoryCache cache, ILogger<CseFundamentalsService> logger)
    {
        _http = http;
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    // ── Sync ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the company's newest interim reports that aren't stored yet (or were read by an older
    /// extractor), walking back until the last year-end report is covered. Returns reports read.
    /// </summary>
    public async Task<int> SyncSymbolAsync(string symbol, CancellationToken ct = default)
    {
        symbol = CseSymbols.ToLookup(symbol);
        await SyncLock.WaitAsync(ct);
        try
        {
            var listing = await GetQuarterlyListingAsync(symbol, ct);
            if (listing.Count == 0) return 0;

            var stored = await _db.CseFinancialReports
                .Where(r => r.Symbol == symbol)
                .ToDictionaryAsync(r => r.CseReportId, ct);

            long? shares = null;
            var read = 0;
            for (var i = 0; i < listing.Count && i < MaxReportsPerSync; i++)
            {
                var item = listing[i];
                stored.TryGetValue(item.Id, out var row);

                if (row == null || row.ExtractorVersion < ExtractorVersion)
                {
                    shares ??= await GetSharesInIssueAsync(symbol, ct);
                    row = await ReadReportAsync(symbol, item, shares, row, ct);
                    if (row == null) continue; // download failed; try again next sync
                    read++;
                }

                // Trailing EPS needs the latest report plus the last full year: stop once we have both.
                if (row.EpsYtdMonths == 12 || (i == 0 && row.EpsLastFullYear != null)) break;
            }
            return read;
        }
        finally
        {
            SyncLock.Release();
        }
    }

    private async Task<CseFinancialReport?> ReadReportAsync(
        string symbol, ListedReport item, long? shares, CseFinancialReport? existing, CancellationToken ct)
    {
        byte[] pdf;
        try
        {
            pdf = await _http.GetByteArrayAsync(ReportUrl(item.Path), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Could not download {Symbol} report {Id}: {Error}", symbol, item.Id, ex.Message);
            return null;
        }

        var row = existing ?? new CseFinancialReport { Symbol = symbol, CseReportId = item.Id };
        row.Title = Truncate(item.Title, 300);
        row.PdfPath = Truncate(item.Path, 300);
        row.PeriodEnd = ParsePeriodEnd(item.Title);
        row.UploadedAt = item.UploadedAt;
        row.SharesInIssue = shares;
        row.ExtractorVersion = ExtractorVersion;
        row.ExtractedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;

        try
        {
            // PDF parsing is CPU-bound; keep it off the request/job thread.
            var x = await Task.Run(() => CseReportExtractor.Extract(new MemoryStream(pdf), shares), ct);

            // Hand-entered figures are never replaced by a re-read.
            if (row.NavStatus != FundamentalStatus.Manual)
            {
                row.NavPerShare = x.NavPerShare;
                row.NavFromEquity = x.NavFromEquity;
                row.NavEntity = x.NavEntity;
                row.NavStatus = x.NavStatus;
            }
            if (row.EpsStatus != FundamentalStatus.Manual)
            {
                row.EpsEntity = x.EpsEntity;
                row.EpsStatus = x.EpsStatus;
                row.EpsYtdMonths = x.EpsYtdMonths;
                row.EpsYtdCurrent = x.EpsYtdCurrent;
                row.EpsYtdPrior = x.EpsYtdPrior;
                row.EpsQuarterCurrent = x.EpsQuarterCurrent;
                row.EpsQuarterPrior = x.EpsQuarterPrior;
                row.EpsLastFullYear = x.EpsLastFullYear;
            }
            row.PublicFloatPercent = x.PublicFloatPercent;
            row.Notes = string.Join("\n", x.Notes);
            row.Error = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read {Symbol} report {Id}", symbol, item.Id);
            row.Error = Truncate(ex.Message, 500);
        }

        if (existing == null) _db.CseFinancialReports.Add(row);
        await _db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>
    /// Report ids filed market-wide recently (cse.lk <c>getFinancialAnnouncement</c>) whose interim
    /// statements we haven't stored yet, as voting-share symbols.
    /// </summary>
    public async Task<List<string>> GetSymbolsWithNewFilingsAsync(CancellationToken ct = default)
    {
        using var response = await _http.PostAsync(ApiBase + "getFinancialAnnouncement", null, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("reqFinancialAnnouncemnets", out var items) ||
            items.ValueKind != JsonValueKind.Array) return new List<string>();

        var filings = items.EnumerateArray()
            .Select(e => (Id: Int(e, "id"), Symbol: Str(e, "symbol"), Title: Str(e, "fileText")))
            .Where(f => f.Id > 0 && f.Symbol.Length > 0 && IsInterimStatement(f.Title))
            .ToList();

        var ids = filings.Select(f => f.Id).ToList();
        var known = await _db.CseFinancialReports.Where(r => ids.Contains(r.CseReportId)).Select(r => r.CseReportId).ToListAsync(ct);
        return filings.Where(f => !known.Contains(f.Id)).Select(f => CseSymbols.ToLookup(f.Symbol)).Distinct().ToList();
    }

    /// <summary>Voting-share symbols that have never been synced (for the one-time backfill).</summary>
    public async Task<List<string>> GetUnsyncedSymbolsAsync(CancellationToken ct = default)
    {
        var synced = _db.CseFinancialReports.Select(r => r.Symbol).Distinct();
        return await _db.CseSecurities
            .Where(s => s.Symbol.EndsWith(".N0000") && !synced.Contains(s.Symbol))
            .OrderBy(s => s.Symbol)
            .Select(s => s.Symbol)
            .ToListAsync(ct);
    }

    // ── Read side ──────────────────────────────────────────────────────────────────────────────────

    public async Task<StockFundamentalsDto> GetFundamentalsAsync(string symbol, CancellationToken ct = default)
    {
        symbol = CseSymbols.ToLookup(symbol);
        var dto = new StockFundamentalsDto { Symbol = symbol };

        var company = await GetCompanySnapshotAsync(symbol, ct);
        if (company != null)
        {
            dto.Price = company.Price;
            dto.MarketCap = company.MarketCap;
            dto.SharesInIssue = company.Shares;
            dto.BetaAspi = company.BetaAspi;
            dto.BetaSl20 = company.BetaSl20;
            dto.High52Week = company.High52;
            dto.Low52Week = company.Low52;
        }

        var reports = await _db.CseFinancialReports.AsNoTracking()
            .Where(r => r.Symbol == symbol && r.Error == null)
            .OrderByDescending(r => r.PeriodEnd)
            .ThenByDescending(r => r.UploadedAt)
            .ToListAsync(ct);
        dto.HasReports = reports.Count > 0;

        // NAV: newest report that has one.
        var navReport = reports.FirstOrDefault(r => r.NavPerShare is > 0);
        if (navReport != null)
        {
            dto.NavPerShare = Value(navReport.NavPerShare, navReport.NavStatus,
                $"{navReport.NavEntity} NAV per share as at {navReport.PeriodEnd:d MMM yyyy}");
            if (dto.Price is > 0)
                dto.PriceToBook = Value(Math.Round(dto.Price.Value / navReport.NavPerShare!.Value, 2), navReport.NavStatus,
                    "Price ÷ NAV per share");
            dto.ReportTitle = navReport.Title;
            dto.ReportPeriodEnd = navReport.PeriodEnd;
            dto.ReportUrl = ReportUrl(navReport.PdfPath);
        }
        else
        {
            dto.NavPerShare.Note = reports.Count == 0 ? "Reports not read yet" : "NAV not found in the latest reports";
        }

        // Trailing EPS → P/E and ROE.
        var (ttm, ttmStatus, ttmNote) = TrailingEps(reports);
        dto.EpsTtm = Value(ttm, ttmStatus, ttmNote);
        if (ttm != null && dto.Price is > 0)
        {
            dto.PriceToEarnings = ttm > 0
                ? Value(Math.Round(dto.Price.Value / ttm.Value, 2), ttmStatus, "Price ÷ trailing 12-month EPS")
                : new FundamentalValueDto { Status = Label(ttmStatus), Note = "Loss over the last 12 months — P/E not meaningful" };
        }
        if (ttm != null && navReport != null)
        {
            dto.Roe = Value(Math.Round(ttm.Value / navReport.NavPerShare!.Value * 100, 2),
                Weakest(ttmStatus, navReport.NavStatus), "Trailing 12-month EPS ÷ latest NAV per share");
        }

        dto.DividendYield = await DividendYieldAsync(symbol, dto.Price, ct);

        var floatReport = reports.FirstOrDefault(r => r.PublicFloatPercent != null);
        if (floatReport != null)
            dto.PublicFloatPercent = Value(floatReport.PublicFloatPercent, FundamentalStatus.Unverified,
                $"As at {floatReport.PeriodEnd:d MMM yyyy}");

        return dto;
    }

    /// <summary>
    /// Trailing 12-month EPS = this year-to-date + last full year − the same year-to-date a year earlier.
    /// A year-end report (12-month YTD) is already trailing.
    /// </summary>
    private static (decimal? Value, FundamentalStatus Status, string Note) TrailingEps(List<CseFinancialReport> reports)
    {
        var latest = reports.FirstOrDefault(r => r.EpsYtdCurrent != null && r.EpsYtdMonths != null);
        if (latest == null)
            return (null, FundamentalStatus.NotFound,
                reports.Count == 0 ? "Reports not read yet" : "Year-to-date EPS not found in the latest reports");

        if (latest.EpsYtdMonths == 12)
            return (latest.EpsYtdCurrent, latest.EpsStatus, $"Full year to {latest.PeriodEnd:d MMM yyyy}");

        if (latest.EpsYtdPrior == null)
            return (null, FundamentalStatus.NotFound, "Prior-year comparative EPS not found");

        decimal? fullYear = latest.EpsLastFullYear;
        var fyStatus = latest.EpsStatus;
        if (fullYear == null && latest.PeriodEnd is { } end)
        {
            var yearEnd = reports.FirstOrDefault(r =>
                r.EpsYtdMonths == 12 && r.EpsYtdCurrent != null &&
                r.PeriodEnd < end && r.PeriodEnd >= end.AddMonths(-12));
            fullYear = yearEnd?.EpsYtdCurrent;
            if (yearEnd != null) fyStatus = yearEnd.EpsStatus;
        }
        if (fullYear == null)
            return (null, FundamentalStatus.NotFound, "Last year-end EPS not read yet");

        var ttm = latest.EpsYtdCurrent!.Value + fullYear.Value - latest.EpsYtdPrior.Value;
        return (Math.Round(ttm, 4), Weakest(latest.EpsStatus, fyStatus),
            $"{latest.EpsYtdMonths} months to {latest.PeriodEnd:d MMM yyyy} + last full year − same period a year earlier");
    }

    /// <summary>Dividends with a record date in the last 12 months ÷ price.</summary>
    private async Task<FundamentalValueDto> DividendYieldAsync(string symbol, decimal? price, CancellationToken ct)
    {
        if (price is not > 0) return new FundamentalValueDto { Note = "No price" };

        var baseSymbol = CseSymbols.BaseOf(symbol);
        var today = ColomboTime.Today;
        var from = today.AddYears(-1);
        var dps = await _db.Dividends.AsNoTracking()
            .Where(d => d.BaseSymbol == baseSymbol && d.RecordDate > from && d.RecordDate <= today)
            .SumAsync(d => (decimal?)d.VotingDivPerShare, ct) ?? 0m;

        // Our dividend history only starts when the dividend job first ran; say so when it's short.
        var since = await _db.Dividends.AsNoTracking().MinAsync(d => (DateTime?)d.RecordDate, ct);
        var partial = since == null || since > from;

        return new FundamentalValueDto
        {
            Value = Math.Round(dps / price.Value * 100, 2),
            Status = partial ? "partial" : "verified",
            Note = partial
                ? $"Dividend history only goes back to {since:d MMM yyyy}; yield may be understated"
                : $"Rs {dps:0.##} per share over the last 12 months ÷ price",
        };
    }

    // ── Admin ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Hand-corrects the newest report's figures; they are kept across re-reads.</summary>
    public async Task<bool> OverrideLatestAsync(string symbol, FundamentalsOverrideDto input, CancellationToken ct = default)
    {
        symbol = CseSymbols.ToLookup(symbol);
        var row = await _db.CseFinancialReports
            .Where(r => r.Symbol == symbol)
            .OrderByDescending(r => r.PeriodEnd)
            .FirstOrDefaultAsync(ct);
        if (row == null) return false;

        if (input.NavPerShare != null)
        {
            row.NavPerShare = input.NavPerShare;
            row.NavStatus = FundamentalStatus.Manual;
        }
        if (input.EpsYtdCurrent != null || input.EpsYtdPrior != null || input.EpsYtdMonths != null || input.EpsLastFullYear != null)
        {
            row.EpsYtdMonths = input.EpsYtdMonths ?? row.EpsYtdMonths;
            row.EpsYtdCurrent = input.EpsYtdCurrent ?? row.EpsYtdCurrent;
            row.EpsYtdPrior = input.EpsYtdPrior ?? row.EpsYtdPrior;
            row.EpsLastFullYear = input.EpsLastFullYear ?? row.EpsLastFullYear;
            row.EpsStatus = FundamentalStatus.Manual;
        }
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Counts by status, for checking the extraction hit rate.</summary>
    public async Task<object> GetStatusAsync(CancellationToken ct = default)
    {
        var rows = await _db.CseFinancialReports.AsNoTracking()
            .Select(r => new { r.Symbol, r.NavStatus, r.EpsStatus, r.Error })
            .ToListAsync(ct);
        return new
        {
            reports = rows.Count,
            companies = rows.Select(r => r.Symbol).Distinct().Count(),
            errors = rows.Count(r => r.Error != null),
            nav = rows.GroupBy(r => r.NavStatus.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            eps = rows.GroupBy(r => r.EpsStatus.ToString()).ToDictionary(g => g.Key, g => g.Count()),
        };
    }

    // ── cse.lk ─────────────────────────────────────────────────────────────────────────────────────

    private sealed record ListedReport(int Id, string Path, string Title, DateTime? UploadedAt);

    /// <summary>The company's interim statements, newest first, without errata/addenda.</summary>
    private async Task<List<ListedReport>> GetQuarterlyListingAsync(string symbol, CancellationToken ct)
    {
        using var response = await _http.PostAsync(ApiBase + "financials",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("symbol", symbol) }), ct);
        if (!response.IsSuccessStatusCode) return new List<ListedReport>();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("infoQuarterlyData", out var items) ||
            items.ValueKind != JsonValueKind.Array) return new List<ListedReport>();

        return items.EnumerateArray()
            .Select(e => new ListedReport(
                Int(e, "id"),
                Str(e, "path"),
                Str(e, "fileText"),
                Long(e, "uploadedDate") is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null))
            .Where(r => r.Id > 0 && r.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && IsInterimStatement(r.Title))
            .OrderByDescending(r => r.UploadedAt)
            .ToList();
    }

    private static bool IsInterimStatement(string title) =>
        title.Contains("interim", StringComparison.OrdinalIgnoreCase) &&
        !Regex.IsMatch(title, @"errata|addendum|corrigendum|amend", RegexOptions.IgnoreCase);

    private sealed record CompanySnapshot(
        decimal? Price, decimal? MarketCap, long? Shares, decimal? BetaAspi, decimal? BetaSl20, decimal? High52, decimal? Low52);

    /// <summary>Price, size, beta and 52-week range from cse.lk companyInfoSummery (cached 15 min).</summary>
    private async Task<CompanySnapshot?> GetCompanySnapshotAsync(string symbol, CancellationToken ct)
    {
        return await _cache.GetOrCreateAsync($"cse-company:{symbol}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            try
            {
                using var response = await _http.PostAsync(ApiBase + "companyInfoSummery",
                    new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("symbol", symbol) }), ct);
                if (!response.IsSuccessStatusCode) return null;
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;
                if (!root.TryGetProperty("reqSymbolInfo", out var info) || info.ValueKind != JsonValueKind.Object) return null;

                var last = Dec(info, "lastTradedPrice");
                var price = last is > 0 ? last : Dec(info, "closingPrice") is > 0 ? Dec(info, "closingPrice") : Dec(info, "previousClose");
                decimal? betaAspi = null, betaSl20 = null;
                if (root.TryGetProperty("reqSymbolBetaInfo", out var beta) && beta.ValueKind == JsonValueKind.Object)
                {
                    betaAspi = Dec(beta, "triASIBetaValue") is { } a ? Math.Round(a, 2) : null;
                    betaSl20 = Dec(beta, "betaValueSPSL") is { } b ? Math.Round(b, 2) : null;
                }
                return new CompanySnapshot(price, Dec(info, "marketCap"), Long(info, "quantityIssued"),
                    betaAspi, betaSl20, Dec(info, "p12HiPrice"), Dec(info, "p12LowPrice"));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning("companyInfoSummery failed for {Symbol}: {Error}", symbol, ex.Message);
                return null;
            }
        });
    }

    /// <summary>Voting shares plus the non-voting (X) class where one is listed — NAV and EPS use both.</summary>
    private async Task<long?> GetSharesInIssueAsync(string symbol, CancellationToken ct)
    {
        var voting = (await GetCompanySnapshotAsync(symbol, ct))?.Shares;
        if (voting == null) return null;

        var nonVotingSymbol = CseSymbols.BaseOf(symbol) + ".X0000";
        if (!await _db.CseSecurities.AnyAsync(s => s.Symbol == nonVotingSymbol, ct)) return voting;
        var nonVoting = (await GetCompanySnapshotAsync(nonVotingSymbol, ct))?.Shares ?? 0;
        return voting + nonVoting;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>cdn.cse.lk paths can contain spaces ("…27 - CSE.pdf"), so each segment is escaped.</summary>
    public static string ReportUrl(string path) =>
        CdnBase + string.Join("/", path.Split('/').Select(Uri.EscapeDataString));

    private static DateOnly? ParsePeriodEnd(string title)
    {
        var m = PeriodEndInTitle.Match(title);
        if (!m.Success) return null;
        return DateTime.TryParseExact($"{m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value}",
            new[] { "d MMMM yyyy", "d MMM yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateOnly.FromDateTime(d)
            : null;
    }

    private static FundamentalValueDto Value(decimal? value, FundamentalStatus status, string note) =>
        new() { Value = value, Status = value == null ? "missing" : Label(status), Note = note };

    private static string Label(FundamentalStatus s) => s switch
    {
        FundamentalStatus.Verified => "verified",
        FundamentalStatus.Manual => "manual",
        FundamentalStatus.Unverified => "unverified",
        _ => "missing",
    };

    /// <summary>The less trustworthy of two statuses (manual counts as trusted).</summary>
    private static FundamentalStatus Weakest(FundamentalStatus a, FundamentalStatus b)
    {
        static int Trust(FundamentalStatus s) => s switch
        {
            FundamentalStatus.Verified or FundamentalStatus.Manual => 2,
            FundamentalStatus.Unverified => 1,
            _ => 0,
        };
        return Trust(a) <= Trust(b) ? a : b;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;

    /// <summary>CSE sends some amounts in exponent form (marketCap 3.26441859452E11); fall back to double.</summary>
    private static decimal? Dec(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number) return null;
        if (v.TryGetDecimal(out var d)) return d;
        return v.TryGetDouble(out var f) && double.IsFinite(f) ? (decimal)f : null;
    }
}
