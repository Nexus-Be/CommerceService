# 03 — SECURITY HEADERS, INTERNAL API & IDENTITY (COMMERCE SERVICE)

## 1. Public Controllers And Temporary Internal Disable
* All controllers live under `Commerce.Api/Controllers/Public/`. There is no `Controllers/Internal/` folder.
* Internal API security is temporarily disabled. `InternalServiceAuthMiddleware` registration is commented; no route requires `X-Internal-Service-Key`.
* Inter-service calls use plain REST routes: `POST /api/v1/notifications/email/otp`, `POST /api/v1/wallets/checkout/direct`, `POST /api/v1/inventories/initialize`, `POST /api/v1/logs/batch`.
* Re-enabling the old model requires explicit approval and restoring middleware, handler, Gateway blocking, and docs together.

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
