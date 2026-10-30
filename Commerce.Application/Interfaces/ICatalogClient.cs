namespace Commerce.Application.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface ICatalogClient
{
    Task<bool> ReserveStockAsync(
        Guid orderId,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken = default);
}
