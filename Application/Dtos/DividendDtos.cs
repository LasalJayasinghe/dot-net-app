using System;
using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using dotnetApp.Domain.Entities;

namespace dotnetApp.Application.Dtos;

/// <summary>
/// Serialises a <see cref="DateTime"/> as a bare <c>yyyy-MM-dd</c> calendar date.
/// The CSE publishes record/payment dates as dates, not instants; emitting them without a
/// time or offset stops browsers re-interpreting them in the viewer's time zone and landing
/// on the wrong day.
/// </summary>
public sealed class IsoDateJsonConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTime.ParseExact(reader.GetString()!, Format, CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
}

/// <summary>
/// Wire shape for dividend endpoints. Deliberately narrower than the <see cref="Dividend"/>
/// entity: the <c>Remarks</c> longtext and internal bookkeeping columns are never selected,
/// so the database only reads and ships the columns the clients actually render.
/// </summary>
public sealed class DividendDto
{
    public int Id { get; init; }

    /// <summary>Ticker without the CSE class suffix, e.g. <c>CSLK</c>.</summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>Full CSE ticker as published, e.g. <c>CSLK.N0000</c>.</summary>
    public string FullSymbol { get; init; } = string.Empty;

    public string CompanyName { get; init; } = string.Empty;

    public string? FinancialYear { get; init; }

    public decimal VotingDivPerShare { get; init; }

    public decimal NonVotingDivPerShare { get; init; }

    [JsonConverter(typeof(IsoDateJsonConverter))]
    public DateTime DateOfAnnouncement { get; init; }

    [JsonConverter(typeof(IsoDateJsonConverter))]
    public DateTime RecordDate { get; init; }

    /// <summary>Null when CSE has not published a parseable payment date.</summary>
    [JsonConverter(typeof(IsoDateJsonConverter))]
    public DateTime? PaymentDate { get; init; }

    /// <summary>The raw CSE payment-date string, shown when <see cref="PaymentDate"/> is null.</summary>
    public string? PaymentDateText { get; init; }

    /// <summary>
    /// Column-level projection. Kept as an expression so EF Core translates it into the SELECT
    /// list rather than materialising whole entities.
    /// </summary>
    public static readonly Expression<Func<Dividend, DividendDto>> Projection = d => new DividendDto
    {
        Id = d.Id,
        Symbol = d.BaseSymbol,
        FullSymbol = d.Symbol,
        CompanyName = d.CompanyName,
        FinancialYear = d.FinancialYear,
        VotingDivPerShare = d.VotingDivPerShare,
        NonVotingDivPerShare = d.NonVotingDivPerShare,
        DateOfAnnouncement = d.DateOfAnnouncement,
        RecordDate = d.RecordDate,
        PaymentDate = d.PaymentDate,
        PaymentDateText = d.PaymentDateRaw,
    };
}
