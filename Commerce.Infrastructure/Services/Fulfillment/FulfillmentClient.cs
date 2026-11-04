namespace Commerce.Infrastructure.Services.Fulfillment;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;
using Commerce.Domain.Exceptions;

public sealed class FulfillmentClient : IFulfillmentClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FulfillmentClient> _logger;
    private readonly bool _failOpen;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FulfillmentClient(
        HttpClient httpClient,
        ILogger<FulfillmentClient> logger,
        IConfiguration? configuration = null)
    {
        _httpClient = httpClient;
        _logger = logger;

        var failOpenSetting = configuration?["StockPolicy:FailOpen"]
            ?? Environment.GetEnvironmentVariable("STOCK_FAIL_OPEN");

        // Mặc định FailOpen = true trong môi trường Dev/Local nếu không cấu hình
        _failOpen = string.IsNullOrEmpty(failOpenSetting) || (bool.TryParse(failOpenSetting, out var val) && val);
    }

    public async Task<bool> CheckStockAvailabilityAsync(Guid skuId, int quantity, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"api/v1/inventories/{skuId}", ct);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<FulfillmentInventoryRawResponse>(JsonOptions, ct);
                if (result != null)
                {
                    return result.CheckAvailable(quantity);
                }
                return true;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("[REST Fulfillment Client] SKU '{SkuId}' not found in fulfillment warehouse.", skuId);
                return false;
            }

            _logger.LogWarning("[REST Fulfillment Client] Check stock returned status {StatusCode} for SKU '{SkuId}'.", response.StatusCode, skuId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Fulfillment Client] Connection error checking stock for SKU '{SkuId}'.", skuId);
            if (!_failOpen)
            {
                throw new CommerceDomainException("STOCK_CHECK_UNAVAILABLE", "Warehouse inventory service is temporarily unreachable.", 503);
            }
        }

        if (!_failOpen)
        {
            throw new CommerceDomainException("STOCK_CHECK_UNAVAILABLE", "Warehouse inventory service is temporarily unreachable.", 503);
        }

        return _failOpen;
    }

    public async Task<Dictionary<Guid, SkuStockCheckResult>> BatchCheckStockAsync(List<Guid> skuIds, CancellationToken ct = default)
    {
        var results = new Dictionary<Guid, SkuStockCheckResult>();
        if (skuIds == null || skuIds.Count == 0)
        {
            return results;
        }

        bool callSucceeded = false;
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/inventories/by-skus", skuIds, JsonOptions, ct);

            if (response.IsSuccessStatusCode)
            {
                callSucceeded = true;
                var rawJson = await response.Content.ReadAsStringAsync(ct);
                var parsedItems = ParseInventoryList(rawJson);
                foreach (var item in parsedItems)
                {
                    if (item.SkuId.HasValue)
                    {
                        var availStock = item.GetAvailableStock();
                        results[item.SkuId.Value] = new SkuStockCheckResult(
                            item.SkuId.Value,
                            item.CheckAvailable(1),
                            availStock,
                            availStock <= 0 ? (item.DisabledReason ?? "OUT_OF_STOCK") : null);
                    }
                }
                return results;
            }

            _logger.LogWarning("[REST Fulfillment Client] Batch stock lookup returned status {StatusCode}.", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Fulfillment Client] Connection error batch checking stock.");
            if (!_failOpen)
            {
                throw new CommerceDomainException("STOCK_CHECK_UNAVAILABLE", "Warehouse inventory service is temporarily unreachable.", 503);
            }
        }

        if (!_failOpen && !callSucceeded)
        {
            throw new CommerceDomainException("STOCK_CHECK_UNAVAILABLE", "Warehouse inventory service is temporarily unreachable.", 503);
        }

        // Dev/Offline Fallback: Chỉ dùng khi FailOpen = true
        if (_failOpen)
        {
            foreach (var skuId in skuIds)
            {
                if (!results.ContainsKey(skuId))
                {
                    results[skuId] = new SkuStockCheckResult(skuId, true, 100, null);
                }
            }
        }

        return results;
    }

    public async Task<bool> ReserveStockAsync(Guid orderId, List<StockReservationItem> items, int ttlMinutes = 15, CancellationToken ct = default)
    {
        var payload = new
        {
            referenceId = orderId.ToString(),
            holdMinutes = ttlMinutes,
            items = items.Select(x => new
            {
                skuId = x.SkuId,
                quantity = x.Quantity
            }).ToList()
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/internal/inventories/reservations", payload, JsonOptions, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Fulfillment Client] Stock reserved successfully for Order '{OrderId}' (Hold: {Hold}m).", orderId, ttlMinutes);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[REST Fulfillment Client] Stock reservation failed for Order '{OrderId}'. Status: {StatusCode}, Details: {Details}",
                orderId, response.StatusCode, errorBody);

            if (!_failOpen)
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Fulfillment Client] Connection error reserving stock for Order '{OrderId}'.", orderId);
            if (!_failOpen)
            {
                throw new CommerceDomainException("STOCK_RESERVATION_FAILED", "Warehouse inventory service is temporarily unreachable.", 503);
            }
        }

        return _failOpen;
    }

    public async Task<bool> CommitStockAsync(Guid orderId, CancellationToken ct = default)
    {
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var emptyContent = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"api/v1/internal/inventories/reservations/{orderId}/commit", emptyContent, ct);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[REST Fulfillment Client] Stock committed (deducted) for Order '{OrderId}'.", orderId);
                    return true;
                }

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("[REST Fulfillment Client] Stock commit returned status {StatusCode} for Order '{OrderId}'. Attempt {Attempt}/{MaxRetries}. Details: {Details}",
                    response.StatusCode, orderId, attempt, maxRetries, errorBody);

                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500 && response.StatusCode != HttpStatusCode.RequestTimeout)
                {
                    return false;
                }
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "[REST Fulfillment Client] Connection error committing stock for Order '{OrderId}'. Attempt {Attempt}/{MaxRetries}. Retrying in {Delay}ms...",
                    orderId, attempt, maxRetries, attempt * 200);
                await Task.Delay(attempt * 200, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[REST Fulfillment Client] Final connection error committing stock for Order '{OrderId}' after {MaxRetries} attempts.", orderId, maxRetries);
                return false;
            }
        }

        return false;
    }

    public async Task<bool> ReleaseStockAsync(Guid orderId, CancellationToken ct = default)
    {
        try
        {
            using var emptyContent = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"api/v1/internal/inventories/reservations/{orderId}/release?reason=OrderCancelled", emptyContent, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Fulfillment Client] Stock released back to available pool for Order '{OrderId}'.", orderId);
                return true;
            }

            _logger.LogWarning("[REST Fulfillment Client] Release stock returned status {StatusCode} for Order '{OrderId}'.", response.StatusCode, orderId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Fulfillment Client] Connection error releasing stock for Order '{OrderId}'.", orderId);
            if (!_failOpen)
            {
                return false;
            }
        }

        return true;
    }

    public async Task<BatchReleaseStockResult> BatchReleaseStockAsync(List<Guid> orderIds, CancellationToken ct = default)
    {
        if (orderIds == null || orderIds.Count == 0)
        {
            return new BatchReleaseStockResult(new List<Guid>(), new List<Guid>(), true);
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/internal/inventories/reservations/batch-release", new
            {
                orderIds
            }, JsonOptions, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Fulfillment Client] Batch stock released successfully for {Count} orders.", orderIds.Count);

                var rawResponse = await response.Content.ReadAsStringAsync(ct);
                if (!string.IsNullOrWhiteSpace(rawResponse))
                {
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<BatchReleaseApiResponse>(rawResponse, JsonOptions);
                        if (parsed != null && (parsed.SucceededOrderIds != null || parsed.FailedOrderIds != null))
                        {
                            return new BatchReleaseStockResult(
                                parsed.SucceededOrderIds ?? orderIds,
                                parsed.FailedOrderIds ?? new List<Guid>(),
                                (parsed.FailedOrderIds?.Count ?? 0) == 0
                            );
                        }
                    }
                    catch
                    {
                        // Best effort parsing
                    }
                }

                return new BatchReleaseStockResult(orderIds, new List<Guid>(), true);
            }

            _logger.LogWarning("[REST Fulfillment Client] Batch stock release returned status {StatusCode} for {Count} orders.",
                response.StatusCode, orderIds.Count);
            throw new CommerceDomainException("STOCK_RELEASE_UNAVAILABLE", "Warehouse inventory service is temporarily unreachable during batch release.", 503);
        }
        catch (CommerceDomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Fulfillment Client] Connection error batch releasing stock for {Count} orders.", orderIds.Count);
            throw new CommerceDomainException("STOCK_RELEASE_UNAVAILABLE", "Warehouse inventory service is temporarily unreachable during batch release.", 503);
        }
    }

    public async Task<bool> ReleaseSubOrderStockAsync(Guid orderId, List<StockReservationItem> items, CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                items = items.Select(x => new
                {
                    skuId = x.SkuId,
                    quantity = x.Quantity
                }).ToList(),
                reason = "SubOrderRejected"
            };
            var response = await _httpClient.PostAsJsonAsync($"api/v1/internal/inventories/reservations/{orderId}/release-items", payload, JsonOptions, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Fulfillment Client] SubOrder stock released for Master Order '{OrderId}'.", orderId);
                return true;
            }

            _logger.LogWarning("[REST Fulfillment Client] Release SubOrder stock returned status {StatusCode} for Master Order '{OrderId}'.", response.StatusCode, orderId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Fulfillment Client] Connection error releasing SubOrder stock for Master Order '{OrderId}'.", orderId);
            return false;
        }
    }

    public async Task<decimal> CalculateShopShippingFeeAsync(ShopShippingQuoteRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/internal/shipping/quote", request, ct);
            if (response.IsSuccessStatusCode)
            {
                var feeResult = await response.Content.ReadFromJsonAsync<ShippingFeeResult>(cancellationToken: ct);
                if (feeResult != null)
                {
                    return feeResult.FeeAmount;
                }
                throw new CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "Fulfillment service returned an empty shipping quote response.", 503);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("[REST Fulfillment Client] Shipping quote rejected with 400 for Seller '{SellerId}': {Error}", request.SellerId, errorBody);
                throw new CommerceDomainException("SHIPPING_QUOTE_FAILED", "Invalid shipping destination address or items payload for carrier calculation.", 400);
            }

            _logger.LogWarning("[REST Fulfillment Client] Shipping quote returned status {StatusCode} for Seller '{SellerId}'.", response.StatusCode, request.SellerId);
            throw new CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "Fulfillment service is temporarily unreachable for shipping fee calculation.", 503);
        }
        catch (CommerceDomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Fulfillment Client] Connection error calculating shipping fee for Seller '{SellerId}'.", request.SellerId);
            throw new CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "Fulfillment service is temporarily unreachable for shipping fee calculation.", 503);
        }
    }

    public async Task<ShipmentCreationResult> CreateShipmentAsync(Guid subOrderId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/v1/internal/shipments", new { subOrderId }, ct);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ShipmentCreationResult>(cancellationToken: ct);
                if (result != null) return result;

                throw new CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "Fulfillment service returned an empty shipment response.", 503);
            }

            _logger.LogWarning("[REST Fulfillment Client] Create shipment returned status {StatusCode} for SubOrder '{SubOrderId}'.", response.StatusCode, subOrderId);
            throw new CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "Unable to create shipment with fulfillment service.", 503);
        }
        catch (CommerceDomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Fulfillment Client] Connection error creating shipment for SubOrder '{SubOrderId}'.", subOrderId);
            throw new CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "Unable to create shipment with fulfillment service.", 503);
        }
    }

    private sealed record StockCheckApiResponse(bool IsAvailable, int AvailableStock);
    private sealed record ShippingFeeResult(decimal FeeAmount);

    private sealed class BatchReleaseApiResponse
    {
        public List<Guid>? SucceededOrderIds { get; set; }
        public List<Guid>? FailedOrderIds { get; set; }
    }

    private sealed class FulfillmentInventoryRawResponse
    {
        public Guid? SkuId { get; set; }
        public int? Quantity { get; set; }
        public int? ReservedQuantity { get; set; }
        public int? AvailableQuantity { get; set; }
        public int? AvailableStock { get; set; }
        public bool? IsAvailable { get; set; }
        public string? DisabledReason { get; set; }

        public int GetAvailableStock()
        {
            if (AvailableStock.HasValue) return AvailableStock.Value;
            if (AvailableQuantity.HasValue) return AvailableQuantity.Value;
            if (Quantity.HasValue) return Math.Max(0, Quantity.Value - (ReservedQuantity ?? 0));
            return 0;
        }

        public bool CheckAvailable(int requiredQuantity = 1)
        {
            if (IsAvailable.HasValue) return IsAvailable.Value && GetAvailableStock() >= requiredQuantity;
            return GetAvailableStock() >= requiredQuantity;
        }
    }

    private static List<FulfillmentInventoryRawResponse> ParseInventoryList(string json)
    {
        var list = new List<FulfillmentInventoryRawResponse>();
        if (string.IsNullOrWhiteSpace(json)) return list;

        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                {
                    var item = JsonSerializer.Deserialize<FulfillmentInventoryRawResponse>(el.GetRawText(), JsonOptions);
                    if (item != null) list.Add(item);
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("items", out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in itemsElem.EnumerateArray())
                    {
                        var item = JsonSerializer.Deserialize<FulfillmentInventoryRawResponse>(el.GetRawText(), JsonOptions);
                        if (item != null) list.Add(item);
                    }
                }
                else if (root.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in dataElem.EnumerateArray())
                    {
                        var item = JsonSerializer.Deserialize<FulfillmentInventoryRawResponse>(el.GetRawText(), JsonOptions);
                        if (item != null) list.Add(item);
                    }
                }
            }
        }
        catch
        {
            // Best-effort parsing
        }

        return list;
    }
}
