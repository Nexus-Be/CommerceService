namespace Commerce.Infrastructure.Services.Wallet;

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
    /// Hợp đồng chuẩn bàn giao từ WalletService:
    /// POST /api/v1/wallets/orders/{orderId}/payment
    /// Header: Authorization: Bearer <buyer JWT>
    /// Body: { "amount": <positive decimal> }
    /// </summary>
    public async Task<WalletPaymentResult> PayOrderAsync(
        Guid orderId,
        decimal amount,
        string? bearerToken = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var token = bearerToken;
            if (string.IsNullOrWhiteSpace(token) && _httpContextAccessor?.HttpContext != null)
            {
                var authHeader = _httpContextAccessor.HttpContext.Request.Headers.Authorization.ToString();
                if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = authHeader["Bearer ".Length..].Trim();
                }
            }

            var requestUri = $"api/v1/wallets/orders/{orderId}/payment";
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(new { amount })
            };

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var tx = await response.Content.ReadFromJsonAsync<WalletTransactionDto>(cancellationToken: cancellationToken);
                _logger.LogInformation("[REST Wallet Client] Order '{OrderId}' payment succeeded. TxID: '{TxId}'.", orderId, tx?.Id);
                return new WalletPaymentResult(
                    IsSuccess: true,
                    TransactionId: tx?.Id,
                    BalanceAfter: tx?.BalanceAfter);
            }

            var statusCode = (int)response.StatusCode;
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("[REST Wallet Client] Order '{OrderId}' payment failed with status {StatusCode}: {ErrorBody}", orderId, statusCode, errorBody);

            string? errorCode = null;
            string? errorMessage = null;
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

            var isTransient = statusCode >= 500;
            return new WalletPaymentResult(
                IsSuccess: false,
                TransactionId: null,
                ErrorCode: errorCode ?? (statusCode switch
                {
                    404 => "WALLET_NOT_FOUND",
                    409 => "INSUFFICIENT_BALANCE",
                    401 => "UNAUTHORIZED",
                    400 => "VAL_INVALID_INPUT",
                    _ => "PAYMENT_FAILED"
                }),
                ErrorMessage: errorMessage ?? errorBody,
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

    /// <summary>
    /// Phương thức tương thích ngược với luồng cũ, gọi PayOrderAsync bên dưới.
    /// </summary>
    public async Task<bool> CheckoutDirectPaymentAsync(
        Guid orderId,
        string userId,
        decimal amount,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var result = await PayOrderAsync(orderId, amount, bearerToken: null, cancellationToken);
        return result.IsSuccess;
    }

    public async Task<decimal> GetWalletBalanceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/wallets/me");
            if (_httpContextAccessor?.HttpContext != null)
            {
                var authHeader = _httpContextAccessor.HttpContext.Request.Headers.Authorization.ToString();
                if (!string.IsNullOrWhiteSpace(authHeader))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", authHeader);
                }
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<WalletDetailsResponse>(cancellationToken: cancellationToken);
                return result?.Balance ?? 0m;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Wallet Client] Connection error getting wallet details for User '{UserId}'. Fallback to 0.", userId);
        }

        return 0m;
    }

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

            var key = idempotencyKey ?? $"refund-suborder-{subOrderId}";
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

            if (_httpContextAccessor?.HttpContext != null)
            {
                var authHeader = _httpContextAccessor.HttpContext.Request.Headers.Authorization.ToString();
                if (!string.IsNullOrWhiteSpace(authHeader))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", authHeader);
                }
            }

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

            var key = idempotencyKey ?? $"refund-order-{orderId}";
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

            if (_httpContextAccessor?.HttpContext != null)
            {
                var authHeader = _httpContextAccessor.HttpContext.Request.Headers.Authorization.ToString();
                if (!string.IsNullOrWhiteSpace(authHeader))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", authHeader);
                }
            }

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

            var key = idempotencyKey ?? $"settle-{subOrderId}";
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

            if (_httpContextAccessor?.HttpContext != null)
            {
                var authHeader = _httpContextAccessor.HttpContext.Request.Headers.Authorization.ToString();
                if (!string.IsNullOrWhiteSpace(authHeader))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", authHeader);
                }
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Wallet Client] SubOrder '{SubOrderId}' settlement succeeded for Seller '{SellerId}', Amount: {Amount}.", subOrderId, sellerId, amount);
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
