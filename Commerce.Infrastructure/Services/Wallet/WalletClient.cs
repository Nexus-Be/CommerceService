namespace Commerce.Infrastructure.Services.Wallet;

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
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

    public async Task<WalletPaymentResult> PayOrderAsync(
        Guid orderId,
        decimal amount,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order ID cannot be empty.", nameof(orderId));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Payment amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("A buyer access token is required.", nameof(accessToken));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/v1/internal/wallet/orders/{orderId:D}/payment")
        {
            Content = JsonContent.Create(new { amount })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = new WalletPaymentResult(
            response.StatusCode,
            responseBody,
            ReadErrorCode(responseBody),
            ReadTransactionId(responseBody));

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "[REST Wallet Client] Payment confirmed for Order '{OrderId}'.",
                orderId);
        }
        else
        {
            _logger.LogWarning(
                "[REST Wallet Client] Payment failed for Order '{OrderId}'. Status: {StatusCode}, Error code: {ErrorCode}",
                orderId,
                result.StatusCode,
                result.ErrorCode);
        }

        return result;
    }

    private static string? ReadErrorCode(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (TryGetStringProperty(root, "errorCode", out var errorCode) ||
                TryGetStringProperty(root, "code", out errorCode))
            {
                return errorCode;
            }

            if (TryGetProperty(root, "error", out var error) &&
                error.ValueKind == JsonValueKind.Object &&
                (TryGetStringProperty(error, "errorCode", out errorCode) ||
                 TryGetStringProperty(error, "code", out errorCode)))
            {
                return errorCode;
            }
        }
        catch (JsonException)
        {
            // Keep the original body in WalletPaymentResult for callers to report or inspect.
        }

        return null;
    }

    private static string? ReadTransactionId(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                TryGetStringProperty(document.RootElement, "id", out var transactionId))
            {
                return transactionId;
            }
        }
        catch (JsonException)
        {
            // A successful HTTP status remains authoritative; preserve the body for the caller.
        }

        return null;
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!TryGetProperty(element, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}