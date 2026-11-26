using System;
using System.Collections.Generic;
using System.Linq;
using dotnetApp.Domain.Common;

namespace dotnetApp.Application.Services.Desk;

/// <summary>One intraday index reading.</summary>
public record IndexPoint(DateTime At, decimal Aspi, decimal SpSl20);

/// <summary>
/// Today's ASPI / S&amp;P SL20 readings, appended by <c>AlertJob</c> on each index poll. cse.lk only
/// serves the current index value, so this is how the desk gets an intraday line. In memory only:
/// a restart loses the morning's points, which affects nothing but that day's chart.
/// </summary>
public class MarketIntradayStore
{
    private readonly object _gate = new();
    private readonly List<IndexPoint> _points = new();
    private DateOnly _day;

    public void Append(decimal aspi, decimal spSl20)
    {
        if (aspi <= 0) return;
        var today = DateOnly.FromDateTime(ColomboTime.Now);
        lock (_gate)
        {
            if (today != _day)
            {
                _points.Clear();
                _day = today;
            }
            // Skip unchanged readings (the closed-market polls) so the series stays small.
            if (_points.Count > 0 && _points[^1].Aspi == aspi && _points[^1].SpSl20 == spSl20) return;
            _points.Add(new IndexPoint(DateTime.UtcNow, aspi, spSl20));
        }
    }

    /// <summary>Today's points, oldest first (empty before the first poll of the day).</summary>
    public IReadOnlyList<IndexPoint> Today()
    {
        var today = DateOnly.FromDateTime(ColomboTime.Now);
        lock (_gate)
            return _day == today ? _points.ToList() : new List<IndexPoint>();
    }
}
