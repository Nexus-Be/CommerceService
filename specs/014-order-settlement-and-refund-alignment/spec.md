# Feature Specification: Order Settlement, Automated Refund, and Seed Data Alignment

**Feature Branch**: `014-order-settlement-and-refund-alignment`  
**Created**: 2026-11-06  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  
**Input User Prompt**: 
> "Triển khai toàn diện các chức năng và khắc phục xung đột kỹ thuật theo Báo cáo đối chiếu 4 backend service ngày 05/11/2026: (1) Khắc phục lỗi EF Core tracking CartItem mới khi thêm vào giỏ và xử lý dứt điểm lỗi conflict JSON metadata seed data của SubOrder.ShippingAddress (gỡ bỏ seed Order/SubOrder khỏi HasData CommerceSeedData, tạo migration FixSubOrderJsonSeed và update snapshot); (2) Tích hợp Outbound REST Client với WalletService mới để tự động hoàn tiền cho Buyer (RefundOrderPaymentCommand) khi Shop từ chối đơn hàng hoặc lỗi DB persistence thay vì chỉ ghi log chờ đối soát thủ công; (3) Tích hợp giải ngân Escrow sang ví Seller (SettleSellerOrderCommand) và mở API cho Buyer xác nhận đã nhận hàng (POST /api/v1/sub-orders/{subOrderId}/confirm-receipt), cập nhật SubOrder.Status sang Completed; (4) Mở API cho Seller tra cứu danh sách đơn con của Shop (GET /api/v1/sub-orders) hỗ trợ phân trang tại Database và lọc theo status; (5) Bổ sung FULFILLMENT_SERVICE_URL vào docker-compose.yml, cập nhật bộ Unit Tests E2E Flow và Order Checkout Tests, đảm bảo 100% tiếng Anh Sentence-case cho error messages, error code SCREAMING_SNAKE_CASE, dotnet build 0 error 0 warning và pass 100% test suite."

---

## 1. User Scenarios & Testing *(mandatory)*

### User Story 1 - Database Stability: CartItem Tracking & SubOrder JSON Seed Fix (Priority: P1)

As a Buyer and DevOps engineer, I need the shopping cart to reliably track and persist newly added items without losing changes, and the database migrations to run cleanly without EF Core JSON ownership metadata conflicts (`HasData` conflict with `ToJson()`).

**Why this priority**:
Adding items to the cart is the top of the commerce conversion funnel. Fails in entity tracking drop items, while migration metadata crashes block CI/CD pipeline deployments and application startup.

**Independent Test**:
1. Call `POST /api/v1/carts/me/items` with a brand new SKU; verify the record is persisted in PostgreSQL table `CartItems` and HTTP 200 returns the updated item count.
2. Run database migration on a clean schema; assert zero EF Core model building exceptions regarding `HasData` on owned JSON navigation `ShippingAddress`.

**Acceptance Scenarios**:
1. **Given** an authenticated buyer with an active cart, **When** they add a SKU that does not yet exist in their cart, **Then** EF Core explicitly tracks the new `CartItem` as `Added`, `SaveChangesAsync` inserts the record into PostgreSQL, and the cart total updates accurately.
2. **Given** `CommerceSeedData.cs`, **When** EF Core builds the database model snapshot, **Then** `SeedOrders` is omitted from `HasData`, preventing `InvalidOperationException` on JSON-mapped `ShippingAddressSnapshot`.
3. **Given** migration `20261104150739_FixSubOrderJsonSeed`, **When** applied, **Then** model snapshot reflects the clean schema without phantom order seed data.

---

### User Story 2 - Automated Wallet Refund on Order Rejection and DB Failure (Priority: P1)

As a Buyer whose order was rejected by a Seller or whose order placement encountered a persistence failure, the system must automatically refund 100% of the debited wallet balance via the WalletService public REST contract without requiring manual CSKH intervention.

**Why this priority**:
Holding buyer funds in limbo upon seller cancellation violates trust and consumer protection rules, while forcing manual reconciliation creates massive support overhead.

**Independent Test**:
Can be independently verified by executing `SellerRejectSubOrderAsync` on a paid SubOrder and asserting an outbound HTTP call targets WalletService refund API with the buyer's identifier, the refund amount, and an idempotency key, updating `OrderRefund.Status` to `Completed`.

**Acceptance Scenarios**:
1. **Given** a paid SubOrder (`wasPaid == true`), **When** the seller rejects the sub-order via `POST /api/v1/orders/{orderId}/reject`, **Then** the system transitions `SubOrder.Status = Cancelled`, invokes `_walletClient.RefundSubOrderPaymentAsync(...)`, creates `OrderCancellation`, and marks `OrderRefund.Status = Completed`.
2. **Given** the outbound Wallet refund call succeeds, **Then** the system also invokes `_fulfillmentClient.ReleaseSubOrderStockAsync` exclusively for the SKUs belonging to the rejected SubOrder.
3. **Given** checkout persistence encounters a database connection error after wallet debit, **When** the compensation circuit triggers, **Then** the system calls `_walletClient.RefundOrderPaymentAsync` with idempotency key `compensation-db-{orderId}` and releases reserved stock.

---

### User Story 3 - Buyer Goods Receipt Confirmation & Seller Escrow Settlement (Priority: P1)

As a Buyer who has physically received their package, I want to confirm receipt in the application, so that the sub-order marks as completed and the system automatically releases the escrowed funds to the Seller's wallet.

**Why this priority**:
Sellers cannot receive payout until the buyer confirms or delivery grace period expires. Automated escrow release is the final phase of multi-vendor marketplace commerce.

**Independent Test**:
Can be independently verified by calling `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt` as the authenticated buyer when status is `Delivered`. Assert `SubOrder.Status` becomes `Completed`, `CompletedAt` is recorded, and an outbound HTTP call to WalletService (`SettleSellerOrder`) transfers `SellerReceiveAmount` to the seller.

**Acceptance Scenarios**:
1. **Given** an authenticated buyer, **When** they call `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt` for their own sub-order currently in `Delivered` (or `Shipped`) status, **Then** `SubOrder.Status` transitions to `Completed` with `CompletedAt = UtcNow`.
2. **Given** receipt is confirmed, **When** post-processing executes, **Then** the system triggers `_walletClient.SettleSellerSubOrderAsync` with `Idempotency-Key: settle-{subOrderId}` and amount `so.SellerReceiveAmount`.
3. **Given** a caller who is not the buyer of the sub-order, **When** attempting confirmation, **Then** the system returns HTTP 403 `FORBIDDEN` with English sentence-case message `"You do not have permission to confirm this sub-order."`.
4. **Given** a sub-order that is in `PendingPayment` or `Cancelled`, **When** confirmation is attempted, **Then** the system rejects with HTTP 400 `INVALID_STATUS` and message `"Sub-order cannot be confirmed in its current status."`.

---

### User Story 4 - Seller Sub-Orders Listing with Database Pagination (Priority: P1)

As a Seller, I need a dedicated REST endpoint to retrieve all sub-orders belonging to my shop, with server-side database pagination and status filtering, so that I can manage my order dispatch pipeline effectively.

**Why this priority**:
Sellers cannot view or process their customer orders without a dedicated listing query. In-memory loading causes severe performance bottlenecks under high volume.

**Independent Test**:
Can be independently verified by authenticating as a seller and requesting `GET /api/v1/sub-orders?page=1&pageSize=20&status=Paid`, asserting the response is structured as `PagedData<SubOrderSummaryResponse>` and SQL executes `.Skip().Take()` without loading foreign shops' data.

**Acceptance Scenarios**:
1. **Given** an authenticated seller (`currentUserId`), **When** calling `GET /api/v1/sub-orders`, **Then** the query filters exclusively by `so.SellerId == currentUserId`.
2. **Given** query parameters `page`, `pageSize`, and optional `status`, **When** the query executes, **Then** pagination is applied directly in PostgreSQL, returning `{ items: [...], pagination: { pageIndex, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage } }`.
3. **Given** an unauthenticated request or missing seller role, **Then** the system returns HTTP 401 or 403 accordingly.

---

### User Story 5 - Environment Alignment, Error Governance & Unit Test Coverage (Priority: P2)

As a Software Engineer and QA Auditor, I need the container environment properly configured with `FULFILLMENT_SERVICE_URL`, error messages 100% compliant with English Sentence-case rules, and the automated test suite updated to achieve 100% pass rate.

**Why this priority**:
Ensures operational readiness, clean container networking, unified developer experience, and zero regression across the 115+ test cases.

**Independent Test**:
1. Inspect `docker-compose.yml` to confirm `FULFILLMENT_SERVICE_URL` is defined.
2. Run `dotnet test` and assert all unit and E2E flow tests pass.
3. Run `dotnet build Commerce.sln -c Release` and confirm 0 errors and 0 warnings.

**Acceptance Scenarios**:
1. **Given** `docker-compose.yml`, **Then** `FULFILLMENT_SERVICE_URL: "${FULFILLMENT_SERVICE_URL:-http://fulfillment-service:8006}"` is present alongside Wallet and Catalog URLs.
2. **Given** all domain and validation exceptions, **Then** error messages are strictly English Sentence-case (e.g., `"Sub-order not found."`, `"Insufficient stock available in warehouse."`) and error codes are `SCREAMING_SNAKE_CASE`.
3. **Given** `EndToEndFlowTests.cs` and `OrderCheckoutTests.cs`, **Then** test mocks reflect the new automated refund and settlement contracts, passing with 100% success rate.

---

## 2. Success Criteria

1. **Clean Architecture & No-Repository**: Direct usage of `ICommerceDbContext` / `AppDbContext` in Application layer. Zero repository abstractions.
2. **Clean RESTful API**: Plural routes (`/api/v1/sub-orders`, `/api/v1/orders`), pure DTO returns, zero `ResultFilter` wrapping.
3. **Port & Database Invariance**: Service operates on port `8003`, DB connects to PostgreSQL `commerce_db:5435`.
4. **Idempotency in Financial Calls**: All outbound Wallet settlement and refund calls include deterministic idempotency keys (`settle-{subOrderId}`, `refund-{subOrderId}`, `compensation-db-{orderId}`).
5. **Compilation & Quality Gate**: `dotnet build Commerce.sln -c Release` succeeds with **0 Error(s)** and **0 Warning(s)**.
