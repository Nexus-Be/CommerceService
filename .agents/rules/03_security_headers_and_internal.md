# 03 — SECURITY HEADERS, INTERNAL API & IDENTITY (COMMERCE SERVICE)

## 1. Controller Segregation
* `Commerce.Api/Controllers/Public/`: Serves public cart and order endpoints (`/api/v1/carts`, `/api/v1/orders`).
* `Commerce.Api/Controllers/Internal/`: Serves inter-service internal endpoints (`/api/v1/internal/...`).

## 2. Internal Service Security
* All internal endpoints protected by `InternalServiceAuthMiddleware` with `X-Internal-Service-Key`.
* Outbound client `WalletClient` registered with `InternalServiceAuthHandler` to call Wallet internal checkout endpoint `POST /api/v1/internal/wallets/checkout/direct`.

## 3. Trusted Identity Extraction & Ownership Guard
* Never trust client query parameters `buyerId`.
* Check resource ownership using extension methods in `Commerce.Api.Extensions.HttpContextExtensions`:
  ```csharp
  var currentUserId = this.GetUserId();
  if (currentUserId == null)
      return this.UnauthorizedError("Authentication is required to view orders.");

  if (!this.EnsureOwnership(order.BuyerId, out var forbidError))
      return forbidError;
  ```
