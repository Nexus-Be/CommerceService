namespace Commerce.Infrastructure.Services.Catalog;

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;

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
}
