using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using dotnetApp.Infrastructure.Data;
using dotnetApp.Domain.Common;
using dotnetApp.Domain.Entities;

namespace dotnetApp.Jobs
{
    public class CseDividendJob : BackgroundService
    {
        private readonly ILogger<CseDividendJob> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceProvider _services;
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
        private const string BaseUrl = "https://www.cse.lk/api/";
        private const string CseDateFormat = "dd MMM yyyy";

        // Bounds the fan-out of per-announcement detail requests against cse.lk.
        private const int MaxConcurrentDetailRequests = 4;

        // HttpClient has no timeout by default here, so a hung CSE response would stall the cycle.
        private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);

        public CseDividendJob(
            ILogger<CseDividendJob> logger,
            IHttpClientFactory httpClientFactory,
            IServiceProvider services)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _services = services;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CSE Dividend Job started.");

            try
            {
                await FetchAndStoreDividendsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching dividends on startup run.");
            }

            var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await FetchAndStoreDividendsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching dividends.");
                }
            }
        }

        private async Task FetchAndStoreDividendsAsync(CancellationToken ct)
        {
            _logger.LogInformation("Starting CSE dividend fetch cycle...");

            var client = _httpClientFactory.CreateClient();
            client.Timeout = HttpTimeout;
            client.DefaultRequestHeaders.Add("accept", "application/json, text/plain, */*");

            // 1. Get list of approved dividend announcements
            var ids = await FetchAnnouncementIdsAsync(client, ct);
            if (ids.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Fetched {Count} dividend announcement IDs from CSE.", ids.Count);

            // 2. Fetch announcement details concurrently, but capped so a large cycle cannot open
            //    an unbounded number of sockets against cse.lk. A failure on one announcement is
            //    logged and skipped rather than aborting the whole cycle.
            var announcements = await FetchAnnouncementDetailsAsync(client, ids, ct);
            if (announcements.Count == 0)
            {
                _logger.LogWarning("No dividend announcement details could be fetched this cycle.");
                return;
            }

            // 3. Persist through a single scope/DbContext and one SaveChanges, rather than a
            //    scope, connection and transaction per announcement.
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var cseIds = announcements.Select(a => a.id).ToList();
            var externalIds = cseIds.Select(NewsExternalId).ToList();

            var existingDividends = await db.Dividends
                .Where(d => cseIds.Contains(d.CseAnnouncementId))
                .ToDictionaryAsync(d => d.CseAnnouncementId, ct);

            var existingNews = await db.NewsArticles
                .Where(n => externalIds.Contains(n.ExternalId))
                .ToDictionaryAsync(n => n.ExternalId, ct);

            foreach (var announcement in announcements)
            {
                UpsertDividend(db, announcement, existingDividends);
                UpsertNews(db, announcement, existingNews);
            }

            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Completed CSE dividend fetch cycle. Upserted {Count} dividend records.",
                announcements.Count);
        }

        private async Task<List<string>> FetchAnnouncementIdsAsync(HttpClient client, CancellationToken ct)
        {
            var listContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("type", "SCRIP DIVIDEND"),
                new KeyValuePair<string, string>("fromDate", DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd")),
                new KeyValuePair<string, string>("toDate", DateTime.UtcNow.ToString("yyyy-MM-dd")),
                new KeyValuePair<string, string>("announcementCategories", "CASH DIVIDEND")
            });

            var listResponse = await client.PostAsync(BaseUrl + "approvedAnnouncement", listContent, ct);
            listResponse.EnsureSuccessStatusCode();

            var listJson = await listResponse.Content.ReadAsStringAsync(ct);
            using var listDoc = JsonDocument.Parse(listJson);
            var ids = ExtractAnnouncementIds(listDoc.RootElement);

            if (ids.Count == 0)
            {
                var preview = listJson.Length > 500 ? listJson.Substring(0, 500) : listJson;
                _logger.LogWarning("Unexpected list payload - cannot extract IDs. Payload preview: {Preview}", preview);
            }

            return ids;
        }

        private async Task<List<ReqBaseAnnouncement>> FetchAnnouncementDetailsAsync(
            HttpClient client,
            List<string> ids,
            CancellationToken ct)
        {
            var results = new ReqBaseAnnouncement?[ids.Count];
            using var gate = new SemaphoreSlim(MaxConcurrentDetailRequests);

            var tasks = ids.Select(async (id, index) =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    results[index] = await FetchAnnouncementDetailAsync(client, id, ct);
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks);

            // De-duplicate: the CSE list endpoint can surface the same announcement twice, and two
            // rows with one CseAnnouncementId would violate the unique index inside one SaveChanges.
            var seen = new HashSet<int>();
            var announcements = new List<ReqBaseAnnouncement>(ids.Count);
            foreach (var announcement in results)
            {
                if (announcement != null && seen.Add(announcement.id))
                {
                    announcements.Add(announcement);
                }
            }

            return announcements;
        }

        private async Task<ReqBaseAnnouncement?> FetchAnnouncementDetailAsync(
            HttpClient client,
            string id,
            CancellationToken ct)
        {
            try
            {
                var detailContent = new FormUrlEncodedContent(new[]
                    { new KeyValuePair<string, string>("announcementId", id) });

                var detailResponse = await client.PostAsync(BaseUrl + "getAnnouncementById", detailContent, ct);
                detailResponse.EnsureSuccessStatusCode();

                var detailJson = await detailResponse.Content.ReadAsStringAsync(ct);
                var detail = JsonSerializer.Deserialize<CseAnnouncementResponse>(detailJson);

                return detail?.reqBaseAnnouncement;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Skipping CSE announcement {AnnouncementId} - detail fetch failed.", id);
                return null;
            }
        }

        private static void UpsertDividend(
            AppDbContext db,
            ReqBaseAnnouncement announcement,
            Dictionary<int, Dividend> existing)
        {
            var symbol = TrimToMaxLength(announcement.symbol, 20) ?? string.Empty;

            var hasAnnouncementDate = DateTime.TryParseExact(
                announcement.dateOfAnnouncement,
                CseDateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateOfAnnouncement);

            var tracked = existing.TryGetValue(announcement.id, out var found) ? found : null;
            var target = tracked ?? new Dividend();

            // Id is a surrogate key and is never taken from the CSE payload; CseAnnouncementId
            // carries the external identity and is uniquely indexed.
            target.CseAnnouncementId = announcement.id;
            target.Symbol = symbol;
            target.BaseSymbol = BaseSymbolOf(symbol);
            target.CompanyName = TrimToMaxLength(announcement.companyName, 200) ?? string.Empty;
            target.DateOfAnnouncement = hasAnnouncementDate ? dateOfAnnouncement.Date : ColomboTime.Today;
            target.VotingDivPerShare = announcement.votingDivPerShare;
            target.NonVotingDivPerShare = announcement.nonVotingDivPerShare;
            target.FinancialYear = TrimToMaxLength(announcement.financialYear, 50);
            // CSE sends record dates as midnight Colombo time; converting straight to UTC would
            // land on the previous calendar day.
            target.RecordDate = ColomboTime.DateFromUnixMilliseconds(announcement.recordDate);
            target.Remarks = announcement.remarks;
            target.PaymentDateRaw = TrimToMaxLength(announcement.payment, 100);
            target.AgmDateRaw = TrimToMaxLength(announcement.agm, 100);
            target.PaymentDate = ParseCseDate(announcement.payment);
            target.AgmDate = ParseCseDate(announcement.agm);

            if (tracked == null)
            {
                db.Dividends.Add(target);
            }
        }

        private static void UpsertNews(
            AppDbContext db,
            ReqBaseAnnouncement announcement,
            Dictionary<string, NewsArticle> existing)
        {
            var externalId = NewsExternalId(announcement.id);
            var tracked = existing.TryGetValue(externalId, out var found) ? found : null;
            var target = tracked ?? new NewsArticle();

            target.ExternalId = externalId;
            target.Title = $"DIVIDEND ANNOUNCEMENT - {announcement.companyName}";
            target.Summary = announcement.remarks ?? "Dividend announcement";
            target.OriginalUrl = $"https://www.cse.lk/general-announcements?id={announcement.id}&type=SCRIP+DIVIDEND";
            target.Source = "CSE";
            target.MarketCategory = "CSE_STOCKS";
            target.Sentiment = "NEUTRAL";
            target.ImportanceScore = 5;
            target.IsModelValidated = true;
            target.IsRelevant = true;
            target.ValidationReasoning = "Automatically generated dividend news";

            if (tracked == null)
            {
                // Only stamped on insert, so an update does not push the article back to the top
                // of the news feed on every hourly cycle.
                target.PublishedAt = DateTime.UtcNow;
                db.NewsArticles.Add(target);
            }
        }

        private static string NewsExternalId(int announcementId) => $"cse-dividend-{announcementId}";

        /// <summary>Strips the CSE class suffix: "CSLK.N0000" becomes "CSLK".</summary>
        private static string BaseSymbolOf(string symbol)
        {
            var dot = symbol.IndexOf('.');
            return dot >= 0 ? symbol[..dot] : symbol;
        }

        /// <summary>Parses a CSE date such as "25 Sep 2026"; null when absent or unrecognised.</summary>
        private static DateTime? ParseCseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTime.TryParseExact(
                value.Trim(),
                CseDateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)
                ? parsed.Date
                : null;
        }

        private static List<string> ExtractAnnouncementIds(JsonElement root)
        {
            var highConfidence = new HashSet<string>(StringComparer.Ordinal);
            var lowConfidence = new HashSet<string>(StringComparer.Ordinal);

            void Scan(JsonElement node)
            {
                if (node.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in node.EnumerateArray())
                    {
                        Scan(item);
                    }

                    return;
                }

                if (node.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                string? localId = null;
                var hasAnnouncementHints = false;

                foreach (var prop in node.EnumerateObject())
                {
                    var name = prop.Name;
                    if (name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("announcementId", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryGetIdString(prop.Value, out var parsedId))
                        {
                            localId = parsedId;
                            lowConfidence.Add(parsedId);
                        }
                    }

                    if (name.Equals("symbol", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("companyName", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("dateOfAnnouncement", StringComparison.OrdinalIgnoreCase))
                    {
                        hasAnnouncementHints = true;
                    }

                    Scan(prop.Value);
                }

                if (localId != null && hasAnnouncementHints)
                {
                    highConfidence.Add(localId);
                }
            }

            Scan(root);

            var source = highConfidence.Count > 0 ? highConfidence : lowConfidence;
            return source.ToList();
        }

        private static bool TryGetIdString(JsonElement idElement, out string id)
        {
            id = string.Empty;

            if (idElement.ValueKind == JsonValueKind.Number)
            {
                if (idElement.TryGetInt64(out var int64Id))
                {
                    id = int64Id.ToString();
                    return true;
                }

                if (idElement.TryGetDecimal(out var decimalId))
                {
                    id = decimalId.ToString();
                    return true;
                }
            }

            if (idElement.ValueKind == JsonValueKind.String)
            {
                var text = idElement.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    id = text.Trim();
                    return true;
                }
            }

            return false;
        }

        private static string? TrimToMaxLength(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim();
            return normalized.Length <= maxLength ? normalized : normalized.Substring(0, maxLength);
        }

        // DTOs matching the CSE API response
        public class CseAnnouncementResponse
        {
            public ReqBaseAnnouncement? reqBaseAnnouncement { get; set; }
            public List<ReqAnnouncementDoc>? reqAnnouncementDocs { get; set; }
        }

        public class ReqBaseAnnouncement
        {
            public int id { get; set; }
            public string? dType { get; set; }
            public string? dateOfAnnouncement { get; set; }
            public string? symbol { get; set; }
            public string? companyName { get; set; }
            public decimal votingDivPerShare { get; set; }
            public decimal nonVotingDivPerShare { get; set; }
            public string? financialYear { get; set; }
            public long recordDate { get; set; }
            public string? remarks { get; set; }
            public string? payment { get; set; }
            public string? agm { get; set; }
        }

        public class ReqAnnouncementDoc
        {
            public int id { get; set; }
            public string? fileName { get; set; }
            public string? fileUrl { get; set; }
            public int fileSize { get; set; }
            public string? fileOriginalName { get; set; }
            public string? contentType { get; set; }
            public string? status { get; set; }
            public string? baseUrl { get; set; }
        }
    }
}
