namespace dotnetApp.Domain.Entities;

/// <summary>
/// A push destination for one installation of the mobile app.
///
/// The value stored is an Expo push token (ExponentPushToken[...]), not a raw
/// FCM/APNs token — the app talks to Expo's push service, which fans out to the
/// right vendor per platform. One user can have several rows (phone + tablet,
/// or a reinstall that minted a new token), so alerts are sent to every active
/// row for that user.
/// </summary>
public class DeviceToken
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    /// <summary>The Expo push token. Unique across the table — a device that is
    /// handed to a second account moves rather than duplicates.</summary>
    public string Token { get; set; } = null!;

    /// <summary>"ios" | "android" — reported by the client, for diagnostics only.</summary>
    public string? Platform { get; set; }

    /// <summary>Human-readable device name, e.g. "Pixel 8". Diagnostics only.</summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// Cleared when Expo reports the token as DeviceNotRegistered (app deleted,
    /// notifications revoked). The row is kept rather than deleted so a
    /// re-install that mints the same token can simply be flipped back on.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Refreshed every time the app re-registers, so stale rows are identifiable.</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser User { get; set; } = null!;
}
