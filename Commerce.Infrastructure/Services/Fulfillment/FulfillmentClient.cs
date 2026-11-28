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

        // Fail closed unless it is switched on explicitly (isolated QA only): an order must never be accepted without a stock reservation.
        _failOpen = bool.TryParse(failOpenSetting, out var val) && val;
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

    public async Task<bool> ReserveStockAsync(Guid orderId, List<StockReservationItem> items, int ttlMinutes = 5, CancellationToken ct = default)
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
            _logger.LogError("[REST Fulfillment Client] Stock reservation failed for Order '{OrderId}'. Status: {StatusCode}, Details: {Details}",
                orderId, response.StatusCode, errorBody);

            // A 4xx answer is a business refusal (not enough stock, unknown SKU): always reject, never fail open.
            if ((int)response.StatusCode is >= 400 and < 500)
            {
                return false;
            }

            // Anything else (5xx) means the warehouse could not do its job; only an explicit QA switch lets the order through.
            if (_failOpen)
            {
                return true;
            }

            throw new CommerceDomainException("FULFILLMENT_UNAVAILABLE", "Warehouse inventory service could not reserve stock right now.", 503);
        }
        catch (CommerceDomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Fulfillment Client] Connection error reserving stock for Order '{OrderId}'.", orderId);
            if (_failOpen)
            {
                return true;
            }

            throw new CommerceDomainException("FULFILLMENT_UNAVAILABLE", "Warehouse inventory service is temporarily unreachable.", 503);
        }
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

    public Task<bool> ReleaseSubOrderStockAsync(Guid orderId, List<StockReservationItem> items, CancellationToken ct = default)
    {
        return ReleaseSubOrderStockAsync(orderId, items, "SubOrderRejected", ct);
    }

    public async Task<bool> ReleaseSubOrderStockAsync(Guid orderId, List<StockReservationItem> items, string reason, CancellationToken ct = default)
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
                reason = string.IsNullOrWhiteSpace(reason) ? "SubOrderRejected" : reason
            };
            var response = await _httpClient.PostAsJsonAsync($"api/v1/internal/inventories/reservations/{orderId}/release-items", payload, JsonOptions, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Fulfillment Client] SubOrder stock released with reason '{Reason}' for Master Order '{OrderId}'.", reason, orderId);
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

    /// <summary>The error code in Fulfillment's error body ("errorCode"), or null when the body is empty or not an error object.</summary>
    private static string? ReadErrorCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("errorCode", out var code)
                && code.ValueKind == System.Text.Json.JsonValueKind.String
                    ? code.GetString()
                    : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static bool IsWarehouseMissing(string? body) => string.Equals(ReadErrorCode(body), "WAREHOUSE_NOT_FOUND", StringComparison.Ordinal);

    /// <summary>The seller has a warehouse but it has no origin district, so no fee can be quoted and no pickup arranged.</summary>
    private sealed record LocationAnswer(string? ProvinceName, string? DistrictName, string? WardName);

    private static bool IsWarehouseIncomplete(string? body) =>
        ReadErrorCode(body) is "INVALID_FROM_DISTRICT" or "WAREHOUSE_ADDRESS_INCOMPLETE";

    public async Task<LocationCheckResult> ValidateLocationAsync(int provinceId, int districtId, string wardCode, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("api/v1/internal/shipping/locations/validate", new { provinceId, districtId, wardCode }, JsonOptions, ct);
            if (response.IsSuccessStatusCode)
            {
                // A 200 that does not name the province, district and ward breaks the contract: the order would keep the typed text, which is
                // exactly what the confirmation is there to replace. It is treated as "cannot be confirmed" (503), never as confirmed.
                LocationAnswer? answer = null;
                try
                {
                    answer = await response.Content.ReadFromJsonAsync<LocationAnswer>(JsonOptions, ct);
                }
                catch (JsonException)
                {
                    // handled below with the other unusable answers
                }

                if (answer is null || string.IsNullOrWhiteSpace(answer.ProvinceName) || string.IsNullOrWhiteSpace(answer.DistrictName) || string.IsNullOrWhiteSpace(answer.WardName))
                {
                    _logger.LogWarning("[REST Fulfillment Client] Location check answered 200 without the province, district and ward names; the address is not used.");
                    return LocationCheckResult.SourceUnavailable();
                }

                return LocationCheckResult.Confirmed(answer.ProvinceName.Trim(), answer.DistrictName.Trim(), answer.WardName.Trim());
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.BadRequest && string.Equals(ReadErrorCode(body), "VAL_INVALID_LOCATION", StringComparison.Ordinal))
            {
                return LocationCheckResult.Rejected();
            }

            _logger.LogWarning("[REST Fulfillment Client] Location check answered {StatusCode}; the address is not used until the GHN lists can be read.", (int)response.StatusCode);
            return LocationCheckResult.SourceUnavailable();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[REST Fulfillment Client] Location check could not be completed.");
            return LocationCheckResult.SourceUnavailable();
        }
    }

    public async Task<decimal> CalculateShopShippingFeeAsync(ShopShippingQuoteRequest request, CancellationToken ct = default)
    {
        try
        {
            // Fulfillment's contract names the fields differently from Commerce's own request (ToDistrictId, ToWardCode, WeightInGrams),
            // and picks the origin district from the seller's warehouse when FromDistrictId is 0.
            var items = request.Items ?? new List<ShippingQuoteItem>();
            var payload = new
            {
                sellerId = request.SellerId,
                carrierCode = request.CarrierCode,
                fromDistrictId = 0,
                toDistrictId = request.GhnDistrictId,
                toWardCode = request.GhnWardCode,
                weightInGrams = Math.Max(1, items.Sum(i => Math.Max(1, i.WeightGrams) * Math.Max(1, i.Quantity))),
                items = items.Select(i => new { skuId = i.SkuId, quantity = Math.Max(1, i.Quantity), weightInGrams = Math.Max(1, i.WeightGrams) }).ToList()
            };
            var response = await _httpClient.PostAsJsonAsync("api/v1/internal/shipping/quote", payload, ct);
            if (response.IsSuccessStatusCode)
            {
                var feeResult = await response.Content.ReadFromJsonAsync<ShippingFeeResult>(cancellationToken: ct);
                if (feeResult != null)
                {
                    // Fulfillment answers { fee, ... }; older builds answered { feeAmount }.
                    return feeResult.Fee > 0 ? feeResult.Fee : feeResult.FeeAmount;
                }
                throw new CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "Fulfillment service returned an empty shipping quote response.", 503);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var missingBody = await response.Content.ReadAsStringAsync(ct);
                if (IsWarehouseMissing(missingBody))
                {
                    _logger.LogWarning("[REST Fulfillment Client] Seller '{SellerId}' has no active warehouse; no shipping fee can be quoted.", request.SellerId);
                    throw new CommerceDomainException("SELLER_WAREHOUSE_MISSING", "This shop is not ready to ship yet because it has no active warehouse.", 422);
                }
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                if (IsWarehouseIncomplete(errorBody))
                {
                    _logger.LogWarning("[REST Fulfillment Client] The warehouse of Seller '{SellerId}' has no origin district; no shipping fee can be quoted.", request.SellerId);
                    throw new CommerceDomainException("SELLER_WAREHOUSE_INCOMPLETE", "This shop's warehouse has no pickup location yet, so shipping cannot be arranged.", 422);
                }

                _logger.LogWarning("[REST Fulfillment Client] Shipping quote rejected with 400 for Seller '{SellerId}': {Error}", request.SellerId, errorBody);
                throw new CommerceDomainException("SHIPPING_QUOTE_FAILED", "Invalid shipping destination address or items payload for carrier calculation.", 400);
            }

            // Fulfillment is up but the shipping carrier cannot be used: say so with the carrier's own code (not configured, unavailable,
            // timed out) instead of the generic "Fulfillment is unreachable", and never blame the buyer's address for it.
            if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
            {
                var carrierBody = await response.Content.ReadAsStringAsync(ct);
                var carrierCode = ReadErrorCode(carrierBody);
                var carrierMessage = carrierCode switch
                {
                    "SHIPPING_CARRIER_NOT_CONFIGURED" => "The shipping carrier is not configured, so the shipping fee cannot be calculated.",
                    "SHIPPING_CARRIER_UNAVAILABLE" => "The shipping carrier is temporarily unavailable, so the shipping fee cannot be calculated.",
                    "SHIPPING_CARRIER_TIMEOUT" => "The shipping carrier did not answer in time, so the shipping fee cannot be calculated.",
                    _ => null
                };
                if (carrierMessage is not null)
                {
                    _logger.LogWarning("[REST Fulfillment Client] Shipping quote for Seller '{SellerId}' failed because of the carrier: {Code}.", request.SellerId, carrierCode);
                    throw new CommerceDomainException(carrierCode!, carrierMessage, 503);
                }
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

    public async Task<ShipmentCreationResult> CreateShipmentAsync(ShipmentCreationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var subOrderId = request.SubOrderId;

        try
        {
            // Fulfillment's contract: it needs the order, the seller and the recipient, and answers { shipmentId, trackingNumber, labelUrl, status }.
            var payload = new
            {
                orderId = request.OrderId,
                subOrderId = request.SubOrderId,
                sellerId = request.SellerId,
                carrierCode = request.CarrierCode,
                recipientName = request.RecipientName,
                recipientPhone = request.RecipientPhone,
                shippingAddress = request.ShippingAddress,
                toProvinceName = request.ToProvinceName,
                toDistrictId = request.ToDistrictId,
                toWardCode = request.ToWardCode,
                weightInGrams = request.WeightInGrams,
                pickShiftId = request.PickShiftId,
                codAmount = request.CodAmount,
                items = request.Items.Select(i => new
                {
                    skuId = i.SkuId,
                    quantity = i.Quantity,
                    name = i.Name,
                    price = i.Price,
                    weightInGrams = i.WeightInGrams
                }).ToList()
            };

            var response = await _httpClient.PostAsJsonAsync("api/v1/internal/shipments", payload, ct);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<CreateShipmentApiResponse>(cancellationToken: ct);
                var tracking = body?.TrackingNumber ?? body?.TrackingCode;
                if (body is null || string.IsNullOrWhiteSpace(tracking))
                    throw new CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "Fulfillment service returned an empty shipment response.", 503);

                return new ShipmentCreationResult(
                    TrackingCode: tracking,
                    CarrierCode: string.IsNullOrWhiteSpace(body.CarrierCode) ? "GHN" : body.CarrierCode,
                    LabelPrintUrl: body.LabelUrl ?? body.LabelPrintUrl ?? string.Empty);
            }

            // A refusal (4xx) means this request is wrong and retrying will not help; only 5xx and timeouts are "unavailable".
            if ((int)response.StatusCode is >= 400 and < 500)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "[REST Fulfillment Client] Create shipment rejected with {StatusCode} for SubOrder '{SubOrderId}': {Error}",
                    (int)response.StatusCode, subOrderId, errorBody);
                if (IsWarehouseMissing(errorBody))
                {
                    throw new CommerceDomainException("SELLER_WAREHOUSE_MISSING", "The seller has no active warehouse yet, so no shipment can be created. Set up the warehouse, then retry.", 422);
                }
                if (IsWarehouseIncomplete(errorBody))
                {
                    throw new CommerceDomainException("SELLER_WAREHOUSE_INCOMPLETE", "The seller's warehouse has no pickup location yet, so no shipment can be created. Complete the warehouse address, then retry.", 422);
                }

                throw new CommerceDomainException("SHIPMENT_CREATION_REJECTED", "The carrier shipment request was rejected by the fulfillment service.", 422);
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

    // The service-to-service route (shipment found by sub-order id). The public /api/v1/shipments/{id}/cancel needs a signed-in owner and answered
    // 403 to Commerce, so the carrier order was never cancelled.
    public async Task<bool> CancelShipmentAsync(Guid subOrderId, string reason = "SubOrderCancelled", CancellationToken ct = default)
    {
        try
        {
            var payload = new { reason };
            var response = await _httpClient.PostAsJsonAsync($"api/v1/internal/shipments/order/{subOrderId}/cancel", payload, JsonOptions, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Fulfillment Client] Carrier shipment cancelled successfully for SubOrder '{SubOrderId}'.", subOrderId);
                return true;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("[REST Fulfillment Client] No active carrier shipment found to cancel for SubOrder '{SubOrderId}'.", subOrderId);
                return true;
            }

            _logger.LogWarning("[REST Fulfillment Client] Cancel shipment returned status {StatusCode} for SubOrder '{SubOrderId}'.", response.StatusCode, subOrderId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Fulfillment Client] Connection error cancelling carrier shipment for SubOrder '{SubOrderId}'.", subOrderId);
            return false;
        }
    }

    private sealed record StockCheckApiResponse(bool IsAvailable, int AvailableStock);
    private sealed record ShippingFeeResult(decimal Fee = 0m, decimal FeeAmount = 0m);

    /// <summary>Fulfillment answers TrackingNumber/LabelUrl; the older names are still read so either build works.</summary>
    private sealed record CreateShipmentApiResponse(
        string? TrackingNumber = null, string? TrackingCode = null, string? CarrierCode = null, string? LabelUrl = null, string? LabelPrintUrl = null);

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
