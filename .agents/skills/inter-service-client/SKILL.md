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
  - WalletService (`8008`): Order payments (`IWalletClient` -> `POST api/v1/internal/wallet/orders/{orderId}/payment`)
  - CatalogService (`8002`): SKU inventory and price validation (`ICatalogClient`)
  - FulfillmentService (`8006`): Order shipment creation (`IFulfillmentClient`)
- Interface Location: `Commerce.Application/Interfaces/`
- Implementation Location: `Commerce.Infrastructure/Services/{Target}/`
- DI Registration: `Commerce.Infrastructure/DependencyInjection.cs`

## Invariants

1. **100% RESTful HTTP/JSON:** RabbitMQ, MassTransit, and Saga orchestrators are decommissioned. All inter-service calls use typed `HttpClient`.
2. **Payment idempotency:** WalletService uses the stable Commerce order ID in the payment route as its idempotency reference. Retries must keep the same order ID, buyer token, and amount.
3. **Identity propagation:** Forward the buyer's original access token using `Authorization: Bearer`. Do not send a user ID in the request body or an identity header.
4. **Strict Isolation:** Never reference external assemblies or query other microservice databases directly.
5. **Private payment route:** Call WalletService directly at `/api/v1/internal/wallet/orders/{orderId}/payment`; never route this internal operation through the public Gateway or send `X-Internal-Service-Key`.
6. **Timeout:** Configure a strict 10-second timeout per client.

## Implementation Steps

### 1. Application Contract
Define `Commerce.Application/Interfaces/IWalletClient.cs`:
```csharp
namespace Commerce.Application.Interfaces;

public interface IWalletClient
{
    Task<WalletPaymentResult> PayOrderAsync(
        Guid orderId,
        decimal amount,
        string accessToken,
        CancellationToken cancellationToken = default);
}
```

`WalletPaymentResult` must retain the HTTP status, response body, Wallet error code when present, and transaction ID when returned. A successful payment is confirmed only by `200 OK`. Let timeout and connection exceptions propagate so checkout can keep the order pending and retry with the same order ID, buyer token, and amount.

### 2. Infrastructure Typed Client
Implement `Commerce.Infrastructure/Services/Wallet/WalletClient.cs` so it:

- Sends `POST api/v1/internal/wallet/orders/{orderId}/payment` with a JSON body containing only `amount`.
- Forwards the buyer's original token using `Authorization: Bearer`.
- Returns the original HTTP status and body, parsed Wallet `errorCode` when present, and transaction `id` on success.
- Treats only `200 OK` as confirmed payment and propagates timeout/connection exceptions because the debit outcome may be unknown.

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
Set `WALLET_SERVICE_URL` in `docker-compose.yml` (default `http://wallet-service:8008`) and the local/server environment examples. `localhost` is for running Commerce and Wallet in a shared host namespace only.
