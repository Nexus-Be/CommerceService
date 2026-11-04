namespace Commerce.Application.Interfaces;

using System;
using System.Collections.Generic;
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

public sealed record CatalogVariantSwitchResult(
    bool IsValid,
    Guid ProductId,
    Guid SellerId,
    Guid NewSkuId,
    string NewSkuName,
    decimal NewUnitPrice,
    bool IsPurchasable,
    int WeightGrams = 200);

public interface ICatalogClient
{
    Task<CatalogProductCheckResult?> GetSkuDetailsAsync(
        Guid skuId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, CatalogProductCheckResult>> GetSkuBatchDetailsAsync(
        IEnumerable<Guid> skuIds,
        CancellationToken cancellationToken = default);

    Task<CatalogVariantSwitchResult> ValidateVariantSwitchAsync(
        Guid currentSkuId,
        Guid newSkuId,
        CancellationToken cancellationToken = default);
}
