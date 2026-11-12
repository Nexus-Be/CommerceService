# Technical Research & Architecture Decisions: Purchase Record Synchronization

**Feature Branch**: `020-catalog-purchase-sync`  
**Date**: 2026-11-11  
**Target Service**: `CommerceService` (Port `8003`)  
**Receiving Service**: `CatalogService` (Port `8002`)  

---

## 1. Background & Context

In the Nexus Microservices Architecture, product catalog management and reviews belong exclusively to `CatalogService` (`8002`), while shopping cart, checkout, payments, and order fulfillment tracking belong to `CommerceService` (`8003`).

To eliminate review fraud and ensure review credibility, `CatalogService` enforces **Verified Purchase Verification**:
- A buyer cannot post a review for a product (`POST /api/v1/products/{productId}/reviews`) unless a `PurchaseRecord` exists linking `(buyerId, orderId, productId)`.
- When an order is completed, `CommerceService` must synchronize the completed items to `CatalogService` via internal endpoint `POST /api/v1/internal/purchases`.

---

## 2. Research Decisions & Technical Trade-offs

### Decision 1: Outbound HTTP Client Integration via Clean Architecture
* **Context**: `CommerceService` adheres strictly to Clean Architecture (Inward dependency, no repository pattern).
* **Investigation**:
  - `ICatalogClient` is already defined in `Commerce.Application/Interfaces/ICatalogClient.cs`.
  - `CatalogClient` is implemented in `Commerce.Infrastructure/Services/Catalog/CatalogClient.cs`.
  - `DependencyInjection.cs` registers `ICatalogClient` with typed `HttpClient`, `CATALOG_SERVICE_URL` (default `http://localhost:8002/` or `http://catalog-service:8002/`), 10-second timeout, `IdentityPropagationHandler`, and `X-Internal-Service-Key`.
* **Decision**:
  - Add `CatalogPurchaseSyncRequest` to `Commerce.Application/Interfaces/ICatalogClient.cs`.
  - Add `Task<bool> SyncPurchaseRecordAsync(CatalogPurchaseSyncRequest request, CancellationToken cancellationToken = default)` to `ICatalogClient`.
  - Add helper `Task<int> SyncPurchasesBatchAsync(Guid buyerId, Guid orderId, IEnumerable<Guid> productIds, DateTimeOffset? completedAt = null, CancellationToken cancellationToken = default)` to execute concurrent requests via `Task.WhenAll`.
  - Implement these in `CatalogClient.cs`.

---

### Decision 2: In-Memory Deduplication (Anti-Spam & Latency Optimization)
* **Problem**: A buyer can order multiple variants of the same product in a single sub-order (e.g., T-shirt Blue Size M and T-shirt Red Size L). Both items share the exact same `ProductId`.
* **Catalog Constraint**: `CatalogService` maintains a unique constraint on `(buyerId, orderId, productId)`.
* **Decision**:
  - Before sending sync requests, `CommerceService` performs in-memory deduplication:
    ```csharp
    var distinctProductIds = subOrder.Items
        .Select(i => i.ProductId)
        .Where(id => id != Guid.Empty)
        .Distinct()
        .ToList();
    ```
  - This eliminates redundant HTTP calls, reduces network latency, and avoids unnecessary database upserts in `CatalogService`.

---

### Decision 3: Resilient Non-Blocking Fault Tolerance
* **Problem**: What happens if `CatalogService` experiences downtime, temporary network timeout, or returns a 500/503 error during order completion?
* **Trade-off Analysis**:
  - *Option A (Fail-Closed / Rollback)*: Throw an exception and abort the order completion / escrow settlement. **REJECTED**: The buyer physically confirmed receipt, and the seller has fulfilled the contract. The seller's funds MUST NOT be locked because a downstream review auxiliary service is down.
  - *Option B (Non-Blocking Fail-Safe with Structured Logging)*: Wrap the outbound sync call in a `try-catch` block inside `CatalogClient` or `OrderService`. Log warnings with order and product IDs for audit and reconciliation. **ACCEPTED**: Verified reviews are secondary auxiliary side-effects (identical to seller reputation adjustment via `UserService.AdjustReputationAsync`).
* **Decision**:
  - `CatalogClient.SyncPurchaseRecordAsync` returns `bool` (`true` for 200/201, `false` for errors).
  - Network exceptions (`HttpRequestException`, `TaskCanceledException`) and HTTP error codes are logged as warnings and do not throw unhandled exceptions to the caller.
  - Core order completion and escrow payouts commit cleanly regardless of Catalog sync status.

---

### Decision 4: Entity Framework Core Navigation Include Fixes
* **Code Audit Finding**:
  - `OrderService.BuyerConfirmSubOrderReceiptAsync` currently uses:
    ```csharp
    var so = await _dbContext.SubOrders
        .FirstOrDefaultAsync(s => s.Id == subOrderId, ct);
    ```
    `so.Items` is NOT loaded! Attempting to access `so.Items` would yield an empty collection due to disabled lazy loading.
  - `OrderService.CompleteOrderAsync` currently uses:
    ```csharp
    var order = await _dbContext.Orders
        .Include(o => o.SubOrders)
        .FirstOrDefaultAsync(o => o.Id == orderId, ct);
    ```
    `so.Items` within `order.SubOrders` is NOT loaded!
* **Resolution**:
  - In `BuyerConfirmSubOrderReceiptAsync`, add `.Include(s => s.Items)`.
  - In `CompleteOrderAsync`, add `.Include(o => o.SubOrders).ThenInclude(so => so.Items)`.
  - In `AuctionOrderService`, `AuctionOrder` already holds `ProductId` as a first-class scalar property on the root entity, so no navigation include is needed.

---

### Decision 5: OrderId Mapping Standards
* **Context**: The payload contract requires `orderId: Guid`.
* **Analysis**:
  - `SubOrder` is the bounded delivery and seller unit. In frontend UX, buyers review items per delivered package from a specific shop.
  - `AuctionOrder` is an independent order with its own UUID (`auctionOrder.Id`).
* **Decision**:
  - When confirming a `SubOrder` receipt: pass `subOrder.Id`.
  - When completing a master `Order`: pass `subOrder.Id` for each sub-order's items.
  - When confirming an `AuctionOrder` receipt: pass `auctionOrder.Id`.
  - This guarantees 100% compatibility with `CatalogService` review verification.

---

### Decision 6: Concurrent Execution via `Task.WhenAll`
* **Performance Analysis**:
  - If a sub-order has 5 items, sequential `await` loops would accumulate network overhead (e.g. 5 $\times$ 50ms = 250ms).
  - `Task.WhenAll` dispatches all distinct product sync requests concurrently over the pooled `HttpClient`. Total execution time is bounded by the slowest single request ($\sim$30-50ms).
* **Decision**:
  - Implement `SyncPurchasesBatchAsync` using `Task.WhenAll` to ensure optimal API response latency.
