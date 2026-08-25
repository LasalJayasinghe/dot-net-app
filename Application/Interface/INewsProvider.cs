 using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dotnetApp.Application.DTOs;

namespace dotnetApp.Application.Interface;

public interface INewsProvider
{
    string ProviderName { get; }
    Task<IEnumerable<RawNewsArticleDto>> FetchArticlesAsync(CancellationToken cancellationToken = default);
}
