namespace Commerce.Application.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;

public record CatalogProductCheckResult(
    Guid ProductId,
    Guid SkuId,
    Guid SellerId,
    string ProductName,
    string? SkuCode,
    decimal UnitPrice,
    bool IsPurchasable,
    int AvailableStock,
    string? ErrorReason = null
);

public interface ICatalogServiceClient
{
    Task<CatalogProductCheckResult> CheckPurchasableAsync(Guid productId, Guid skuId, int quantity, CancellationToken cancellationToken = default);
}
