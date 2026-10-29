namespace Commerce.Infrastructure.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;

public sealed class IdempotencyService : IIdempotencyService
{
    private readonly ICommerceDbContext _dbContext;

    public IdempotencyService(ICommerceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IdempotencyKey?> GetAsync(string key, string scope, CancellationToken cancellationToken = default)
    {
        return await _dbContext.IdempotencyKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key && x.Scope == scope && x.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);
    }

    public async Task CreateOrUpdateAsync(string key, string scope, int statusCode, string? responseBody, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.IdempotencyKeys
            .FirstOrDefaultAsync(x => x.Key == key && x.Scope == scope, cancellationToken);

        if (existing != null)
        {
            existing.ResponseStatusCode = statusCode;
            existing.ResponseBody = responseBody;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.ExpiresAt = DateTimeOffset.UtcNow.AddHours(24);
        }
        else
        {
            var entity = new IdempotencyKey
            {
                Id = Guid.NewGuid(),
                Key = key,
                Scope = scope,
                ResponseStatusCode = statusCode,
                ResponseBody = responseBody,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
            };
            _dbContext.IdempotencyKeys.Add(entity);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
