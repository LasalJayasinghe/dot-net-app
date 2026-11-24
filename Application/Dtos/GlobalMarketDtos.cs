using System;
using System.Collections.Generic;

namespace dotnetApp.Application.DTOs;

/// <summary>A raw quote as returned by an <c>IGlobalMarketProvider</c> (before grouping / mood).</summary>
public class ProviderMarketQuote
{
    public string Symbol { get; set; } = "";
    public string Currency { get; set; } = "";
    public decimal Price { get; set; }
    public decimal PreviousClose { get; set; }

    /// <summary>The provider's own % change, when it reports one; otherwise computed from <see cref="PreviousClose"/>.</summary>
    public decimal? ChangePercent { get; set; }

    /// <summary>True while the instrument's regular session is trading.</summary>
    public bool IsOpen { get; set; }

    /// <summary>UTC time of the last trade the price reflects.</summary>
    public DateTime? MarketTime { get; set; }
}

public class GlobalMarketQuoteDto
{
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public string Currency { get; set; } = "";
    public decimal Price { get; set; }
    public decimal PreviousClose { get; set; }
    public decimal Change { get; set; }
    public decimal ChangePercent { get; set; }
    public bool IsOpen { get; set; }
    public DateTime? MarketTime { get; set; }
}

public class GlobalMarketGroupDto
{
    /// <summary>"us", "asia", "commodities" or "risk".</summary>
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public List<GlobalMarketQuoteDto> Quotes { get; set; } = new();
}

/// <summary>A one-line read of global risk appetite, meant for a glance before CSE opens.</summary>
public class GlobalMarketMoodDto
{
    /// <summary>"Risk-on", "Mixed", "Risk-off" or "Unknown".</summary>
    public string Label { get; set; } = "Unknown";

    /// <summary>"positive", "negative" or "neutral" — drives the colour in the UI.</summary>
    public string Tone { get; set; } = "neutral";

    public string Summary { get; set; } = "";
    public decimal? AsiaAverageChangePercent { get; set; }
    public decimal? UsFuturesChangePercent { get; set; }
}

public class GlobalMarketOverviewDto
{
    public List<GlobalMarketGroupDto> Groups { get; set; } = new();
    public GlobalMarketMoodDto Mood { get; set; } = new();

    /// <summary>UTC time the data was fetched; null when nothing has been fetched yet.</summary>
    public DateTime? UpdatedAt { get; set; }
}
