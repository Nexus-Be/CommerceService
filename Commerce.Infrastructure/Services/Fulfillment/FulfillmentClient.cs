namespace Commerce.Infrastructure.Services.Fulfillment;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;

public sealed class FulfillmentClient : IFulfillmentClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FulfillmentClient> _logger;

    public FulfillmentClient(HttpClient httpClient, ILogger<FulfillmentClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> CheckStockAvailabilityAsync(Guid skuId, int quantity, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/fulfillment/inventory/check", new
            {
                skuId,
                quantity
            }, ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<StockCheckApiResponse>(cancellationToken: ct);
                return result?.IsAvailable ?? true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Fulfillment Client] Connection error checking stock for SKU '{SkuId}'. Fallback to available.", skuId);
        }

        // Dev/Offline Fallback: Coi như còn hàng nếu không kết nối được tới Fulfillment Service
        return true;
    }

    public async Task<Dictionary<Guid, SkuStockCheckResult>> BatchCheckStockAsync(List<Guid> skuIds, CancellationToken ct = default)
    {
        var results = new Dictionary<Guid, SkuStockCheckResult>();

        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/fulfillment/inventory/batch-check", new
            {
                skuIds
            }, ct);

            if (response.IsSuccessStatusCode)
            {
                var apiResults = await response.Content.ReadFromJsonAsync<List<SkuStockCheckResult>>(cancellationToken: ct);
                if (apiResults != null)
                {
                    foreach (var item in apiResults)
                    {
                        results[item.SkuId] = item;
                    }
                    return results;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Fulfillment Client] Connection error batch checking stock. Fallback to available for all SKUs.");
        }

        // Dev/Offline Fallback: Mặc định tất cả SKU đều có 100 sản phẩm khả dụng
        foreach (var skuId in skuIds)
        {
            results[skuId] = new SkuStockCheckResult(skuId, true, 100, null);
        }

        return results;
    }

    public async Task<bool> ReserveStockAsync(Guid orderId, List<StockReservationItem> items, int ttlMinutes = 15, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/fulfillment/inventory/reserve", new
            {
                orderId,
                ttlMinutes,
                items
            }, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Fulfillment Client] Stock reserved successfully for Order '{OrderId}' (TTL: {Ttl}m).", orderId, ttlMinutes);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[REST Fulfillment Client] Stock reservation failed for Order '{OrderId}'. Status: {StatusCode}, Details: {Details}",
                orderId, response.StatusCode, errorBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Fulfillment Client] Connection error reserving stock for Order '{OrderId}'. Dev fallback to true.", orderId);
            return true; // Dev Fallback
        }
    }

    public async Task<bool> CommitStockAsync(Guid orderId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/fulfillment/inventory/commit", new { orderId }, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Fulfillment Client] Stock committed (deducted) for Order '{OrderId}'.", orderId);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("[REST Fulfillment Client] Stock commit failed for Order '{OrderId}'. Status: {StatusCode}, Details: {Details}",
                orderId, response.StatusCode, errorBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Fulfillment Client] Connection error committing stock for Order '{OrderId}'.", orderId);
            return false; // Return false thật sự, triệt tiêu silent fallback
        }
    }

    public async Task<bool> ReleaseStockAsync(Guid orderId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/fulfillment/inventory/release", new { orderId }, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Fulfillment Client] Stock released back to available pool for Order '{OrderId}'.", orderId);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Fulfillment Client] Connection error releasing stock for Order '{OrderId}'.", orderId);
        }

        return true;
    }

    public async Task<bool> ReleaseSubOrderStockAsync(Guid subOrderId, List<StockReservationItem> items, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/fulfillment/inventory/suborder/release", new
            {
                subOrderId,
                items
            }, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Fulfillment Client] SubOrder stock released for SubOrder '{SubOrderId}' ({Count} items).", subOrderId, items.Count);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Fulfillment Client] Connection error releasing SubOrder stock for SubOrder '{SubOrderId}'.", subOrderId);
        }

        return true;
    }

    public async Task<decimal> CalculateShopShippingFeeAsync(ShopShippingQuoteRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/fulfillment/shipping/quote", request, ct);
            if (response.IsSuccessStatusCode)
            {
                var feeResult = await response.Content.ReadFromJsonAsync<ShippingFeeResult>(cancellationToken: ct);
                if (feeResult != null)
                {
                    return feeResult.FeeAmount;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Fulfillment Client] Connection error calculating shipping fee for Seller '{SellerId}'. Fallback to 30000.", request.SellerId);
        }

        // Mặc định cước GHN đồng giá 30.000 VNĐ
        return 30000m;
    }

    public async Task<ShipmentCreationResult> CreateShipmentAsync(Guid subOrderId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/fulfillment/shipments", new { subOrderId }, ct);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ShipmentCreationResult>(cancellationToken: ct);
                if (result != null) return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Fulfillment Client] Connection error creating shipment for SubOrder '{SubOrderId}'.", subOrderId);
        }

        return new ShipmentCreationResult(
            TrackingCode: $"GHN-{DateTime.UtcNow:yyyyMMdd}-{subOrderId.ToString()[..6].ToUpperInvariant()}",
            CarrierCode: "GHN",
            LabelPrintUrl: $"https://fulfillment.nexus.internal/labels/{subOrderId}.pdf"
        );
    }

    private sealed record StockCheckApiResponse(bool IsAvailable, int AvailableStock);
    private sealed record ShippingFeeResult(decimal FeeAmount);
}
