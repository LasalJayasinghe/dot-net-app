using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace dotnetApp;

public class TelegramSettings
{
    public string? BotToken { get; set; }
    public string? ChatId { get; set; }
}

public class TelegramService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelegramService> _logger;
    private readonly TelegramSettings _settings;

    public TelegramService(HttpClient http, IOptions<TelegramSettings> settings , ILogger<TelegramService> logger)
    {
        _httpClient = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<bool> SendMessageAsync(long chatId ,string message)
    {
        Console.WriteLine("Sending Telegram message: " + message);
        Console.WriteLine("Using Bot Token: " + _settings.BotToken);
        var url = $"https://api.telegram.org/bot{_settings.BotToken}/sendMessage";

        var payload = new
        {
            chat_id = _settings.ChatId,
            text = message
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }
            else
            {
                _logger.LogError("Failed to send Telegram message. Status code: {StatusCode}", response.StatusCode);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while sending Telegram message.");
            return false;
        }
    }

}