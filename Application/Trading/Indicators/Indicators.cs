namespace dotnetApp.Application.Trading.Indicators;

// All indicators are streaming: each Update() is O(1) (amortized for rolling extremes)
// and keeps only the window it needs, so strategies never rescan candle history.

/// <summary>Fixed-capacity circular buffer. Index 0 = newest.</summary>
public sealed class RingBuffer<T>
{
    private readonly T[] _items;
    private int _start;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new T[capacity];
    }

    public int Count { get; private set; }
    public int Capacity => _items.Length;
    public bool IsFull => Count == _items.Length;

    /// <summary>Adds an item; returns true and the evicted item when the buffer was full.</summary>
    public bool Add(T item, out T evicted)
    {
        if (Count < _items.Length)
        {
            _items[(_start + Count) % _items.Length] = item;
            Count++;
            evicted = default!;
            return false;
        }

        evicted = _items[_start];
        _items[_start] = item;
        _start = (_start + 1) % _items.Length;
        return true;
    }

    public void Add(T item) => Add(item, out _);

    /// <summary>ago = 0 is the newest item, ago = Count - 1 the oldest.</summary>
    public T this[int ago]
    {
        get
        {
            if ((uint)ago >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(ago));
            return _items[(_start + Count - 1 - ago) % _items.Length];
        }
    }
}

/// <summary>Simple moving average with running sum.</summary>
public sealed class Sma
{
    private readonly RingBuffer<decimal> _window;
    private decimal _sum;

    public Sma(int period) => _window = new RingBuffer<decimal>(period);

    public bool IsReady => _window.IsFull;
    public decimal Value => _window.Count == 0 ? 0 : _sum / _window.Count;

    public decimal Update(decimal value)
    {
        if (_window.Add(value, out var old)) _sum -= old;
        _sum += value;
        return Value;
    }
}

/// <summary>Rolling mean / population standard deviation via running sum and sum of squares.</summary>
public sealed class RollingStats
{
    private readonly RingBuffer<decimal> _window;
    private decimal _sum, _sumSq;

    public RollingStats(int period) => _window = new RingBuffer<decimal>(period);

    public bool IsReady => _window.IsFull;
    public decimal Mean => _window.Count == 0 ? 0 : _sum / _window.Count;

    public decimal StdDev
    {
        get
        {
            if (_window.Count == 0) return 0;
            var mean = Mean;
            var variance = _sumSq / _window.Count - mean * mean;
            return variance <= 0 ? 0 : (decimal)Math.Sqrt((double)variance);
        }
    }

    public void Update(decimal value)
    {
        if (_window.Add(value, out var old))
        {
            _sum -= old;
            _sumSq -= old * old;
        }
        _sum += value;
        _sumSq += value * value;
    }

    /// <summary>(value − mean) / σ, or 0 when σ is 0.</summary>
    public decimal ZScore(decimal value)
    {
        var sd = StdDev;
        return sd == 0 ? 0 : (value - Mean) / sd;
    }
}

/// <summary>Exponential moving average, seeded with the SMA of the first <c>period</c> values.</summary>
public sealed class Ema
{
    private readonly int _period;
    private readonly decimal _k;
    private decimal _seedSum;
    private int _count;

    public Ema(int period)
    {
        _period = period;
        _k = 2m / (period + 1);
    }

    public bool IsReady => _count >= _period;
    public decimal Value { get; private set; }

    public decimal Update(decimal value)
    {
        if (_count < _period)
        {
            _count++;
            _seedSum += value;
            Value = _seedSum / _count;
        }
        else
        {
            Value += (value - Value) * _k;
        }
        return Value;
    }
}

/// <summary>Wilder's RSI.</summary>
public sealed class Rsi
{
    private readonly int _period;
    private decimal? _prev;
    private decimal _avgGain, _avgLoss;
    private int _changes;

    public Rsi(int period) => _period = period;

    public bool IsReady => _changes >= _period;

    public decimal Value
    {
        get
        {
            if (!IsReady) return 50;
            if (_avgLoss == 0) return _avgGain == 0 ? 50 : 100;
            var rs = _avgGain / _avgLoss;
            return 100 - 100 / (1 + rs);
        }
    }

    public decimal Update(decimal close)
    {
        if (_prev is not { } prev)
        {
            _prev = close;
            return Value;
        }

        var change = close - prev;
        _prev = close;
        var gain = change > 0 ? change : 0;
        var loss = change < 0 ? -change : 0;

        if (_changes < _period)
        {
            _changes++;
            _avgGain += gain;
            _avgLoss += loss;
            if (_changes == _period)
            {
                _avgGain /= _period;
                _avgLoss /= _period;
            }
        }
        else
        {
            _avgGain = (_avgGain * (_period - 1) + gain) / _period;
            _avgLoss = (_avgLoss * (_period - 1) + loss) / _period;
        }

        return Value;
    }
}

/// <summary>MACD (fast EMA − slow EMA) with signal line and histogram.</summary>
public sealed class Macd
{
    private readonly Ema _fast, _slow, _signal;

    public Macd(int fast = 12, int slow = 26, int signal = 9)
    {
        _fast = new Ema(fast);
        _slow = new Ema(slow);
        _signal = new Ema(signal);
    }

    public bool IsReady => _signal.IsReady;
    public decimal Line { get; private set; }
    public decimal Signal => _signal.Value;
    public decimal Histogram => Line - Signal;

    public void Update(decimal close)
    {
        _fast.Update(close);
        _slow.Update(close);
        if (!_slow.IsReady) return;
        Line = _fast.Value - _slow.Value;
        _signal.Update(Line);
    }
}

/// <summary>Wilder's Average True Range.</summary>
public sealed class Atr
{
    private readonly int _period;
    private decimal? _prevClose;
    private decimal _seedSum;
    private int _count;

    public Atr(int period) => _period = period;

    public bool IsReady => _count >= _period;
    public decimal Value { get; private set; }

    public decimal Update(decimal high, decimal low, decimal close)
    {
        var tr = high - low;
        if (_prevClose is { } pc)
            tr = Math.Max(tr, Math.Max(Math.Abs(high - pc), Math.Abs(low - pc)));
        _prevClose = close;

        if (_count < _period)
        {
            _count++;
            _seedSum += tr;
            Value = _seedSum / _count;
        }
        else
        {
            Value = (Value * (_period - 1) + tr) / _period;
        }
        return Value;
    }
}

/// <summary>Rolling max or min over the last <c>period</c> values using a monotonic deque (amortized O(1)).</summary>
public sealed class RollingExtreme
{
    private readonly int _period;
    private readonly bool _isMax;
    private readonly long[] _idx;
    private readonly decimal[] _val;
    private int _head, _count;
    private long _n;

    public RollingExtreme(int period, bool isMax)
    {
        _period = period;
        _isMax = isMax;
        _idx = new long[period + 1];
        _val = new decimal[period + 1];
    }

    public static RollingExtreme Max(int period) => new(period, true);
    public static RollingExtreme Min(int period) => new(period, false);

    public bool IsReady => _n >= _period;
    public decimal Value => _count == 0 ? 0 : _val[_head];

    public decimal Update(decimal value)
    {
        var cap = _val.Length;

        // Drop dominated values from the back.
        while (_count > 0)
        {
            var back = (_head + _count - 1) % cap;
            if (_isMax ? _val[back] <= value : _val[back] >= value) _count--;
            else break;
        }

        var pos = (_head + _count) % cap;
        _idx[pos] = _n;
        _val[pos] = value;
        _count++;

        // Drop the front if it left the window [n - period + 1, n].
        if (_idx[_head] <= _n - _period)
        {
            _head = (_head + 1) % cap;
            _count--;
        }

        _n++;
        return Value;
    }
}

/// <summary>Supertrend (ATR bands that flip between support and resistance).</summary>
public sealed class Supertrend
{
    private readonly Atr _atr;
    private readonly decimal _multiplier;
    private decimal _finalUpper, _finalLower, _prevClose;
    private bool _initialized;

    public Supertrend(int period = 10, decimal multiplier = 3m)
    {
        _atr = new Atr(period);
        _multiplier = multiplier;
    }

    public bool IsReady => _initialized;

    /// <summary>+1 uptrend, −1 downtrend.</summary>
    public int Direction { get; private set; } = 1;

    /// <summary>Trailing line: lower band in an uptrend, upper band in a downtrend.</summary>
    public decimal Line => Direction > 0 ? _finalLower : _finalUpper;

    public void Update(decimal high, decimal low, decimal close)
    {
        _atr.Update(high, low, close);
        if (!_atr.IsReady)
        {
            _prevClose = close;
            return;
        }

        var hl2 = (high + low) / 2;
        var basicUpper = hl2 + _multiplier * _atr.Value;
        var basicLower = hl2 - _multiplier * _atr.Value;

        if (!_initialized)
        {
            _finalUpper = basicUpper;
            _finalLower = basicLower;
            Direction = close >= hl2 ? 1 : -1;
            _initialized = true;
            _prevClose = close;
            return;
        }

        _finalUpper = basicUpper < _finalUpper || _prevClose > _finalUpper ? basicUpper : _finalUpper;
        _finalLower = basicLower > _finalLower || _prevClose < _finalLower ? basicLower : _finalLower;

        if (Direction < 0 && close > _finalUpper) Direction = 1;
        else if (Direction > 0 && close < _finalLower) Direction = -1;

        _prevClose = close;
    }
}

/// <summary>Bollinger Bands on a rolling window.</summary>
public sealed class Bollinger
{
    private readonly RollingStats _stats;
    private readonly decimal _k;

    public Bollinger(int period = 20, decimal k = 2m)
    {
        _stats = new RollingStats(period);
        _k = k;
    }

    public bool IsReady => _stats.IsReady;
    public decimal Middle => _stats.Mean;
    public decimal Upper => Middle + _k * _stats.StdDev;
    public decimal Lower => Middle - _k * _stats.StdDev;
    public decimal Bandwidth => Middle == 0 ? 0 : (Upper - Lower) / Middle;

    public void Update(decimal close) => _stats.Update(close);
}

/// <summary>Rolling volume-weighted average price using typical price (H+L+C)/3.</summary>
public sealed class RollingVwap
{
    private readonly RingBuffer<(decimal Pv, decimal V)> _window;
    private decimal _pv, _v;

    public RollingVwap(int period) => _window = new RingBuffer<(decimal, decimal)>(period);

    public bool IsReady => _window.IsFull && _v > 0;
    public decimal Value => _v == 0 ? 0 : _pv / _v;

    public decimal Update(decimal high, decimal low, decimal close, decimal volume)
    {
        var pv = (high + low + close) / 3 * volume;
        if (_window.Add((pv, volume), out var old))
        {
            _pv -= old.Pv;
            _v -= old.V;
        }
        _pv += pv;
        _v += volume;
        return Value;
    }
}

/// <summary>Rate of change over <c>period</c> values: close / close[period] − 1.</summary>
public sealed class RateOfChange
{
    private readonly RingBuffer<decimal> _window;

    public RateOfChange(int period) => _window = new RingBuffer<decimal>(period + 1);

    public bool IsReady => _window.IsFull;
    public decimal Value => !IsReady || _window[_window.Count - 1] == 0 ? 0 : _window[0] / _window[_window.Count - 1] - 1;

    public decimal Update(decimal close)
    {
        _window.Add(close);
        return Value;
    }
}
