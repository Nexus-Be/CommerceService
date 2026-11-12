# Feature Specification: Purchase Record Synchronization (CommerceService ➔ CatalogService)

**Feature Branch**: `020-catalog-purchase-sync`  
**Created**: 2026-11-11  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port `8003`, Database `commerce_db:5435`)  
**Receiving Service**: `CatalogService` (Port `8002`, Database `catalog_db:5432`)  
**Reference Document**: `COMMERCE_PURCHASE_SYNC_GUIDE.md` (Dated 2026-11-10)  
**Input**: User description: "Tích hợp đồng bộ đơn mua hàng (Purchase Record Sync) từ CommerceService sang CatalogService theo tài liệu COMMERCE_PURCHASE_SYNC_GUIDE.md: Mã tính năng: 020-catalog-purchase-sync; Mục tiêu: Kích hoạt quyền đánh giá sản phẩm (Verified Purchase Review) cho Buyer bên CatalogService (Port 8002) qua POST /api/v1/internal/purchases; Contract gửi: { buyerId, orderId, productId, completedAt }; Ràng buộc: Chỉ truyền ProductId (ID sản phẩm cha, không truyền variant/skuId). Lọc distinct ProductId nếu đơn có nhiều biến thể cùng sản phẩm cha; Điểm kích hoạt: 1. Buyer xác nhận nhận hàng đơn con (POST /api/v1/sub-orders/{subOrderId}/confirm-receipt), 2. Buyer hoàn tất đơn cha (POST /api/v1/orders/{orderId}/complete), 3. Buyer xác nhận nhận hàng đơn đấu giá (POST /api/v1/auction-orders/{id}/confirm-receipt); Kiến trúc & Khả năng chịu lỗi: Non-blocking side-effect (bọc try-catch và ghi log cảnh báo, không làm sập hoặc rollback giao dịch hoàn tất đơn và giải ngân ví Seller nếu CatalogService lỗi mạng), Giao tiếp REST qua ICatalogClient và CatalogClient trong Commerce.Infrastructure, Sử dụng Task.WhenAll khi gửi danh sách sản phẩm để tối ưu độ trễ, Viết đầy đủ unit tests cho CatalogClient và OrderLifecycle."

---

## 1. Executive Summary & Business Objective

To preserve product review authenticity and prevent fraudulent or spam ratings in the Nexus Microservices Ecosystem, `CatalogService` enforces a strict **Verified Purchase Review** gate:
- Buyers may only submit product reviews (`POST /api/v1/products/{productId}/reviews`) if an active `PurchaseRecord` exists linking `(buyerId, orderId, productId)`.
- When an order transitions to `Completed` (either via explicit buyer receipt confirmation or master order completion), `CommerceService` is responsible for synchronizing completed purchase records to `CatalogService` at endpoint `POST /api/v1/internal/purchases`.

This specification formalizes the outbound REST synchronization contract, event triggers, deduplication, error resilience, and automated verification within `CommerceService`.

---

## 2. User Scenarios & Testing *(mandatory)*

### User Story 1 - Sub-Order Delivery Receipt Confirmation & Concurrent Purchase Sync (Priority: P1)

As an authenticated buyer who received their package from a shop, I want to confirm receipt of my sub-order (`POST /api/v1/sub-orders/{subOrderId}/confirm-receipt`), so that the sub-order is marked `Completed`, seller escrow payout is triggered, and all distinct products within that sub-order are synchronized to CatalogService, immediately unlocking my ability to write verified reviews.

**Why this priority**:
Sub-order receipt confirmation is the primary customer journey for package delivery acknowledgment in a multi-seller commerce platform. Immediate synchronization allows buyers to evaluate products while their experience is fresh.

**Independent Test**:
Seed an order with a sub-order in `Delivered` status containing 2 items with distinct `ProductId`s. Call `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt`. Assert:
1. Sub-order status becomes `Completed` with `CompletedAt` populated.
2. Escrow funds are settled to the seller.
3. Outbound calls target `CatalogService` (`POST /api/v1/internal/purchases`) for each distinct `ProductId` with matching `{ buyerId, orderId: subOrder.Id, productId, completedAt }`.
4. Response returns HTTP 200 OK with `ConfirmReceiptResponse`.

**Acceptance Scenarios**:
1. **Given** a sub-order owned by `currentUserId` in `Delivered` or `Shipped` status containing multiple items, **When** `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt` is executed, **Then** `CommerceService` updates `SubOrder.Status = Completed`, triggers wallet escrow release, rewards seller reputation, and fires concurrent outbound synchronization calls via `ICatalogClient.SyncPurchaseRecordAsync` for all distinct `ProductId`s.
2. **Given** a sub-order containing 3 line items where 2 items belong to the same parent `ProductId` (different `SkuId`s/variants), **When** receipt is confirmed, **Then** `CommerceService` performs in-memory deduplication and sends exactly 2 unique purchase synchronization requests to `CatalogService`.
3. **Given** `CatalogService` returns HTTP 201 Created or HTTP 200 OK (idempotent duplicate), **Then** `CommerceService` logs successful synchronization and completes without error.
4. **Given** `CatalogService` is unavailable (e.g., returns 500/503 or network times out), **Then** `CommerceService` catches the exception, logs a warning with details, and **does not** fail or roll back the sub-order completion or seller settlement transaction.

---

### User Story 2 - Master Order Completion & Cascading Purchase Synchronization (Priority: P1)

As an authenticated buyer who placed a multi-shop master order, I want to confirm completion of the entire master order (`POST /api/v1/orders/{orderId}/complete`), so that all active sub-orders transition to `Completed` and all products across all sub-orders are synchronized to CatalogService.

**Why this priority**:
Buyers often complete entire multi-vendor orders from a top-level order management view. All delivered items must be unlocked for review uniformly.

**Independent Test**:
Seed a master order with 2 sub-orders in `Delivered` status. Call `POST /api/v1/orders/{orderId}/complete`. Assert:
1. Master order status becomes `Completed`.
2. Both sub-orders transition to `Completed`.
3. Outbound synchronization calls target `CatalogService` for all distinct `ProductId`s across all sub-orders.
4. Response returns HTTP 200 OK with `OrderCompleteResponse`.

**Acceptance Scenarios**:
1. **Given** an order owned by `currentUserId` with active sub-orders, **When** `POST /api/v1/orders/{orderId}/complete` is executed, **Then** `CommerceService` marks `Order.Status = Completed`, completes each eligible sub-order, and synchronizes all distinct products to `CatalogService`.
2. **Given** one of the sub-orders was previously `Cancelled`, **When** the master order is completed, **Then** items from the cancelled sub-order are excluded from Catalog synchronization.
3. **Given** one of the sub-orders was already `Completed` (previously synced via individual receipt confirmation), **Then** the idempotent Catalog API safely returns HTTP 200 OK without creating duplicate purchase records.

---

### User Story 3 - Won Auction Order Delivery Receipt Confirmation (Priority: P2)

As an authenticated winning bidder who received their auction package, I want to confirm receipt of my auction order (`POST /api/v1/auction-orders/{id}/confirm-receipt`), so that the auction order transitions to `Completed`, seller escrow is settled, and the auction product's `ProductId` is synchronized to CatalogService for verified review.

**Why this priority**:
Auction orders are formal commerce orders created from won bids. Winning buyers who purchase catalog products via auction must also receive verified review privileges.

**Independent Test**:
Seed an `AuctionOrder` in `Delivered` or `Shipped` status. Call `POST /api/v1/auction-orders/{id}/confirm-receipt`. Assert:
1. `AuctionOrder.Status` becomes `Completed` with `CompletedAt` timestamp.
2. Seller escrow payout is released.
3. Outbound call targets `CatalogService` (`POST /api/v1/internal/purchases`) with `{ buyerId: auctionOrder.BuyerId, orderId: auctionOrder.Id, productId: auctionOrder.ProductId, completedAt }`.
4. Response returns HTTP 200 OK with `ConfirmReceiptResponse`.

**Acceptance Scenarios**:
1. **Given** an `AuctionOrder` in `Delivered` or `Shipped` status owned by `currentUserId`, **When** `POST /api/v1/auction-orders/{id}/confirm-receipt` is executed, **Then** `CommerceService` marks `Status = Completed`, logs status history, settles seller escrow, and dispatches a purchase record sync to `CatalogService` for `auctionOrder.ProductId`.
2. **Given** `CatalogService` is unreachable during auction receipt confirmation, **Then** `CommerceService` logs the warning and returns HTTP 200 OK without failing the buyer's confirmation or seller payout.

---

### User Story 4 - Outbound Catalog REST Client Implementation & Resilience (Priority: P1)

As a developer maintaining CommerceService, I need a robust typed HTTP client method in `ICatalogClient` and `CatalogClient` that targets `POST /api/v1/internal/purchases` with proper serialization, timeout, error logging, and non-blocking semantics.

**Why this priority**:
Clean Architecture dictates that all outbound HTTP communication resides in `Commerce.Infrastructure` behind domain abstractions in `Commerce.Application/Interfaces`. Centralizing error handling and serialization in `CatalogClient` ensures maintainability and prevents code duplication across services.

**Independent Test**:
Execute unit tests with mocked `HttpMessageHandler`. Assert:
1. Successful call (201 Created or 200 OK) returns `true`.
2. 400 Bad Request / 404 Not Found returns `false` and logs warning without throwing unhandled exceptions.
3. 500 / 503 / HttpRequestException returns `false` and logs error without bubbling to caller.

**Acceptance Scenarios**:
1. **Given** a valid `CatalogPurchaseSyncRequest`, **When** `ICatalogClient.SyncPurchaseRecordAsync` is called, **Then** `CatalogClient` sends an HTTP POST request to `api/v1/internal/purchases` with JSON body `{ buyerId, orderId, productId, completedAt }`.
2. **Given** response status `201 Created` or `200 OK`, **Then** the method returns `true`.
3. **Given** response status `400 Bad Request` or `404 Not Found`, **Then** `CatalogClient` logs an informative warning detailing the error response and returns `false`.
4. **Given** a network connection failure or 5xx server error, **Then** `CatalogClient` catches the exception, logs a warning/error with context, and returns `false` gracefully.

---

## 3. Edge Cases & Resilience Strategy

| Edge Case / Condition | Expected Behavior in CommerceService |
| :--- | :--- |
| **Multiple items sharing the same parent `ProductId`** | In-memory `DistinctBy(i => i.ProductId)` or `Select(i => i.ProductId).Distinct()` ensures only ONE sync request per unique product is dispatched per order/sub-order. |
| **Empty or zero line items in SubOrder** | Query must include `.Include(s => s.Items)`. If `Items.Count == 0`, log a warning and skip Catalog sync without throwing. |
| **CatalogService responds with 200 OK** | Treated as 100% successful (idempotent record already existed in CatalogService). |
| **CatalogService returns 404 `PRODUCT_NOT_FOUND`** | Log warning: product was removed or missing from Catalog Master Data. Core transaction commits successfully. |
| **CatalogService network timeout or 503 outage** | Non-blocking fail-safe: catch exception, log warning with order and product IDs for reconciliation audit. Core transaction commits successfully. |
| **SubOrder already `Completed`** | Existing validation in `BuyerConfirmSubOrderReceiptAsync` throws HTTP 400 `INVALID_STATUS` with message `"Sub-order cannot be confirmed in its current status."`. No redundant sync triggered. |

---

## 4. Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `Commerce.Application` MUST define an outbound contract record `CatalogPurchaseSyncRequest(Guid BuyerId, Guid OrderId, Guid ProductId, DateTimeOffset? CompletedAt)`.
- **FR-002**: `ICatalogClient` MUST declare method `Task<bool> SyncPurchaseRecordAsync(CatalogPurchaseSyncRequest request, CancellationToken cancellationToken = default)`.
- **FR-003**: `ICatalogClient` MUST declare helper method `Task<int> SyncPurchasesBatchAsync(Guid buyerId, Guid orderId, IEnumerable<Guid> productIds, DateTimeOffset? completedAt = null, CancellationToken cancellationToken = default)` executing concurrent requests via `Task.WhenAll`.
- **FR-004**: `CatalogClient` MUST implement `SyncPurchaseRecordAsync` targeting `POST api/v1/internal/purchases` using the configured `CATALOG_SERVICE_URL`.
- **FR-005**: `CatalogClient` MUST treat HTTP `201 Created` and HTTP `200 OK` as successful completions returning `true`.
- **FR-006**: `CatalogClient` MUST catch all `HttpRequestException`, `TaskCanceledException`, and non-success HTTP status codes, log structured warnings with contextual metadata, and return `false` without throwing exceptions to calling application services.
- **FR-007**: `OrderService.BuyerConfirmSubOrderReceiptAsync` MUST include sub-order `Items` (`.Include(s => s.Items)`), extract distinct `ProductId` values, and invoke purchase synchronization upon transitioning `SubOrder.Status = Completed`.
- **FR-008**: `OrderService.CompleteOrderAsync` MUST include sub-order items (`.Include(o => o.SubOrders).ThenInclude(so => so.Items)`), extract distinct `ProductId` values across all newly completed sub-orders, and invoke purchase synchronization.
- **FR-009**: `AuctionOrderService.BuyerConfirmAuctionDeliveryAsync` MUST invoke purchase synchronization for `auctionOrder.ProductId` upon transitioning `AuctionOrder.Status = Completed`.
- **FR-010**: All error messages MUST strictly adhere to 100% English sentence case. All error codes MUST follow `SCREAMING_SNAKE_CASE`.

### Non-Functional Requirements & Governance Invariants

- **NFR-001 (Clean Architecture & No Repository)**: Database queries must remain direct over `ICommerceDbContext` / `AppDbContext`. No repository wrappers or UnitOfWork abstractions.
- **NFR-002 (Non-Blocking Side-Effect)**: Purchase record synchronization must never fail, abort, or roll back financial escrow settlement or buyer delivery receipt confirmation.
- **NFR-003 (Low Latency & Anti-N+1)**: Multi-item purchase synchronization must execute concurrently via `Task.WhenAll` to avoid sequential latency compounding.
- **NFR-004 (Inward-Only Scope)**: No relative paths outside `D:\CommerceService`. All contract documentation and test mocks must be self-contained within this repository.

---

## 5. Contract & Data Model Alignment

### Outbound Request Contract (CatalogService `POST /api/v1/internal/purchases`)

```json
{
  "buyerId": "dddddddd-4444-4444-4444-444444444444",
  "orderId": "99000000-0000-0000-0000-000000000002",
  "productId": "11000000-0000-0000-0000-000000000001",
  "completedAt": "2026-11-11T20:30:00Z"
}
```

### Property Mapping Table

| Property | Source Entity & Field | Type | Required | Notes |
| :--- | :--- | :---: | :---: | :--- |
| `buyerId` | `SubOrder.BuyerId` / `AuctionOrder.BuyerId` | `Guid` | Yes | Buyer account UUID |
| `orderId` | `SubOrder.Id` / `AuctionOrder.Id` | `Guid` | Yes | Sub-order UUID or Auction Order UUID |
| `productId` | `SubOrderItem.ProductId` / `AuctionOrder.ProductId` | `Guid` | Yes | Parent product UUID (NEVER `SkuId`) |
| `completedAt` | `SubOrder.CompletedAt` / `AuctionOrder.CompletedAt` | `DateTimeOffset?` | No | ISO 8601 UTC timestamp |

---

## 6. Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of distinct products in a confirmed sub-order trigger synchronization calls to `CatalogService`.
- **SC-002**: Multi-item sub-orders execute synchronization concurrently, completing all outbound HTTP calls within `< 300ms` total added latency.
- **SC-003**: In an intentional fault injection test where `CatalogService` returns HTTP 500 or disconnects, 100% of receipt confirmations and seller escrow settlements succeed without failure.
- **SC-004**: Full solution build (`dotnet build Commerce.sln -c Release -nodeReuse:false -m:1 --no-restore`) succeeds with 0 errors and 0 warnings.
- **SC-005**: All unit tests in `tests/Commerce.UnitTests` pass successfully.
