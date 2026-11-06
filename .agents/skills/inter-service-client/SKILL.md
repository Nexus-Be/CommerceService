---
name: inter-service-client
description: Add or modify outbound typed REST clients in CommerceService (port 8003). Use when CommerceService calls WalletService, CatalogService, or FulfillmentService without using message brokers.
---

# Outbound REST Client in CommerceService

> Scope: CommerceService repository only. Never reference parent workspace paths, root documentation, or sibling service directories.

## Local Facts

- Service: CommerceService
- Local HTTP Port: 8003
- Primary Downstream Targets:
  - WalletService (`8008`): Direct checkout payments (`IWalletClient` -> `POST api/v1/wallets/checkout/direct`)
  - CatalogService (`8002`): SKU inventory and price validation (`ICatalogClient`)
  - FulfillmentService (`8006`): Order shipment creation (`IFulfillmentClient`)
- Interface Location: `Commerce.Application/Interfaces/`
- Implementation Location: `Commerce.Infrastructure/Services/{Target}/`
- DI Registration: `Commerce.Infrastructure/DependencyInjection.cs`

## Invariants

1. **100% RESTful HTTP/JSON:** RabbitMQ, MassTransit, and Saga orchestrators are decommissioned. All inter-service calls use typed `HttpClient`.
2. **Mandatory Idempotency Keys:** Mutating payment calls to WalletService must transmit the `Idempotency-Key` header with a UUIDv4 string to prevent double charges upon retry.
3. **Identity Propagation:** Pass the buyer identity in the `x-user-id` request header.
4. **Strict Isolation:** Never reference external assemblies or query other microservice databases directly.
5. **No Internal API Key:** Inter-service routes use standard public paths. Do not send `X-Internal-Service-Key`.
6. **Timeout:** Configure a strict 10-second timeout per client.
7. **W3C Distributed Tracing:** Preserve and propagate `traceparent` headers across calls to WalletService, CatalogService, and FulfillmentService so that payment and checkout spans correlate in Grafana Tempo.

## Implementation Steps

### 1. Application Contract
Define `Commerce.Application/Interfaces/IWalletClient.cs`:
```csharp
namespace Commerce.Application.Interfaces;

public interface IWalletClient
{
    Task<bool> CheckoutDirectPaymentAsync(
        Guid orderId,
        string userId,
        decimal amount,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);
}
```

### 2. Infrastructure Typed Client
Implement `Commerce.Infrastructure/Services/Wallet/WalletClient.cs`:
```csharp
namespace Commerce.Infrastructure.Services.Wallet;

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
            var payload = new { orderId, userId, amount };
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/wallets/checkout/direct")
            {
                Content = JsonContent.Create(payload)
            };

            if (!string.IsNullOrWhiteSpace(userId))
            {
                request.Headers.TryAddWithoutValidation("x-user-id", userId);
            }

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("[REST Wallet] Payment failed: {Status} {Error}", response.StatusCode, errorBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REST Wallet] Connection error during direct checkout.");
            return false;
        }
    }
}
```

### 3. Dependency Injection Registration
In `Commerce.Infrastructure/DependencyInjection.cs`:
```csharp
var walletBaseUrl = Environment.GetEnvironmentVariable("WALLET_SERVICE_URL")
    ?? configuration["WALLET_SERVICE_URL"]
    ?? configuration["Services:Wallet:BaseUrl"]
    ?? "http://localhost:8008/";

services.AddHttpClient<IWalletClient, WalletClient>(client =>
{
    client.BaseAddress = new Uri(walletBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
```

### 4. Configuration Synchronization
Ensure `WALLET_SERVICE_URL`, `CATALOG_SERVICE_URL`, and `FULFILLMENT_SERVICE_URL` are present in `docker-compose.yml` and `.env.example`.
