namespace Commerce.Infrastructure.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;

public sealed class MockCatalogServiceClient : ICatalogServiceClient
{
    public Task<CatalogProductCheckResult> CheckPurchasableAsync(Guid productId, Guid skuId, int quantity, CancellationToken cancellationToken = default)
    {
        var result = new CatalogProductCheckResult(
            ProductId: productId,
            SkuId: skuId,
            SellerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ProductName: "Sample Product",
            SkuCode: "SKU-SAMPLE-01",
            UnitPrice: 100000m,
            IsPurchasable: true,
            AvailableStock: 999
        );

        return Task.FromResult(result);
    }
}
