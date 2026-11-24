using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.DTOs;

namespace dotnetApp.Application.Interface;

/// <summary>
/// Source of global index / commodity quotes. Kept behind an interface so the free (unofficial)
/// Yahoo feed can be swapped for a paid API without touching the service or UI.
/// </summary>
public interface IGlobalMarketProvider
{
    string ProviderName { get; }

    /// <summary>
    /// Returns a quote for every symbol that could be fetched; symbols that fail are simply omitted.
    /// </summary>
    Task<IReadOnlyList<ProviderMarketQuote>> FetchQuotesAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default);
}
