namespace Commerce.Application.Interfaces;

using System.Threading;
using System.Threading.Tasks;
using Commerce.Domain.Entities;

public interface IIdempotencyService
{
    Task<IdempotencyKey?> GetAsync(string key, string scope, CancellationToken cancellationToken = default);
    Task CreateOrUpdateAsync(string key, string scope, int statusCode, string? responseBody, CancellationToken cancellationToken = default);
}
