namespace Commerce.Infrastructure.Services.Wallet;

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;

public sealed class WalletClient : IWalletClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WalletClient> _logger;

    public WalletClient(HttpClient httpClient, ILogger<WalletClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> CheckoutDirectPaymentAsync(
        Guid orderId,
        string userId,
        decimal amount,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                orderId,
                userId,
                amount
            };

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "api/v1/wallets/checkout/direct")
            {
                Content = JsonContent.Create(payload)
            };

            if (!string.IsNullOrWhiteSpace(userId))
            {
                requestMessage.Headers.TryAddWithoutValidation("x-user-id", userId);
            }

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                requestMessage.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            }

            var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[REST Wallet Client] Checkout payment succeeded for Order '{OrderId}', User '{UserId}'.", orderId, userId);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("[REST Wallet Client] Checkout payment failed for Order '{OrderId}'. Status: {StatusCode}, Details: {Details}",
                orderId, response.StatusCode, errorBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Wallet Client] Connection error while dispatching payment for Order '{OrderId}'.", orderId);
            return false;
        }
    }
}
