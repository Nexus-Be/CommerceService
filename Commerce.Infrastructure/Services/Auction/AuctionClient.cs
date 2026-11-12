namespace Commerce.Infrastructure.Services.Auction;

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;

public sealed class AuctionClient : IAuctionClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuctionClient> _logger;
    private readonly string? _serviceKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AuctionClient(
        HttpClient httpClient,
        ILogger<AuctionClient> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _serviceKey = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")
            ?? configuration["INTERNAL_SERVICE_SECRET"]
            ?? configuration["InternalSecurity:ServiceKey"]
            ?? "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e";
    }

    public async Task<bool> NotifyPaymentStatusAsync(
        Guid auctionId,
        AuctionPaymentStatusCallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var effectiveEventId = request.EventId ?? Guid.NewGuid();
        var uri = $"api/v1/auctions/{auctionId}/settlement/payment-status";

        var payload = new
        {
            orderId = request.OrderId,
            winnerId = request.WinnerId,
            status = request.Status,
            amount = request.Amount,
            paidAt = request.PaidAt,
            eventId = effectiveEventId
        };

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };

            if (!string.IsNullOrWhiteSpace(_serviceKey) && !httpRequest.Headers.Contains("X-Internal-Service-Key"))
            {
                httpRequest.Headers.Add("X-Internal-Service-Key", _serviceKey);
            }

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[AuctionClient] Callback succeeded for Auction '{AuctionId}', Order '{OrderId}', Status '{Status}', EventId '{EventId}'.",
                    auctionId, request.OrderId, request.Status, effectiveEventId);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("[AuctionClient] Callback returned non-success status {StatusCode} for Auction '{AuctionId}', EventId '{EventId}'. Response: {Response}",
                response.StatusCode, auctionId, effectiveEventId, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AuctionClient] Network error dispatching callback for Auction '{AuctionId}', Order '{OrderId}', EventId '{EventId}'.",
                auctionId, request.OrderId, effectiveEventId);
            return false;
        }
    }
}
