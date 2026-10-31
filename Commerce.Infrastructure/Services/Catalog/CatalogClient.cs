namespace Commerce.Infrastructure.Services.Catalog;

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;

/// <summary>
/// REST client interacting with Catalog service for stock reservation and SKU checks.
/// Returns false or fallback gracefully if the target endpoint is unreachable or pending deployment.
/// </summary>
public sealed class CatalogClient : ICatalogClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CatalogClient> _logger;

    public CatalogClient(HttpClient httpClient, ILogger<CatalogClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> ReserveStockAsync(
        Guid orderId,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                orderId,
                productId,
                quantity
            };

            var response = await _httpClient.PostAsJsonAsync("api/v1/products/reserve", payload, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Catalog Client] Stock reservation succeeded for Order '{OrderId}', Product '{ProductId}'.", orderId, productId);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("[REST Catalog Client] Stock reservation failed for Order '{OrderId}'. Status: {StatusCode}, Details: {Details}",
                orderId, response.StatusCode, errorBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Catalog Client] Connection error while reserving stock for Order '{OrderId}'.", orderId);
            return false;
        }
    }

    public async Task<CatalogProductCheckResult?> GetSkuDetailsAsync(
        Guid skuId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"api/v1/catalog/skus/{skuId}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<CatalogProductCheckResult>(cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Catalog Client] Connection error getting SKU details for '{SkuId}'. Fallback to mock data.", skuId);
        }

        // Fallback for development / test when Catalog Service is not reachable
        return new CatalogProductCheckResult(
            SkuId: skuId,
            ProductId: Guid.NewGuid(),
            SellerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ProductName: $"Product SKU {skuId.ToString()[..8]}",
            UnitPrice: 100000m,
            IsPurchasable: true,
            StockQuantity: 100);
    }
}
