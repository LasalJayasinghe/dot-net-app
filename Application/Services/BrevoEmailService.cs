using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using dotnetApp.Application.Interface;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace dotnetApp.Application.Services;

public class BrevoEmailService : IBrevoEmailService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BrevoEmailService> _logger;

    public BrevoEmailService(HttpClient httpClient, IConfiguration configuration, ILogger<BrevoEmailService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string purpose)
    {
        var apiKey = _configuration["Brevo:ApiKey"] ?? Environment.GetEnvironmentVariable("BREVO_API_KEY");
        var senderEmail = _configuration["Brevo:SenderEmail"] ?? "noreply@alertme.io";
        var senderName = _configuration["Brevo:SenderName"] ?? "AlertMe Trading Desk";

        _logger.LogInformation("==================================================");
        _logger.LogInformation($"[OTP DEBUG LOG] Purpose: {purpose} | Email: {toEmail} | Code: {otpCode}");
        _logger.LogInformation("==================================================");

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("YOUR_BREVO_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Brevo API Key is not configured. OTP logged to console above.");
            return true; // Return true in dev mode so flow succeeds
        }

        try
        {
            var subjectTitle = purpose == "SignUp" ? "Verification Code for AlertMe Registration" : "Password Reset Code for AlertMe Account";
            var actionText = purpose == "SignUp" ? "complete your AlertMe account registration" : "reset your AlertMe account password";

            var htmlTemplate = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body {{ font-family: 'DM Sans', -apple-system, sans-serif; background-color: #18181B; color: #FAFAFA; margin: 0; padding: 40px 20px; }}
        .card {{ max-width: 500px; margin: 0 auto; background-color: #27272A; border: 1px solid #3F3F46; border-radius: 8px; padding: 32px; }}
        .badge {{ font-family: monospace; font-size: 11px; text-transform: uppercase; letter-spacing: 0.15em; color: #EA580C; background: #3F2318; padding: 4px 10px; border-radius: 4px; display: inline-block; }}
        .title {{ font-size: 24px; font-weight: 700; margin-top: 16px; color: #FAFAFA; }}
        .otp-box {{ background-color: #18181B; border: 1px solid #52525B; border-radius: 6px; padding: 20px; text-align: center; font-family: monospace; font-size: 36px; font-weight: 700; letter-spacing: 0.35em; color: #FAFAFA; margin: 24px 0; }}
        .text {{ font-size: 14px; line-height: 1.6; color: #A1A1AA; }}
        .footer {{ margin-top: 32px; font-size: 12px; color: #71717A; text-align: center; border-top: 1px solid #3F3F46; padding-top: 16px; }}
    </style>
</head>
<body>
    <div class=""card"">
        <span class=""badge"">AlertMe Desk Security</span>
        <div class=""title"">{subjectTitle}</div>
        <p class=""text"">Use the 6-digit verification code below to {actionText}. This code will expire in 10 minutes.</p>
        <div class=""otp-box"">{otpCode}</div>
        <p class=""text"">If you did not request this code, please ignore this email.</p>
        <div class=""footer"">AlertMe Trading Desk &copy; {DateTime.UtcNow.Year}. All rights reserved.</div>
    </div>
</body>
</html>";

            var payload = new
            {
                sender = new { name = senderName, email = senderEmail },
                to = new[] { new { email = toEmail } },
                subject = subjectTitle,
                htmlContent = htmlTemplate
            };

            var requestJson = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
            request.Headers.Add("api-key", apiKey);
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation($"Successfully dispatched OTP email via Brevo to {toEmail}. Brevo response: {responseContent}");
                return true;
            }
            else
            {
                _logger.LogError($"Brevo API returned error status {response.StatusCode}: {responseContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Exception occurred while sending OTP email to {toEmail}");
            return false;
        }
    }
}
