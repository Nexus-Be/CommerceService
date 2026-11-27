namespace Commerce.Infrastructure.Services.Wallet;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;

public sealed class WalletClient : IWalletClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WalletClient> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WalletClient(
        HttpClient httpClient,
        ILogger<WalletClient> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<WalletAuctionHoldStatus?> GetAuctionDepositHoldAsync(Guid depositId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"api/v1/wallets/holds/by-reference/AUCTION_DEPOSIT/{depositId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WalletAuctionHoldStatus>(JsonOptions, cancellationToken);
    }

    /// <summary>
    /// Hợp đồng chuẩn bàn giao từ WalletService (2026-11-10):
    /// POST /api/v1/wallets/orders/{orderId}/payment
    /// Header: x-user-id: <buyer UUID>
    /// Body: { "amount": <positive decimal> }
    /// </summary>
    public async Task<WalletPaymentResult> PayOrderAsync(
        Guid orderId,
        Guid buyerId,
        decimal amount,
        string? bearerToken = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var effectiveBuyerId = ResolveEffectiveUserId(buyerId);
            var requestUri = $"api/v1/wallets/orders/{orderId}/payment";

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(new { amount })
            };

            if (effectiveBuyerId != Guid.Empty)
            {
                request.Headers.TryAddWithoutValidation("x-user-id", effectiveBuyerId.ToString());
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var tx = await response.Content.ReadFromJsonAsync<WalletTransactionDto>(JsonOptions, cancellationToken);
                _logger.LogInformation("[REST Wallet Client] Order '{OrderId}' payment succeeded. TxID: '{TxId}'.", orderId, tx?.Id);
                return new WalletPaymentResult(
                    IsSuccess: true,
                    TransactionId: tx?.Id,
                    BalanceAfter: tx?.BalanceAfter);
            }

            var statusCode = (int)response.StatusCode;
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("[REST Wallet Client] Order '{OrderId}' payment failed with status {StatusCode}: {ErrorBody}", orderId, statusCode, errorBody);

            var (errorCode, errorMessage) = ParseProblemDetails(errorBody, statusCode);
            var isTransient = statusCode >= 500;

            return new WalletPaymentResult(
                IsSuccess: false,
                TransactionId: null,
                ErrorCode: errorCode,
                ErrorMessage: errorMessage,
                IsTransientFailure: isTransient);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Wallet Client] Connection error or timeout while paying for Order '{OrderId}'. Marked as transient.", orderId);
            return new WalletPaymentResult(
                IsSuccess: false,
                TransactionId: null,
                ErrorCode: "CONNECTION_ERROR",
                ErrorMessage: ex.Message,
                IsTransientFailure: true);
        }
    }

    public Task<WalletPaymentResult> PayOrderAsync(
        Guid orderId,
        decimal amount,
        string? bearerToken = null,
        CancellationToken cancellationToken = default)
    {
        var buyerId = ResolveEffectiveUserId(Guid.Empty);
        return PayOrderAsync(orderId, buyerId, amount, bearerToken, cancellationToken);
    }

    /// <summary>
    /// Tra cứu số dư ví qua internal endpoint (an toàn cả với worker nền lẫn HTTP request):
    /// GET /api/v1/internal/wallets/{userId}
    /// Header: X-Internal-Service-Key (tự động gắn từ HttpClient default headers)
    /// </summary>
    public async Task<decimal> GetWalletBalanceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var effectiveUserId = ResolveEffectiveUserId(userId);
            if (effectiveUserId == Guid.Empty)
            {
                return 0m;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/internal/wallets/{effectiveUserId}");
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<WalletDetailsResponse>(JsonOptions, cancellationToken: cancellationToken);
                return result?.Balance ?? 0m;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Wallet Client] Connection error getting wallet details for User '{UserId}'. Fallback to 0.", userId);
        }

        return 0m;
    }

    /// <summary>
    /// Refunds a sub-order to the buyer's wallet (RefundCredit).
    /// POST /api/v1/wallets/sub-orders/{subOrderId}/refund
    /// S2S route: no identity header. The body carries the parent order id, which the wallet requires (it checks the payment and takes the
    /// money out of the parent escrow). The wallet keeps one refund per sub-order id; a repeated call answers the existing transaction.
    /// </summary>
    public async Task<WalletRefundResult> RefundSubOrderPaymentAsync(
        Guid subOrderId,
        Guid parentOrderId,
        string userId,
        decimal amount,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestUri = $"api/v1/wallets/sub-orders/{subOrderId}/refund";
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(new
                {
                    userId,
                    amount,
                    reason,
                    parentOrderId
                })
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var (transactionId, replayed) = ParseRefundTransaction(body);
                _logger.LogInformation("[REST Wallet Client] SubOrder '{SubOrderId}' refund succeeded for User '{UserId}', Amount: {Amount}, Transaction: {TransactionId}, Replayed: {Replayed}.", subOrderId, userId, amount, transactionId, replayed);
                return WalletRefundResult.Success(transactionId, replayed);
            }

            var statusCode = (int)response.StatusCode;
            var (errorCode, errorMessage) = ParseProblemDetails(body, statusCode);

            if (statusCode == 503 || errorCode == "FEATURE_DISABLED")
            {
                _logger.LogError("[REST Wallet Client] SubOrder '{SubOrderId}' refund failed with 503 FEATURE_DISABLED. The wallet release flag 'OrderRefundEnabled' is off, so the refund stays pending: {ErrorBody}", subOrderId, body);
            }
            else if (statusCode == 409)
            {
                _logger.LogError("[REST Wallet Client] SubOrder '{SubOrderId}' refund returned 409 ({ErrorCode}): {ErrorMessage}", subOrderId, errorCode, errorMessage);
            }
            else
            {
                _logger.LogWarning("[REST Wallet Client] SubOrder '{SubOrderId}' refund failed with status {StatusCode} ({ErrorCode}): {ErrorMessage}", subOrderId, statusCode, errorCode, errorMessage);
            }
            return WalletRefundResult.Failure(errorCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Wallet Client] Error during refund for SubOrder '{SubOrderId}'.", subOrderId);
            return WalletRefundResult.Failure("WALLET_UNREACHABLE");
        }
    }

    /// <summary>Reads the refund transaction id and the replay marker from the wallet's answer (plain or wrapped in "data").</summary>
    public async Task<RefundFundPayResponse?> PayRefundFundCaseAsync(RefundFundPayRequest request, CancellationToken ct = default)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"api/v1/internal/wallets/refund-fund/sub-orders/{request.SubOrderId}/payout")
            {
                Content = JsonContent.Create(new
                {
                    parentOrderId = request.ParentOrderId,
                    buyerId = request.BuyerId.ToString(),
                    amount = request.Amount,
                    firstApproverId = request.FirstApproverId.ToString(),
                    secondApproverId = request.SecondApproverId?.ToString()
                })
            };
            using var response = await _httpClient.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    root = data;
                }

                string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                decimal Number(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : 0m;
                var replayed = root.TryGetProperty("replayed", out var rep) && rep.ValueKind == JsonValueKind.True;
                return new RefundFundPayResponse(Text("outcome") ?? "NeedsFinance", Text("reason"), Number("amount"), Number("sellerRecovered"), Number("sellerShortfall"), replayed, Text("buyerTransactionId"));
            }

            // 5xx, 408 and 429 (the fund being switched off included) are not decisions: the caller repeats later.
            if (status >= 500 || status == 408 || status == 429)
            {
                _logger.LogWarning("[REST Wallet Client] Refund fund payout of sub-order '{SubOrderId}' is not ready ({Status}); it will be repeated.", request.SubOrderId, status);
                return null;
            }

            var (errorCode, _) = ParseProblemDetails(body, status);
            _logger.LogWarning("[REST Wallet Client] Refund fund payout of sub-order '{SubOrderId}' was refused: {ErrorCode}.", request.SubOrderId, errorCode);
            return new RefundFundPayResponse("Rejected", string.IsNullOrWhiteSpace(errorCode) ? $"HTTP_{status}" : errorCode, request.Amount, 0m, 0m, false, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[REST Wallet Client] Could not reach the Wallet for the refund fund payout of sub-order '{SubOrderId}'.", request.SubOrderId);
            return null;
        }
    }

    private static (Guid? TransactionId, bool Replayed) ParseRefundTransaction(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, false);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object) root = data;
            if (root.ValueKind != JsonValueKind.Object) return (null, false);
            Guid? id = root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String && Guid.TryParse(idElement.GetString(), out var parsed) ? parsed : null;
            var replayed = root.TryGetProperty("replayed", out var flag) && flag.ValueKind == JsonValueKind.True;
            return (id, replayed);
        }
        catch (JsonException)
        {
            return (null, false);
        }
    }

    /// <summary>
    /// Tự động hoàn tiền Master Order vào ví Buyer qua WalletService (RefundCredit).
    /// POST /api/v1/wallets/orders/{orderId}/refund
    /// S2S Route: Không yêu cầu identity header; neo idempotency trên orderId.
    /// </summary>
    public async Task<bool> RefundOrderPaymentAsync(
        Guid orderId,
        string userId,
        decimal amount,
        string reason,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestUri = $"api/v1/wallets/orders/{orderId}/refund";
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(new
                {
                    userId,
                    amount,
                    reason
                })
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Wallet Client] Order '{OrderId}' refund succeeded for User '{UserId}', Amount: {Amount}.", orderId, userId, amount);
                return true;
            }

            var statusCode = (int)response.StatusCode;
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var (errorCode, errorMessage) = ParseProblemDetails(errorBody, statusCode);

            if (statusCode == 503 || errorCode == "FEATURE_DISABLED")
            {
                _logger.LogError("[REST Wallet Client] Order '{OrderId}' refund failed with 503 FEATURE_DISABLED. Wallet feature flag 'LegacyCommerceCreditsEnabled' may be disabled: {ErrorBody}", orderId, errorBody);
            }
            else if (statusCode == 409)
            {
                _logger.LogError("[REST Wallet Client] Order '{OrderId}' refund returned 409 ({ErrorCode}): {ErrorMessage}", orderId, errorCode, errorMessage);
            }
            else
            {
                _logger.LogWarning("[REST Wallet Client] Order '{OrderId}' refund failed with status {StatusCode} ({ErrorCode}): {ErrorMessage}", orderId, statusCode, errorCode, errorMessage);
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Wallet Client] Error during refund for Order '{OrderId}'.", orderId);
            return false;
        }
    }

    /// <summary>
    /// Giải ngân tiền từ Escrow sang ví của Seller khi Buyer xác nhận đã nhận hàng (EscrowRelease).
    /// POST /api/v1/wallets/sub-orders/{subOrderId}/settle
    /// S2S Route: Không yêu cầu identity header; neo idempotency trên subOrderId.
    /// </summary>
    public async Task<bool> SettleSellerSubOrderAsync(
        Guid subOrderId,
        Guid sellerId,
        decimal amount,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestUri = $"api/v1/wallets/sub-orders/{subOrderId}/settle";
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(new
                {
                    sellerId,
                    amount
                })
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Wallet Client] SubOrder '{SubOrderId}' settlement succeeded for Seller '{SellerId}', Amount: {Amount}.", subOrderId, sellerId, amount);
                return true;
            }

            var statusCode = (int)response.StatusCode;
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var (errorCode, errorMessage) = ParseProblemDetails(errorBody, statusCode);

            if (statusCode == 503 || errorCode == "FEATURE_DISABLED")
            {
                _logger.LogError("[REST Wallet Client] SubOrder '{SubOrderId}' settlement failed with 503 FEATURE_DISABLED. Wallet feature flag 'LegacyCommerceCreditsEnabled' may be disabled: {ErrorBody}", subOrderId, errorBody);
                return false;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                if (errorCode == "ORDER_ESCROW_INSUFFICIENT")
                {
                    _logger.LogError("[REST Wallet Client] SubOrder '{SubOrderId}' settlement returned 409 ORDER_ESCROW_INSUFFICIENT. Flagged for financial audit: {ErrorBody}", subOrderId, errorBody);
                }
                else
                {
                    _logger.LogWarning("[REST Wallet Client] SubOrder '{SubOrderId}' settlement returned 409 {ErrorCode}: {ErrorBody}", subOrderId, errorCode, errorBody);
                }
                return false;
            }

            _logger.LogWarning("[REST Wallet Client] SubOrder '{SubOrderId}' settlement failed with status {StatusCode} ({ErrorCode}): {ErrorMessage}", subOrderId, statusCode, errorCode, errorMessage);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Wallet Client] Error during settlement for SubOrder '{SubOrderId}'.", subOrderId);
            return false;
        }
    }

    private Guid ResolveEffectiveUserId(Guid explicitUserId)
    {
        if (explicitUserId != Guid.Empty)
        {
            return explicitUserId;
        }

        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext != null)
        {
            var claimVal = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User.FindFirst("sub")?.Value;

            if (Guid.TryParse(claimVal, out var claimId))
            {
                return claimId;
            }

            if (httpContext.Request.Headers.TryGetValue("x-user-id", out var hVal) &&
                Guid.TryParse(hVal.ToString(), out var headerId))
            {
                return headerId;
            }
        }

        return Guid.Empty;
    }

    public Task<WalletCallOutcome> SettleOrderPayoutAsync(WalletSettlementRequest request, CancellationToken cancellationToken = default) =>
        PostInternalAsync(
            "api/v1/internal/wallets/order-settlements",
            new
            {
                businessType = request.BusinessType,
                parentOrderId = request.ParentOrderId,
                subOrderId = request.SubOrderId,
                sellerId = request.SellerId,
                sellerReceiveAmount = request.SellerReceiveAmount,
                platformFeeAmount = request.PlatformFeeAmount,
                shippingAmount = request.ShippingAmount,
                subsidyAmount = request.SubsidyAmount,
                orderEscrowAmount = request.OrderEscrowAmount,
                depositEscrowAmount = request.DepositEscrowAmount,
                payloadVersion = request.PayloadVersion
            },
            $"payout of {request.BusinessType} '{request.SubOrderId}'",
            cancellationToken);

    public Task<WalletCallOutcome> HoldPlatformSubsidyAsync(Guid orderId, Guid subOrderId, decimal amount, CancellationToken cancellationToken = default) =>
        PostInternalAsync(
            $"api/v1/internal/wallets/sub-orders/{subOrderId}/subsidy-hold",
            new { orderId, amount },
            $"subsidy hold of sub-order '{subOrderId}'",
            cancellationToken);

    public Task<WalletCallOutcome> ReleasePlatformSubsidyAsync(Guid subOrderId, string reason, CancellationToken cancellationToken = default) =>
        PostInternalAsync(
            $"api/v1/internal/wallets/sub-orders/{subOrderId}/subsidy-release",
            new { reason },
            $"subsidy release of sub-order '{subOrderId}'",
            cancellationToken);

    public async Task<IReadOnlyList<StaleSubsidyHold>?> ListStaleSubsidyHoldsAsync(int olderThanMinutes, int limit, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/v1/internal/wallets/subsidy-holds/stale?olderThanMinutes={olderThanMinutes}&limit={limit}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[REST Wallet Client] The list of stale subsidy holds failed with status {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)) root = data;
            if (root.ValueKind != JsonValueKind.Array) return null;

            var result = new List<StaleSubsidyHold>();
            foreach (var item in root.EnumerateArray())
            {
                result.Add(new StaleSubsidyHold(
                    item.GetProperty("subOrderId").GetGuid(),
                    item.GetProperty("orderId").GetGuid(),
                    item.GetProperty("amount").GetDecimal(),
                    item.GetProperty("heldSince").GetDateTimeOffset()));
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Wallet Client] The list of stale subsidy holds could not be read.");
            return null;
        }
    }

    /// <summary>
    /// Posts to an internal Wallet route and reports the outcome without ever throwing: a Wallet answer of 4xx is a permanent refusal,
    /// anything else (timeout, 5xx, feature switched off) is transient.
    /// </summary>
    private async Task<WalletCallOutcome> PostInternalAsync(string requestUri, object body, string what, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = JsonContent.Create(body) };
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var replay = false;
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("data", out var data)) root = data;
                    replay = root.TryGetProperty("isReplay", out var flag) && flag.ValueKind == JsonValueKind.True;
                }
                catch (JsonException)
                {
                    // A body that is not JSON does not change the fact that Wallet accepted the call.
                }

                return WalletCallOutcome.Ok(replay);
            }

            var (code, message) = ParseProblemDetails(content, (int)response.StatusCode);
            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || code == "FEATURE_DISABLED")
            {
                _logger.LogError("[REST Wallet Client] {What} refused: the feature is disabled on Wallet ({Code}).", what, code);
                return new WalletCallOutcome(false, 503, code ?? "FEATURE_DISABLED", message);
            }

            _logger.LogWarning("[REST Wallet Client] {What} failed with status {StatusCode} ({ErrorCode}): {Message}", what, (int)response.StatusCode, code, message);
            return new WalletCallOutcome(false, (int)response.StatusCode, code, message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Wallet Client] {What} failed before an answer was received.", what);
            return WalletCallOutcome.Transient("WALLET_UNAVAILABLE", ex.Message);
        }
    }

    private static (string ErrorCode, string ErrorMessage) ParseProblemDetails(string errorBody, int statusCode)
    {
        string? errorCode = null;
        string? errorMessage = null;

        if (!string.IsNullOrWhiteSpace(errorBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(errorBody);
                if (doc.RootElement.TryGetProperty("errorCode", out var codeElem))
                {
                    errorCode = codeElem.GetString();
                }
                if (doc.RootElement.TryGetProperty("detail", out var detailElem))
                {
                    errorMessage = detailElem.GetString();
                }
                if (string.IsNullOrWhiteSpace(errorMessage) && doc.RootElement.TryGetProperty("title", out var titleElem))
                {
                    errorMessage = titleElem.GetString();
                }
            }
            catch
            {
                // Fallback nếu response không phải JSON
            }
        }

        var finalCode = errorCode ?? (statusCode switch
        {
            400 => "VAL_INVALID_INPUT",
            401 => "UNAUTHORIZED",
            403 => "FORBIDDEN",
            404 => "WALLET_NOT_FOUND",
            409 => "PAYMENT_ORDER_CONFLICT",
            503 => "FEATURE_DISABLED",
            _ => "PAYMENT_FAILED"
        });

        var finalMessage = errorMessage ?? (!string.IsNullOrWhiteSpace(errorBody) ? errorBody : "Payment operation failed.");
        return (finalCode, finalMessage);
    }

    private sealed record WalletTransactionDto(
        string? Id,
        string? WalletId,
        string? UserId,
        string? Type,
        decimal Amount,
        string? Currency,
        decimal? BalanceAfter,
        string? Status,
        string? Note,
        DateTimeOffset? CreatedAt);

    private sealed record WalletDetailsResponse(
        Guid Id,
        Guid UserId,
        decimal Balance,
        string Currency,
        string Status);
}
