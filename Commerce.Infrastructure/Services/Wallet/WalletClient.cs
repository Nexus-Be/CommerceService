namespace Commerce.Infrastructure.Services.Wallet;

using System;
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
    /// Khởi tạo phiên thanh toán trực tiếp qua cổng VNPay:
    /// POST /api/v1/wallets/checkout/direct
    /// Header: x-user-id: <buyer UUID>
    /// Body: { "orderId": "<uuid>", "amount": <decimal>, "returnUrl": "..." }
    /// </summary>
    public async Task<DirectCheckoutResult> InitiateDirectCheckoutAsync(
        Guid orderId,
        Guid buyerId,
        decimal amount,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var effectiveBuyerId = ResolveEffectiveUserId(buyerId);
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/wallets/checkout/direct")
            {
                Content = JsonContent.Create(new
                {
                    orderId,
                    amount,
                    returnUrl
                })
            };

            if (effectiveBuyerId != Guid.Empty)
            {
                request.Headers.TryAddWithoutValidation("x-user-id", effectiveBuyerId.ToString());
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var dto = await response.Content.ReadFromJsonAsync<DirectCheckoutResponseDto>(JsonOptions, cancellationToken);
                _logger.LogInformation("[REST Wallet Client] Direct VNPay checkout created for Order '{OrderId}'. URL: '{Url}'.", orderId, dto?.PaymentUrl);
                return DirectCheckoutResult.Success(dto?.PaymentUrl ?? string.Empty, dto?.TransactionId);
            }

            var statusCode = (int)response.StatusCode;
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("[REST Wallet Client] Direct checkout for Order '{OrderId}' failed with status {StatusCode}: {ErrorBody}", orderId, statusCode, errorBody);

            var (errorCode, errorMessage) = ParseProblemDetails(errorBody, statusCode);
            return DirectCheckoutResult.Failure(errorCode, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Wallet Client] Connection error during direct checkout for Order '{OrderId}'.", orderId);
            return DirectCheckoutResult.Failure("CONNECTION_ERROR", ex.Message);
        }
    }

    /// <summary>
    /// Thăm dò trạng thái quyết toán thanh toán trực tiếp VNPay cho đơn hàng:
    /// GET /api/v1/wallets/checkout/direct/{orderId}
    /// Header: x-user-id: <buyer UUID>
    /// </summary>
    public async Task<DirectCheckoutStatusResult?> GetDirectCheckoutStatusAsync(
        Guid orderId,
        Guid buyerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var effectiveBuyerId = ResolveEffectiveUserId(buyerId);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/wallets/checkout/direct/{orderId}");

            if (effectiveBuyerId != Guid.Empty)
            {
                request.Headers.TryAddWithoutValidation("x-user-id", effectiveBuyerId.ToString());
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var dto = await response.Content.ReadFromJsonAsync<DirectCheckoutStatusDto>(JsonOptions, cancellationToken);
                if (dto != null)
                {
                    return new DirectCheckoutStatusResult(
                        dto.OrderId,
                        dto.TransactionId,
                        dto.Status,
                        dto.Amount,
                        dto.PaidAt);
                }
            }

            _logger.LogDebug("[REST Wallet Client] Direct checkout query for Order '{OrderId}' returned status {StatusCode}.", orderId, (int)response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Wallet Client] Connection error polling direct checkout status for Order '{OrderId}'.", orderId);
            return null;
        }
    }

    /// <summary>
    /// Phương thức tương thích ngược với luồng cũ, gọi PayOrderAsync.
    /// </summary>
    public async Task<bool> CheckoutDirectPaymentAsync(
        Guid orderId,
        string userId,
        decimal amount,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        _ = Guid.TryParse(userId, out var parsedUserId);
        var result = await PayOrderAsync(orderId, parsedUserId, amount, bearerToken: null, cancellationToken);
        return result.IsSuccess;
    }

    /// <summary>
    /// Tra cứu số dư ví:
    /// GET /api/v1/wallets/me
    /// Header: x-user-id: <userId>
    /// </summary>
    public async Task<decimal> GetWalletBalanceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/wallets/me");
            var effectiveUserId = ResolveEffectiveUserId(userId);
            if (effectiveUserId != Guid.Empty)
            {
                request.Headers.TryAddWithoutValidation("x-user-id", effectiveUserId.ToString());
            }

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
    /// Tự động hoàn tiền SubOrder vào ví Buyer qua WalletService (RefundCredit).
    /// POST /api/v1/wallets/sub-orders/{subOrderId}/refund
    /// S2S Route: Không yêu cầu identity header; neo idempotency trên subOrderId.
    /// </summary>
    public async Task<bool> RefundSubOrderPaymentAsync(
        Guid subOrderId,
        string userId,
        decimal amount,
        string reason,
        string? idempotencyKey = null,
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
                    reason
                })
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Wallet Client] SubOrder '{SubOrderId}' refund succeeded for User '{UserId}', Amount: {Amount}.", subOrderId, userId, amount);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("[REST Wallet Client] SubOrder '{SubOrderId}' refund failed with status {StatusCode}: {ErrorBody}", subOrderId, (int)response.StatusCode, errorBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Wallet Client] Error during refund for SubOrder '{SubOrderId}'.", subOrderId);
            return false;
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

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("[REST Wallet Client] Order '{OrderId}' refund failed with status {StatusCode}: {ErrorBody}", orderId, (int)response.StatusCode, errorBody);
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

            // Nếu 409 SETTLEMENT_CONFLICT do replay lại cùng seller và amount, hợp đồng WalletService vẫn an toàn
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogInformation("[REST Wallet Client] SubOrder '{SubOrderId}' settlement returned 409 Conflict. Treating idempotent resolution.", subOrderId);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("[REST Wallet Client] SubOrder '{SubOrderId}' settlement failed with status {StatusCode}: {ErrorBody}", subOrderId, (int)response.StatusCode, errorBody);
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

    private sealed record DirectCheckoutResponseDto(
        Guid TransactionId,
        Guid OrderId,
        decimal Amount,
        string PaymentUrl,
        string Status,
        DateTimeOffset CreatedAt);

    private sealed record DirectCheckoutStatusDto(
        Guid OrderId,
        Guid? TransactionId,
        string Status,
        decimal Amount,
        DateTimeOffset? PaidAt);
}
