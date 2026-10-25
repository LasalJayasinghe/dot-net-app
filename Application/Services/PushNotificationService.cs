using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using dotnetApp.Application.Interface;
using dotnetApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace dotnetApp.Application.Services;

public class PushSettings
{
    /// <summary>Turns every send into a no-op without touching call sites.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional Expo access token. Only required once push security is enabled
    /// on the Expo project; without it Expo accepts unauthenticated sends.
    /// </summary>
    public string? ExpoAccessToken { get; set; }
}

/// <summary>
/// Sends through Expo's push service, which forwards to FCM on Android and APNs
/// on iOS. Going through Expo rather than the vendors directly is what lets the
/// API hold one opaque token per device and no Firebase service account.
/// </summary>
public class PushNotificationService : IPushNotificationService
{
    // Expo caps a single request at 100 messages.
    private const int BatchSize = 100;
    private const string SendUrl = "https://exp.host/--/api/v2/push/send";

    private readonly HttpClient _httpClient;
    private readonly AppDbContext _dbContext;
    private readonly PushSettings _settings;
    private readonly ILogger<PushNotificationService> _logger;

    public PushNotificationService(
        HttpClient httpClient,
        AppDbContext dbContext,
        IOptions<PushSettings> settings,
        ILogger<PushNotificationService> logger)
    {
        _httpClient = httpClient;
        _dbContext = dbContext;
        _settings = settings.Value;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_settings.ExpoAccessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.ExpoAccessToken);
        }
    }

    public async Task<int> SendToUserAsync(
        string userId,
        string title,
        string body,
        IDictionary<string, object?>? data = null,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled || string.IsNullOrWhiteSpace(userId)) return 0;

        try
        {
            var tokens = await _dbContext.DeviceTokens
                .AsNoTracking()
                .Where(d => d.UserId == userId && d.IsActive)
                .Select(d => d.Token)
                .ToListAsync(cancellationToken);

            if (tokens.Count == 0) return 0;

            var accepted = 0;
            for (var i = 0; i < tokens.Count; i += BatchSize)
            {
                var batch = tokens.Skip(i).Take(BatchSize).ToList();
                accepted += await SendBatchAsync(batch, title, body, data, cancellationToken);
            }

            return accepted;
        }
        catch (Exception ex)
        {
            // Swallowed on purpose - see IPushNotificationService.
            _logger.LogError(ex, "Push notification to user {UserId} failed.", userId);
            return 0;
        }
    }

    private async Task<int> SendBatchAsync(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        IDictionary<string, object?>? data,
        CancellationToken cancellationToken)
    {
        var messages = tokens.Select(token => new ExpoPushMessage
        {
            To = token,
            Title = title,
            Body = body,
            Data = data,
            Sound = "default",
            // Price alerts are time-critical; "high" wakes the device rather than
            // letting Android batch the message into the next maintenance window.
            Priority = "high",
            ChannelId = "alerts",
        }).ToList();

        var response = await _httpClient.PostAsJsonAsync(SendUrl, messages, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Expo push rejected the batch. Status: {StatusCode}. Body: {Body}",
                response.StatusCode, detail);
            return 0;
        }

        var payload = await response.Content.ReadFromJsonAsync<ExpoPushResponse>(
            cancellationToken: cancellationToken);

        var tickets = payload?.Data;
        if (tickets == null) return 0;

        var accepted = 0;
        var deadTokens = new List<string>();

        // Tickets come back in request order, so index i belongs to tokens[i].
        for (var i = 0; i < tickets.Count && i < tokens.Count; i++)
        {
            var ticket = tickets[i];
            if (string.Equals(ticket.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                accepted++;
                continue;
            }

            var error = ticket.Details?.Error;
            if (string.Equals(error, "DeviceNotRegistered", StringComparison.OrdinalIgnoreCase))
            {
                // The app was uninstalled or notifications were revoked. Retrying
                // forever would get the whole project rate-limited by Expo.
                deadTokens.Add(tokens[i]);
            }
            else
            {
                _logger.LogWarning(
                    "Expo push ticket failed. Error: {Error}. Message: {Message}",
                    error ?? "unknown", ticket.Message);
            }
        }

        if (deadTokens.Count > 0) await DeactivateAsync(deadTokens, cancellationToken);

        return accepted;
    }

    private async Task DeactivateAsync(List<string> tokens, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.DeviceTokens
            .Where(d => tokens.Contains(d.Token))
            .ToListAsync(cancellationToken);

        foreach (var row in rows) row.IsActive = false;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Deactivated {Count} unreachable device token(s).", rows.Count);
    }

    // ── Expo wire format ──────────────────────────────────────────────────────

    private sealed class ExpoPushMessage
    {
        [JsonPropertyName("to")] public string To { get; set; } = null!;
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("sound")] public string? Sound { get; set; }
        [JsonPropertyName("priority")] public string? Priority { get; set; }
        [JsonPropertyName("channelId")] public string? ChannelId { get; set; }

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IDictionary<string, object?>? Data { get; set; }
    }

    private sealed class ExpoPushResponse
    {
        [JsonPropertyName("data")] public List<ExpoPushTicket>? Data { get; set; }
    }

    private sealed class ExpoPushTicket
    {
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("details")] public ExpoPushTicketDetails? Details { get; set; }
    }

    private sealed class ExpoPushTicketDetails
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}
