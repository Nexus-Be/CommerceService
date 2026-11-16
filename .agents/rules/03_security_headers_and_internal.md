# 03 — SECURITY HEADERS, INTERNAL API & IDENTITY (COMMERCE SERVICE)

## 1. Public Controllers And Internal Security Model
* All controllers live under `Commerce.Api/Controllers/Public/` (`CartsController`, `OrdersController`, `AuctionOrdersController`).
* Global `InternalServiceAuthMiddleware` registration is commented out at the HTTP pipeline level. Inter-service outbound calls use plain REST routes.
* **Controller-Level Security for Sensitive Callbacks**:
  - `POST /api/v1/sub-orders/{subOrderId}/delivery-status` (Fulfillment webhook)
  - `POST /api/v1/auction-orders/settlement` (Auction settlement callback)
  These actions explicitly inspect the `X-Internal-Service-Key` header against `INTERNAL_SERVICE_SECRET` at the controller level to prevent unauthorized status transitions from public networks.

## 2. Trusted Identity Extraction & Ownership Guard
* Never trust client query parameters or request body `buyerId`.
* Always extract authenticated user ID from gateway-propagated headers via extension methods in `Commerce.Api.Extensions.HttpContextExtensions`:
  ```csharp
  if (!this.TryGetUserId(out var currentUserId, out var authError))
  {
      return authError;
  }

  if (!this.EnsureOwnership(order.BuyerId, out var forbidError))
  {
      return forbidError;
  }
  ```
* If `currentUserId != buyerId` and user does not have `admin` or `staff` role, immediately return HTTP 403 `FORBIDDEN`.
