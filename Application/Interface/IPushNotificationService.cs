namespace dotnetApp.Application.Interface;

/// <summary>
/// Delivers a push notification to every device a user has registered.
///
/// Implementations must never throw at the caller: a push is a best-effort
/// courtesy alongside the real delivery channel (Telegram, email), and a dead
/// push provider must not abort the alert loop that called it.
/// </summary>
public interface IPushNotificationService
{
    /// <returns>How many devices the notification was accepted for.</returns>
    Task<int> SendToUserAsync(
        string userId,
        string title,
        string body,
        IDictionary<string, object?>? data = null,
        CancellationToken cancellationToken = default);
}
