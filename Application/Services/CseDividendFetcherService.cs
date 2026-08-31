using System;
using System.Collections.Generic;
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
using dotnetApp.Domain.Entities;

namespace dotnetApp.Application.Services
{
    public class CseDividendFetcherService : BackgroundService
    {
        private readonly ILogger<CseDividendFetcherService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceProvider _services;
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
        private const string BaseUrl = "https://www.cse.lk/api/";

        public CseDividendFetcherService(
            ILogger<CseDividendFetcherService> logger,
            IHttpClientFactory httpClientFactory,
            IServiceProvider services)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _services = services;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CSE Dividend Fetcher started.");
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
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("accept", "application/json, text/plain, */*");

            // 1️⃣ Get list of approved dividend announcements
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
                _logger.LogWarning("Unexpected list payload – cannot extract IDs. Payload preview: {Preview}", preview);
                return;
            }

            // 2️⃣ Fetch each announcement details and store
            foreach (var id in ids)
            {
                var detailContent = new FormUrlEncodedContent(new[]
                    { new KeyValuePair<string, string>("announcementId", id) });
                var detailResponse = await client.PostAsync(BaseUrl + "getAnnouncementById", detailContent, ct);
                detailResponse.EnsureSuccessStatusCode();
                var detailJson = await detailResponse.Content.ReadAsStringAsync(ct);
                var detail = JsonSerializer.Deserialize<CseAnnouncementResponse>(detailJson);
                if (detail?.reqBaseAnnouncement == null) continue;

                var announcement = detail.reqBaseAnnouncement;

                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var hasDate = DateTime.TryParseExact(
                    announcement.dateOfAnnouncement,
                    "dd MMM yyyy",
                    null,
                    System.Globalization.DateTimeStyles.None,
                    out var dateOfAnnouncement);

                // Save Dividend entity
                var dividend = new Dividend
                {
                    Id = announcement.id,
                    Symbol = announcement.symbol ?? string.Empty,
                    CompanyName = announcement.companyName ?? string.Empty,
                    DateOfAnnouncement = hasDate ? dateOfAnnouncement : DateTime.UtcNow,
                    VotingDivPerShare = announcement.votingDivPerShare,
                    NonVotingDivPerShare = announcement.nonVotingDivPerShare,
                    FinancialYear = TrimToMaxLength(announcement.financialYear, 50),
                    RecordDate = DateTimeOffset.FromUnixTimeMilliseconds(announcement.recordDate).UtcDateTime,
                    Remarks = announcement.remarks,
                    PaymentDate = announcement.payment,
                    AgmDate = announcement.agm
                };
                var existingDiv = await db.Dividends.FindAsync(dividend.Id);
                if (existingDiv == null)
                    db.Dividends.Add(dividend);
                else
                    db.Entry(existingDiv).CurrentValues.SetValues(dividend);

                // Save NewsArticle entry for the dividend announcement
                var news = new NewsArticle
                {
                    ExternalId = $"cse-dividend-{announcement.id}",
                    Title = $"DIVIDEND ANNOUNCEMENT - {announcement.companyName}",
                    Summary = announcement.remarks ?? "Dividend announcement",
                    OriginalUrl = $"https://www.cse.lk/general-announcements?id={announcement.id}&type=SCRIP+DIVIDEND",
                    Source = "CSE",
                    MarketCategory = "CSE_STOCKS",
                    Sentiment = "NEUTRAL",
                    ImportanceScore = 5,
                    PublishedAt = DateTime.UtcNow,
                    IsModelValidated = true,
                    IsRelevant = true,
                    ValidationReasoning = "Automatically generated dividend news"
                };
                var existingNews = await db.NewsArticles.FirstOrDefaultAsync(n => n.ExternalId == news.ExternalId, ct);
                if (existingNews == null)
                    db.NewsArticles.Add(news);
                else
                    db.Entry(existingNews).CurrentValues.SetValues(news);

                await db.SaveChangesAsync(ct);
            }
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
