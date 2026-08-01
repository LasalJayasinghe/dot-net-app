using Microsoft.AspNetCore.SignalR;

/// <summary>
/// SignalR hub for the Crypto Trading Dashboard.
/// Clients subscribe here to receive real-time candle and ticker push events
/// originating from BinanceService WebSocket streams.
/// </summary>
public class CryptoHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
