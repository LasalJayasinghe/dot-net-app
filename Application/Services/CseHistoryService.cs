using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using dotnetApp.Application.Dtos;
using dotnetApp.Domain.Entities;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.Application.Services;

/// <summary>Last result of each history task, for the admin status endpoint (in memory).</summary>
public sealed class CseHistoryStatus
{
    public sealed record RunInfo(string Task, DateTime FinishedAtUtc, bool Ok, string Message);

    private readonly ConcurrentDictionary<string, RunInfo> _runs = new();

    public void Set(string task, bool ok, string message) =>
        _runs[task] = new RunInfo(task, DateTime.UtcNow, ok, message);

    public IReadOnlyCollection<RunInfo> All => _runs.Values.OrderBy(r => r.Task).ToList();
}

/// <summary>
/// Keeps a permanent daily price history of every CSE security in our database.
///   • <see cref="RecordLatestSessionAsync"/>: after the close, saves the session from tradeSummary
///     (open/high/low/close, volume, trades, turnover, market cap), the ASPI and S&amp;P SL20 closes and
///     the market-wide dailyMarketSummery (which CSE only serves for 2 days).
///   • <see cref="BackfillAsync"/>: fills missing days from the chart API (high/low/close/volume, no open).
///     period 5 ≈ 1 year (the maximum CSE serves), period 4 ≈ 2 months (weekly gap repair).
/// Every task is idempotent (upsert by symbol + date) and runs one at a time.
/// </summary>
public class CseHistoryService
{
    private const string Api = "https://www.cse.lk/api/";

    /// <summary>Pause between per-security chart calls, to stay gentle with cse.lk.</summary>
    private static readonly TimeSpan PoliteDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>A session counts as published once this share of its securities has a closing price.</summary>
    private const double ClosedShare = 0.9;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static readonly TimeZoneInfo Colombo = ResolveColombo();

    private readonly HttpClient _http;
    private readonly AppDbContext _db;
    private readonly CseHistoryStatus _status;
    private readonly ILogger<CseHistoryService> _logger;

    public CseHistoryService(HttpClient http, AppDbContext db, CseHistoryStatus status, ILogger<CseHistoryService> logger)
    {
        _http = http;
        _db = db;
        _status = status;
        _logger = logger;
    }

    /// <summary>True while a backfill / record run is in progress.</summary>
    public static bool IsBusy => Gate.CurrentCount == 0;

    public static DateOnly TodayInColombo() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Colombo));

    public Task<bool> HasPriceDataAsync(CancellationToken ct) => _db.CsePriceDaily.AnyAsync(ct);

    // ── Daily recording ──────────────────────────────────────────────────────

    public sealed record RecordResult(bool Recorded, DateOnly? SessionDate, int Rows, string Message);

    /// <summary>
    /// Saves the latest finished session. The session date comes from CSE's own last-trade times, so a
    /// late run (e.g. after a restart in the evening) still files the day correctly, and on holidays it
    /// is a no-op because that session is already stored.
    /// </summary>
    public async Task<RecordResult> RecordLatestSessionAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var result = await RecordCoreAsync(ct);
            _status.Set("record", true, result.Message);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "CSE history: recording the session failed.");
            _status.Set("record", false, ex.Message);
            return new RecordResult(false, null, 0, ex.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<RecordResult> RecordCoreAsync(CancellationToken ct)
    {
        var rows = await FetchTradeSummaryAsync(ct);
        if (rows.Count == 0) return new RecordResult(false, null, 0, "tradeSummary returned no rows.");

        await UpsertSecuritiesAsync(rows, ct);

        var sessionDate = rows.GroupBy(r => r.TradeDate).OrderByDescending(g => g.Count()).First().Key;
        var session = rows.Where(r => r.TradeDate == sessionDate).ToList();
        var closed = session.Where(r => r.ClosingPrice > 0).ToList();

        if (closed.Count < session.Count * ClosedShare)
            return new RecordResult(false, sessionDate, 0,
                $"Session {sessionDate:yyyy-MM-dd} not closed yet ({closed.Count}/{session.Count} closing prices published).");

        var existing = await _db.CsePriceDaily
            .Where(p => p.TradeDate == sessionDate)
            .ToDictionaryAsync(p => p.Symbol, StringComparer.OrdinalIgnoreCase, ct);

        var alreadyRecorded = existing.Values.Count(p => p.Source == CseHistorySource.TradeSummary) >= closed.Count;
        var written = 0;
        if (!alreadyRecorded)
        {
            var now = DateTime.UtcNow;
            foreach (var r in closed)
            {
                if (!existing.TryGetValue(r.Symbol, out var row))
                {
                    row = new CsePriceDaily { Symbol = r.Symbol, TradeDate = sessionDate, CreatedAt = now };
                    _db.CsePriceDaily.Add(row);
                }

                // tradeSummary is the richer source, so it replaces a chart-backfilled row for the same day.
                row.StockId = r.Id;
                row.Open = r.Open > 0 ? r.Open : null;
                row.Close = r.ClosingPrice;
                row.High = r.High > 0 ? Math.Max(r.High, r.ClosingPrice) : r.ClosingPrice;
                row.Low = r.Low > 0 ? Math.Min(r.Low, r.ClosingPrice) : r.ClosingPrice;
                row.PreviousClose = r.PreviousClose > 0 ? r.PreviousClose : null;
                row.ShareVolume = r.ShareVolume;
                row.Trades = r.TradeVolume;
                row.Turnover = r.Turnover;
                row.MarketCap = r.MarketCap > 0 ? r.MarketCap : null;
                row.Status = r.Status;
                row.Source = CseHistorySource.TradeSummary;
                row.UpdatedAt = now;
                written++;
            }
            await _db.SaveChangesAsync(ct);
        }

        // Index closes and the market summary: retried on every run until the day is stored.
        var indices = await RecordIndicesAsync(sessionDate, ct);
        var market = await RecordMarketSummaryAsync(ct);

        var msg = alreadyRecorded
            ? $"Session {sessionDate:yyyy-MM-dd} already recorded."
            : $"Recorded {written} securities for {sessionDate:yyyy-MM-dd}.";
        if (indices > 0) msg += $" {indices} index closes.";
        if (market > 0) msg += $" {market} market-summary days.";
        if (!alreadyRecorded) _logger.LogInformation("CSE history: {Message}", msg);
        return new RecordResult(!alreadyRecorded, sessionDate, written, msg);
    }

    private async Task<int> RecordIndicesAsync(DateOnly sessionDate, CancellationToken ct)
    {
        var written = 0;
        foreach (var (code, endpoint) in new[] { ("ASPI", "aspiData"), ("SNPSL20", "snpData") })
        {
            try
            {
                if (await _db.CseIndexDaily.AnyAsync(i => i.IndexCode == code && i.TradeDate == sessionDate
                        && i.Source == CseHistorySource.TradeSummary, ct))
                    continue;

                using var doc = await PostJsonAsync(endpoint, null, ct);
                var e = doc.RootElement;
                var value = Dec(e, "value");
                var ts = Long(e, "timestamp");
                if (value <= 0 || ts <= 0 || ToColomboDate(ts) != sessionDate) continue;

                var row = await _db.CseIndexDaily.FirstOrDefaultAsync(i => i.IndexCode == code && i.TradeDate == sessionDate, ct);
                if (row is null)
                {
                    row = new CseIndexDaily { IndexCode = code, TradeDate = sessionDate };
                    _db.CseIndexDaily.Add(row);
                }
                row.Close = value;
                row.High = NullIfZero(Dec(e, "highValue"));
                row.Low = NullIfZero(Dec(e, "lowValue"));
                row.Change = Dec(e, "change");
                row.ChangePercent = Math.Round(Dec(e, "percentage"), 6);
                row.Source = CseHistorySource.TradeSummary;
                row.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                written++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning("CSE history: {Index} close not recorded ({Error}).", code, ex.Message);
            }
        }
        return written;
    }

    private async Task<int> RecordMarketSummaryAsync(CancellationToken ct)
    {
        try
        {
            using var doc = await PostJsonAsync("dailyMarketSummery", null, ct);
            var items = new List<JsonElement>();
            Flatten(doc.RootElement, items);

            var written = 0;
            foreach (var e in items)
            {
                var ms = Long(e, "tradeDate");
                if (ms <= 0) continue;
                var date = ToColomboDate(ms);

                var row = await _db.CseMarketDaily.FirstOrDefaultAsync(m => m.TradeDate == date, ct);
                var raw = e.GetRawText();
                if (row is not null && row.RawJson == raw) continue;
                if (row is null)
                {
                    row = new CseMarketDaily { TradeDate = date };
                    _db.CseMarketDaily.Add(row);
                }

                row.MarketTurnover = Dec(e, "marketTurnover");
                row.MarketTrades = Long(e, "marketTrades");
                row.EquityForeignPurchase = Dec(e, "equityForeignPurchase");
                row.EquityForeignSales = Dec(e, "equityForeignSales");
                row.MarketCap = NullIfZero(Dec(e, "marketCap"));
                row.ListedCompanies = (int)Long(e, "listedCompanyNumber");
                row.TradedCompanies = (int)Long(e, "tradeCompanyNumber");
                row.Asi = NullIfZero(Dec(e, "asi"));
                row.SpSl20 = NullIfZero(Dec(e, "spp"));
                row.Per = NullIfZero(Dec(e, "per"));
                row.Pbv = NullIfZero(Dec(e, "pbv"));
                row.DividendYield = NullIfZero(Dec(e, "dy"));
                row.RawJson = raw;
                row.UpdatedAt = DateTime.UtcNow;
                written++;
            }
            if (written > 0) await _db.SaveChangesAsync(ct);
            return written;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning("CSE history: market summary not recorded ({Error}).", ex.Message);
            return 0;
        }

        static void Flatten(JsonElement e, List<JsonElement> into)
        {
            if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Flatten(x, into);
            else if (e.ValueKind == JsonValueKind.Object) into.Add(e);
        }
    }

    // ── Backfill / gap repair ────────────────────────────────────────────────

    public sealed record BackfillResult(int Securities, int Inserted, int Updated, int Failed, int IndexRows, string Message);

    /// <summary>
    /// Fills days from the chart API for every known security. Never overwrites a tradeSummary row.
    /// period 5 ≈ 1 year (initial backfill); period 4 ≈ 2 months (gap repair after downtime).
    /// </summary>
    public async Task<BackfillResult> BackfillAsync(int period, CancellationToken ct)
    {
        period = period >= 5 ? 5 : 4;
        var task = period == 5 ? "backfill" : "repair";

        await Gate.WaitAsync(ct);
        try
        {
            var result = await BackfillCoreAsync(period, ct);
            _status.Set(task, result.Failed == 0, result.Message);
            _logger.LogInformation("CSE history {Task}: {Message}", task, result.Message);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "CSE history {Task} failed.", task);
            _status.Set(task, false, ex.Message);
            return new BackfillResult(0, 0, 0, 0, 0, ex.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<BackfillResult> BackfillCoreAsync(int period, CancellationToken ct)
    {
        // Refresh the security list (ids) from today's trade summary first.
        try { await UpsertSecuritiesAsync(await FetchTradeSummaryAsync(ct), ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning("CSE history: tradeSummary unavailable, using the stored security list ({Error}).", ex.Message);
        }

        var securities = await _db.CseSecurities.AsNoTracking().OrderBy(s => s.Symbol).ToListAsync(ct);
        int inserted = 0, updated = 0, failed = 0;

        foreach (var sec in securities)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var points = await FetchChartAsync(sec.StockId, period, ct);
                if (points.Count > 0)
                {
                    var (ins, upd) = await UpsertChartRowsAsync(sec, points, ct);
                    inserted += ins;
                    updated += upd;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                failed++;
                _logger.LogWarning("CSE history: chart fetch failed for {Symbol} ({Error}).", sec.Symbol, ex.Message);
            }
            await Task.Delay(PoliteDelay, ct);
        }

        var indexRows = 0;
        try { indexRows = await BackfillAspiAsync(period, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            failed++;
            _logger.LogWarning("CSE history: ASPI chart fetch failed ({Error}).", ex.Message);
        }

        return new BackfillResult(securities.Count, inserted, updated, failed, indexRows,
            $"{securities.Count} securities: {inserted} days added, {updated} refreshed, {failed} failed; {indexRows} ASPI days.");
    }

    private sealed record ChartPoint(DateOnly Date, decimal High, decimal Low, decimal Close, long Volume);

    private async Task<(int Inserted, int Updated)> UpsertChartRowsAsync(CseSecurity sec, List<ChartPoint> points, CancellationToken ct)
    {
        var from = points[0].Date;
        var to = points[^1].Date;
        var existing = await _db.CsePriceDaily
            .Where(p => p.Symbol == sec.Symbol && p.TradeDate >= from && p.TradeDate <= to)
            .ToDictionaryAsync(p => p.TradeDate, ct);

        int inserted = 0, updated = 0;
        decimal? prevClose = null;
        var now = DateTime.UtcNow;
        foreach (var pt in points)
        {
            var high = pt.High > 0 ? Math.Max(pt.High, pt.Close) : pt.Close;
            var low = pt.Low > 0 ? Math.Min(pt.Low, pt.Close) : pt.Close;

            if (existing.TryGetValue(pt.Date, out var row))
            {
                // A tradeSummary row is richer and authoritative; only refresh other chart rows.
                if (row.Source == CseHistorySource.Chart &&
                    (row.Close != pt.Close || row.High != high || row.Low != low || row.ShareVolume != pt.Volume))
                {
                    row.High = high; row.Low = low; row.Close = pt.Close; row.ShareVolume = pt.Volume;
                    row.PreviousClose ??= prevClose;
                    row.UpdatedAt = now;
                    updated++;
                }
            }
            else
            {
                _db.CsePriceDaily.Add(new CsePriceDaily
                {
                    Symbol = sec.Symbol, StockId = sec.StockId, TradeDate = pt.Date,
                    High = high, Low = low, Close = pt.Close, PreviousClose = prevClose, ShareVolume = pt.Volume,
                    Source = CseHistorySource.Chart, CreatedAt = now, UpdatedAt = now,
                });
                inserted++;
            }
            prevClose = pt.Close;
        }

        if (inserted + updated > 0) await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return (inserted, updated);
    }

    private async Task<int> BackfillAspiAsync(int period, CancellationToken ct)
    {
        using var doc = await PostJsonAsync("chartData", new()
        {
            ["symbol"] = "ASPI", ["chartId"] = "1", ["period"] = period.ToString(CultureInfo.InvariantCulture),
        }, ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return 0;

        // Several intraday points can share a date; the last one of the day is the close.
        var byDate = new SortedDictionary<DateOnly, decimal>();
        foreach (var p in doc.RootElement.EnumerateArray())
        {
            var ms = Long(p, "d");
            var v = Dec(p, "v");
            if (ms > 0 && v > 0) byDate[ToColomboDate(ms)] = v;
        }
        if (byDate.Count == 0) return 0;

        var first = byDate.Keys.First();
        var existing = await _db.CseIndexDaily
            .Where(i => i.IndexCode == "ASPI" && i.TradeDate >= first)
            .ToDictionaryAsync(i => i.TradeDate, ct);

        var written = 0;
        decimal? prev = null;
        foreach (var (date, value) in byDate)
        {
            if (existing.TryGetValue(date, out var row))
            {
                if (row.Source == CseHistorySource.Chart && row.Close != value) { row.Close = value; row.UpdatedAt = DateTime.UtcNow; written++; }
            }
            else
            {
                _db.CseIndexDaily.Add(new CseIndexDaily
                {
                    IndexCode = "ASPI", TradeDate = date, Close = value,
                    Change = prev is { } pv ? value - pv : null,
                    ChangePercent = prev is { } pp && pp != 0 ? Math.Round((value / pp - 1) * 100, 6) : null,
                    Source = CseHistorySource.Chart,
                });
                written++;
            }
            prev = value;
        }
        if (written > 0) await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return written;
    }

    // ── Reads (history endpoint, admin) ──────────────────────────────────────

    /// <summary>
    /// Daily bars from the database for the chart endpoint. Returns null when the database doesn't
    /// cover the requested range yet (the caller then falls back to the live CSE chart).
    /// range: 1W, 1M, 3M, 1Y, ALL. Missing opens use the previous close, like the live chart.
    /// </summary>
    public async Task<List<PriceHistoryPointDto>?> GetDailyHistoryAsync(string symbol, string range, CancellationToken ct)
    {
        symbol = CseSymbols.ToLookup(symbol);
        range = (range ?? "1M").ToUpperInvariant();
        var today = TodayInColombo();
        var from = range switch
        {
            "1W" => today.AddDays(-7),
            "1M" => today.AddDays(-31),
            "3M" => today.AddDays(-92),
            "1Y" => today.AddDays(-366),
            "ALL" => DateOnly.MinValue,
            _ => today.AddDays(-31),
        };

        // One extra earlier row supplies the first bar's previous close.
        var rows = await _db.CsePriceDaily.AsNoTracking()
            .Where(p => p.Symbol == symbol && p.TradeDate >= from)
            .OrderBy(p => p.TradeDate)
            .Select(p => new { p.TradeDate, p.Open, p.High, p.Low, p.Close, p.PreviousClose, p.ShareVolume })
            .ToListAsync(ct);
        if (rows.Count < 2) return null;

        // Only trust the database once it reaches back to (about) the start of the range.
        var covers = range == "ALL"
            ? rows[0].TradeDate <= today.AddDays(-300) || rows.Count >= 150
            : rows[0].TradeDate <= from.AddDays(14);
        if (!covers) return null;

        var points = new List<PriceHistoryPointDto>(rows.Count);
        decimal? prevClose = null;
        foreach (var r in rows)
        {
            var open = r.Open ?? r.PreviousClose ?? prevClose ?? r.Close;
            var high = Math.Max(r.High, Math.Max(open, r.Close));
            var low = Math.Min(r.Low > 0 ? r.Low : r.Close, Math.Min(open, r.Close));
            points.Add(new PriceHistoryPointDto(ColomboMidnightUnix(r.TradeDate), open, high, low, r.Close, r.ShareVolume));
            prevClose = r.Close;
        }
        return points;
    }

    public async Task<object> GetSummaryAsync(CancellationToken ct)
    {
        var priceRows = await _db.CsePriceDaily.CountAsync(ct);
        DateOnly? first = priceRows == 0 ? null : await _db.CsePriceDaily.MinAsync(p => p.TradeDate, ct);
        DateOnly? last = priceRows == 0 ? null : await _db.CsePriceDaily.MaxAsync(p => p.TradeDate, ct);

        return new
        {
            busy = IsBusy,
            prices = new
            {
                rows = priceRows,
                symbols = await _db.CsePriceDaily.Select(p => p.Symbol).Distinct().CountAsync(ct),
                firstDate = first,
                lastDate = last,
                rowsOnLastDate = last is null ? 0 : await _db.CsePriceDaily.CountAsync(p => p.TradeDate == last, ct),
                fromTradeSummary = await _db.CsePriceDaily.CountAsync(p => p.Source == CseHistorySource.TradeSummary, ct),
                fromChart = await _db.CsePriceDaily.CountAsync(p => p.Source == CseHistorySource.Chart, ct),
            },
            indices = await _db.CseIndexDaily.GroupBy(i => i.IndexCode)
                .Select(g => new { index = g.Key, rows = g.Count(), firstDate = g.Min(i => i.TradeDate), lastDate = g.Max(i => i.TradeDate) })
                .ToListAsync(ct),
            marketSummaryDays = await _db.CseMarketDaily.CountAsync(ct),
            securitiesKnown = await _db.CseSecurities.CountAsync(ct),
            lastRuns = _status.All,
        };
    }

    public Task<List<CsePriceDaily>> GetRowsAsync(DateOnly? from, DateOnly? to, string? symbol, CancellationToken ct)
    {
        var q = _db.CsePriceDaily.AsNoTracking().AsQueryable();
        if (from is { } f) q = q.Where(p => p.TradeDate >= f);
        if (to is { } t) q = q.Where(p => p.TradeDate <= t);
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var s = CseSymbols.ToLookup(symbol);
            q = q.Where(p => p.Symbol == s);
        }
        return q.OrderBy(p => p.TradeDate).ThenBy(p => p.Symbol).ToListAsync(ct);
    }

    // ── CSE calls ────────────────────────────────────────────────────────────

    private sealed record SummaryRow(
        int Id, string Symbol, string? Name, decimal Open, decimal High, decimal Low, decimal ClosingPrice,
        decimal PreviousClose, long ShareVolume, long TradeVolume, decimal Turnover, decimal MarketCap, int? Status,
        DateOnly TradeDate);

    private async Task<List<SummaryRow>> FetchTradeSummaryAsync(CancellationToken ct)
    {
        using var doc = await PostJsonAsync("tradeSummary", null, ct);
        var rows = new List<SummaryRow>();
        if (!doc.RootElement.TryGetProperty("reqTradeSummery", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return rows;

        foreach (var e in arr.EnumerateArray())
        {
            var symbol = Str(e, "symbol");
            var id = (int)Long(e, "id");
            var lastTraded = Long(e, "lastTradedTime");
            if (string.IsNullOrWhiteSpace(symbol) || id <= 0 || lastTraded <= 0) continue;

            rows.Add(new SummaryRow(
                id, symbol.Trim().ToUpperInvariant(), Str(e, "name"),
                Dec(e, "open"), Dec(e, "high"), Dec(e, "low"), Dec(e, "closingPrice"), Dec(e, "previousClose"),
                Long(e, "sharevolume"), Long(e, "tradevolume"), Dec(e, "turnover"), Dec(e, "marketCap"),
                e.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : null,
                ToColomboDate(lastTraded)));
        }
        return rows;
    }

    private async Task UpsertSecuritiesAsync(List<SummaryRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        var known = await _db.CseSecurities.ToDictionaryAsync(s => s.Symbol, StringComparer.OrdinalIgnoreCase, ct);
        var now = DateTime.UtcNow;

        foreach (var r in rows.GroupBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            if (!known.TryGetValue(r.Symbol, out var sec))
            {
                sec = new CseSecurity { Symbol = r.Symbol, FirstSeen = r.TradeDate };
                _db.CseSecurities.Add(sec);
            }
            sec.StockId = r.Id;
            if (!string.IsNullOrWhiteSpace(r.Name)) sec.Name = r.Name!.Length > 255 ? r.Name[..255] : r.Name;
            if (r.TradeDate > sec.LastSeen) sec.LastSeen = r.TradeDate;
            if (r.TradeDate < sec.FirstSeen) sec.FirstSeen = r.TradeDate;
            sec.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task<List<ChartPoint>> FetchChartAsync(int stockId, int period, CancellationToken ct)
    {
        using var doc = await PostJsonAsync("companyChartDataByStock", new()
        {
            ["stockId"] = stockId.ToString(CultureInfo.InvariantCulture),
            ["period"] = period.ToString(CultureInfo.InvariantCulture),
        }, ct);

        var points = new SortedDictionary<DateOnly, ChartPoint>();
        if (doc.RootElement.TryGetProperty("chartData", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in arr.EnumerateArray())
            {
                var ms = Long(p, "t");
                var close = Dec(p, "p");
                if (ms <= 0 || close <= 0) continue;
                var date = ToColomboDate(ms);
                points[date] = new ChartPoint(date, Dec(p, "h"), Dec(p, "l"), close, Long(p, "q"));
            }
        }
        return points.Values.ToList();
    }

    private async Task<JsonDocument> PostJsonAsync(string endpoint, Dictionary<string, string>? form, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var content = form is null ? null : new FormUrlEncodedContent(form);
                using var response = await _http.PostAsync(Api + endpoint, content, ct);
                if ((int)response.StatusCode >= 500 && attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                var text = await response.Content.ReadAsStringAsync(ct);
                return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "null" : text);
            }
            catch (Exception ex) when (attempt < 3 && ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    public static DateOnly ToColomboDate(long unixMs) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime, Colombo));

    private static long ColomboMidnightUnix(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Colombo), TimeSpan.Zero).ToUnixTimeSeconds();
    }

    private static decimal Dec(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : 0m;

    private static long Long(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number) return 0;
        if (v.TryGetInt64(out var l)) return l;
        return v.TryGetDecimal(out var d) ? (long)d : 0;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? NullIfZero(decimal v) => v == 0 ? null : v;

    private static TimeZoneInfo ResolveColombo()
    {
        foreach (var id in new[] { "Asia/Colombo", "Sri Lanka Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("LK", TimeSpan.FromHours(5.5), "Colombo", "Colombo");
    }
}
