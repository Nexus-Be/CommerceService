namespace Commerce.Application.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;

public sealed record CatalogProductCheckResult(
    Guid SkuId,
    Guid ProductId,
    Guid SellerId,
    string ProductName,
    decimal UnitPrice,
    bool IsPurchasable,
    int StockQuantity = 100,
    string? ErrorReason = null);

public interface ICatalogClient
{
    Task<bool> ReserveStockAsync(
        Guid orderId,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken = default);

    Task<CatalogProductCheckResult?> GetSkuDetailsAsync(
        Guid skuId,
        CancellationToken cancellationToken = default);
}
