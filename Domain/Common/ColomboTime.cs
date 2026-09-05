using System;

namespace dotnetApp.Domain.Common;

/// <summary>
/// Colombo Stock Exchange calendar helpers.
/// Sri Lanka observes a fixed UTC+05:30 offset with no daylight saving, so the offset is
/// hard-coded rather than resolved through the OS time-zone database (whose ids differ
/// between Windows and Linux).
/// </summary>
public static class ColomboTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromMinutes(330);

    /// <summary>Current instant expressed in Colombo local time.</summary>
    public static DateTime Now => DateTime.UtcNow + Offset;

    /// <summary>Today's Colombo calendar date, at midnight.</summary>
    public static DateTime Today => Now.Date;

    /// <summary>
    /// Converts a CSE epoch-milliseconds value into the Colombo calendar date it represents.
    /// CSE publishes record/payment dates as midnight Colombo time, which is 18:30 UTC on the
    /// previous day - taking <c>.UtcDateTime.Date</c> would shift the date back by one day.
    /// </summary>
    public static DateTime DateFromUnixMilliseconds(long milliseconds)
    {
        var utc = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
        return (utc + Offset).Date;
    }
}
