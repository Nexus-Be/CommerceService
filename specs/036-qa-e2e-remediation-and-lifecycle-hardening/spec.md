# Feature Specification: QA E2E Remediation, Buy Now Contract Parity, Stock Resilience & Lifecycle Hardening

**Feature Branch**: `036-qa-e2e-remediation-and-lifecycle-hardening`  
**Created**: 2026-11-21  
**Status**: Ready for Implementation  
**Target Service**: `CommerceService` (Port `8003`, Database `commerce_db:5435`)  
**Scope**: Inward-Only (CommerceService internal architecture, contracts, persistence, and external service clients)  
**Input**: Phân tích đối soát mã nguồn thực tế `CommerceService` với Báo cáo kiểm thử toàn diện Phase 2 (E2E 20/11/2026) và Báo cáo thay đổi so với `dev` (19/11/2026). Giải quyết triệt để 5 vấn đề cốt lõi tại CommerceService: (1) Lỗi D-14: Hợp đồng `WinningBidId` nullable cho Settlement Mua ngay (Buy Now); (2) Lỗi D-01: Chuyển cấu hình `StockPolicy:FailOpen` sang fail-closed ngăn bán vượt tồn; (3) Lỗi D-07 & D-08: Tiếp nhận trạng thái `Returning` từ Fulfillment, loại bỏ việc hủy sớm đơn đấu giá khi `DeliveryFailed`, và tự động phạt điểm uy tín Buyer khi bom hàng (`Returned`); (4) Lỗi D-12: Khắc phục migration `20261106065814_FixSubOrderJsonSeed` vi phạm khóa ngoại FK khi khởi tạo DB trống; (5) Lỗi D-06: Bổ sung endpoint nội bộ `POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund` cho UserService giải quyết khiếu nại hoàn tiền.

---

## 1. Background & Codebase Audit Findings

Đối soát trực tiếp mã nguồn `CommerceService` trên nhánh `dev` tại thời điểm hiện tại:

1. **Vấn đề D-14 (`WinningBidId` in Buy Now Settlements)**:
   - File: `Commerce.Application/Services/AuctionOrder/AuctionOrderRequests.cs` dòng 21 đang định nghĩa:
     ```csharp
     [Required] Guid WinningBidId,
     ```
   - File: `Commerce.Domain/Entities/AuctionOrder.cs` dòng 12 định nghĩa:
     ```csharp
     public required Guid WinningBidId { get; set; }
     ```
   - **Hậu quả**: Khi `AuctionService` thực hiện settlement cho luồng Buy Now (`source = "BUY_NOW"`), giao dịch không phát sinh bid nên `WinningBidId = null`. Khi Auction gửi payload sang `POST /api/v1/auction-orders/settlement`, Model Validation của ASP.NET Core ném HTTP 400 Bad Request: `"The WinningBidId field is required."`. Đơn hàng Buy Now không thể được tạo.

2. **Vấn đề D-01 (Stock Reservation `FailOpen` Behavior)**:
   - File: `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs` dòng 34–38:
     ```csharp
     var failOpenSetting = configuration?["StockPolicy:FailOpen"]
         ?? Environment.GetEnvironmentVariable("STOCK_FAIL_OPEN");
     _failOpen = string.IsNullOrEmpty(failOpenSetting) || (bool.TryParse(failOpenSetting, out var val) && val);
     ```
   - File: `FulfillmentClient.cs` dòng 168–175: Khi lệnh gọi giữ kho thất bại (500 hoặc 4xx), hệ thống chỉ ghi `_logger.LogWarning` và trả về `_failOpen = true`.
   - **Hậu quả**: Nếu không có cấu hình rõ ràng, Commerce luôn mặc định nuốt lỗi giữ kho. Đơn hàng vẫn được tạo và thanh toán dẫn đến bán vượt tồn kho (overselling). Cần chuyển sang Fail-Closed mặc định trên môi trường Staging/Production.

3. **Vấn đề D-07 & D-08 (Delivery Lifecycle & Buyer Penalty on Bom Hàng)**:
   - File: `Commerce.Application/Services/Order/OrderService.cs` dòng 2590–2685:
     - Chưa xử lý trạng thái webhook `"Returning"` khi bưu tá giao hàng thất bại lần thứ 3 và hãng vận chuyển bắt đầu hoàn hàng về kho.
     - Dòng 2942–2945: Tại `UpdateAuctionOrderDeliveryStatusAsync`, đơn đấu giá bị hủy ngay lập tức khi nhận trạng thái `DeliveryFailed` từ bưu tá thay vì duy trì trạng thái chờ quy trình hoàn trả.
     - Dòng 2683–2775: Khi trạng thái là `"Returned"` do người mua từ chối nhận hàng (bom hàng), Commerce chỉ hoàn ví và nhả kho, **hoàn toàn không gọi trừ điểm uy tín người mua**.
   - **Hậu quả**: Trạng thái đơn lệch pha so với hành trình thực tế của bưu cục GHN; người mua bom hàng không chịu bất kỳ chế tài nào trên nền tảng.

4. **Vấn đề D-12 (Database Migration FK Violation on Empty DB)**:
   - File: `Commerce.Infrastructure/Persistence/Migrations/20261106065814_FixSubOrderJsonSeed.cs` dòng 14–28:
     ```csharp
     migrationBuilder.DeleteData(table: "SubOrderItems", keyColumn: "Id", keyValue: new Guid("03000000-0000-0000-0000-000000000001"));
     migrationBuilder.DeleteData(table: "SubOrders", keyColumn: "Id", keyValue: new Guid("02000000-0000-0000-0000-000000000001"));
     migrationBuilder.DeleteData(table: "Orders", keyColumn: "Id", keyValue: new Guid("01000000-0000-0000-0000-000000000001"));
     ```
   - **Hậu quả**: Các bảng `DiscountCodeUsages`, `OrderInvoices`, `OrderStatusHistories` được seed tại `InitialCreate` đều chứa khóa ngoại tham chiếu tới `SubOrderId = 02000000-0000-0000-0000-000000000001`. Lệnh xóa `SubOrders` gây lỗi PostgreSQL `23503: FK_DiscountCodeUsages_SubOrders_SubOrderId` làm tê liệt mọi container khởi tạo database từ đầu.

5. **Vấn đề D-06 (Dispute Resolution S2S Refund Integration)**:
   - File: `Commerce.Api/Controllers/Public/OrdersController.cs` dòng 288–301 (`POST /sub-orders/{subOrderId}/refund`) bắt buộc JWT token của Buyer.
   - File: `Commerce.Api/Controllers/Public/RefundsController.cs` dòng 60 (`POST /refunds/{id}/approve`) đòi hỏi quyền Seller hoặc Staff duyệt yêu cầu refund có sẵn.
   - **Hậu quả**: Khi `UserService` phân xử khiếu nại (Report/Dispute) và quyết định Buyer thắng, UserService không thể kích hoạt giải ngân hoàn tiền trực tiếp do thiếu endpoint service-to-service an toàn.

---

## 2. User Scenarios & Testing *(mandatory)*

### User Story 1 - Buy Now Settlement Ingestion with Nullable WinningBidId (Priority: P1) 🎯 MVP

As an external Auction Service, when a buyer triggers "Buy Now", I need to push a settlement payload where `source = "BUY_NOW"` and `winningBidId` is omitted or null, so that Commerce Service accepts the settlement without validation errors, creates an `AuctionOrder` in `PendingPayment`, sets `RemainingAmount = 0 VND`, and allows the buyer to pay the shipping fee.

- **Why this priority**:
  In a Buy Now auction purchase, the transaction is executed immediately at the fixed buy-now price without placing bids in the bid ledger. `WinningBidId` does not exist. Blocking this field halts 100% of the Buy Now flow.
- **Independent Test**:
  Call `POST /api/v1/auction-orders/settlement` with payload:
  `{ "auctionId": "<guid>", "winnerId": "<guid>", "sellerId": "<guid>", "productId": "<guid>", "productName": "MacBook Pro M3", "winningAmount": 45000000, "depositAmount": 45000000, "paymentDeadlineAt": "<now+24h>", "source": "BUY_NOW" }` (omitting `winningBidId`).
  Assert HTTP 201 Created is returned, `OrderSource` is `BuyNow`, `WinningBidId` is `null`, and `RemainingAmount` is `0.0`.
- **Acceptance Scenarios**:
  1. **Given** a Buy Now settlement payload where `source: "BUY_NOW"` and `WinningBidId` is null or omitted, **When** `POST /api/v1/auction-orders/settlement` is invoked, **Then** Commerce accepts the request, persists `AuctionOrder` with `WinningBidId = null`, `OrderSource = AuctionOrderSource.BuyNow`, `RemainingAmount = 0`, and returns HTTP 201 Created.
  2. **Given** an incoming settlement where `source: "WINNER"` or `source: "SECOND_CHANCE"`, **When** `WinningBidId` is null or `Guid.Empty`, **Then** Commerce rejects with HTTP 400 Bad Request (`VAL_INVALID_SETTLEMENT`, `"Winning bid ID is required for winner and second-chance settlements."`).
  3. **Given** an existing Buy Now order with `WinningBidId == null`, **When** a duplicate settlement request is posted with identical terms (`WinningBidId == null`), **Then** idempotency logic correctly matches `null == null` and returns HTTP 200 OK with the existing order details without throwing `SETTLEMENT_CONFLICT`.

---

### User Story 2 - Fail-Closed Stock Reservation & Resilience Governance (Priority: P1)

As a Commerce Service system operator, I need stock reservation calls to Fulfillment Service to operate in a strict Fail-Closed mode by default in non-development environments, so that any failure to reserve stock immediately blocks order placement and prevents catastrophic inventory overselling.

- **Why this priority**:
  A silent fail-open stock policy permits buyers to place and pay for orders when physical stock does not exist. This creates negative inventory and causes physical delivery failures downstream.
- **Independent Test**:
  Configure `CommerceService` without `StockPolicy:FailOpen` (or with `StockPolicy:FailOpen = false`). Simulate a warehouse inventory reservation 500 error from Fulfillment Service. Attempt to checkout/place an order.
  Assert Commerce rejects order creation with HTTP 400 `INSUFFICIENT_STOCK` (or HTTP 503 `STOCK_RESERVATION_FAILED` if network unreachable) and logs an `LogError` event.
- **Acceptance Scenarios**:
  1. **Given** `StockPolicy:FailOpen` is unset or set to `false`, **When** `FulfillmentClient.ReserveStockAsync` receives a non-success HTTP status code from Fulfillment Service, **Then** the method returns `false`, `OrderService` throws `CommerceDomainException("INSUFFICIENT_STOCK")`, and order creation is aborted.
  2. **Given** `StockPolicy:FailOpen` is unset and the environment is `Production` or `Staging`, **When** `FulfillmentClient` fails to connect to Fulfillment Service, **Then** the client throws HTTP 503 `STOCK_RESERVATION_FAILED` ("Warehouse inventory service is temporarily unreachable.").
  3. **Given** an explicit local development override `StockPolicy:FailOpen = true` in `appsettings.Development.json`, **When** reservation fails, **Then** the client logs `LogError` explicitly warning that fail-open is active and returns `true`.

---

### User Story 3 - Delivery Status "Returning" Handling & Auction Order Resilience (Priority: P1)

As a logistics operations coordinator, when carrier GHN marks a package as returning to the seller after 3 failed delivery attempts, I need Commerce Service to accept the `"Returning"` status webhook, log the transition into order status history, keep the order active, and avoid prematurely cancelling auction orders until physical receipt by the seller (`"Returned"`).

- **Why this priority**:
  Currently, Commerce has no handler for `"Returning"`, causing unhandled states. Furthermore, auction orders were prematurely cancelled on the first `DeliveryFailed`, preventing accurate tracking while the package was still with the shipper.
- **Independent Test**:
  Call `POST /api/v1/sub-orders/{subOrderId}/delivery-status` with `status: "Returning"` and note `"Third delivery attempt failed. Package returning to merchant warehouse."`.
  Assert HTTP 200 OK, `OrderStatusHistory` records the transition from `Shipped` to `Returning`, and `SubOrder.Status` reflects the return journey without premature cancellation.
- **Acceptance Scenarios**:
  1. **Given** a `SubOrder` in `Shipped` status, **When** Fulfillment sends webhook `status: "Returning"`, **Then** Commerce records an `OrderStatusHistory` entry with `ToStatus = SubOrderStatus.Returning` (or `Shipped` with note `"Returning"`), updates `UpdatedAt`, and keeps the order active.
  2. **Given** an `AuctionOrder` in `Shipped` status, **When** Fulfillment sends `status: "DeliveryFailed"` (interim attempt), **Then** Commerce logs the attempt in history and does NOT cancel the order.
  3. **Given** an `AuctionOrder` in `Shipped` or `Returning` status, **When** Fulfillment sends `status: "Returned"`, **Then** Commerce transitions `AuctionOrder.Status = SubOrderStatus.Cancelled`, records `OrderCancellation` with `ReasonCode = CancellationReasonCode.PackageReturned`, refunds the payment, and logs the completion.

---

### User Story 4 - Buyer Reputation Penalty on Delivery Failure Return ("Bom Hàng") (Priority: P1)

As a platform governance officer, when a regular order is physically returned to the merchant warehouse (`"Returned"`) due to delivery failures or recipient refusal (bom hàng), I need Commerce Service to automatically penalize the buyer by deducting 5 reputation points via User Service, so that serial abusers are penalized and platform integrity is maintained.

- **Why this priority**:
  Sellers suffer physical shipping costs and inventory lock-in when buyers intentionally refuse delivery. Automatically penalizing buyers discourages non-serious purchases and aligns with the existing penalty system for abusive sellers.
- **Independent Test**:
  Given a paid `SubOrder` in `Shipped` status. Call `POST /api/v1/sub-orders/{subOrderId}/delivery-status` with `status: "Returned"`.
  Assert Commerce calls `IUserClient.AdjustReputationAsync` with `TargetRole = "Buyer"`, `UserId = so.BuyerId`, `ScoreDelta = -5`, `Reason = "PackageReturnedDeliveryFailed"`, and notes the penalty.
- **Acceptance Scenarios**:
  1. **Given** a `SubOrder` transitioned to `Returned` due to carrier return, **When** processing cancellation and inventory release, **Then** Commerce invokes `_userClient.AdjustReputationAsync` with:
     - `UserId`: `so.BuyerId`
     - `TargetRole`: `"Buyer"`
     - `PartnerUserId`: `so.SellerId`
     - `ScoreDelta`: `-5`
     - `Reason`: `"PackageReturnedDeliveryFailed"`
     - `ReferenceType`: `"Order"`
     - `ReferenceId`: `so.SubOrderNo`
     - `Note`: `"Automated penalty for uncollected package returned to seller."`
  2. **Given** `UserService` is temporarily unreachable during the penalty call, **When** `_userClient.AdjustReputationAsync` throws an exception, **Then** Commerce logs an error without aborting the order return and wallet refund transaction.

---

### User Story 5 - Database Migration Seed Foreign Key Remediation (Priority: P1)

As a DevOps engineer deploying a new environment, when `dotnet ef database update` runs on an empty database, I need all migrations to execute sequentially without foreign key constraint violations, so that clean staging, CI/CD, and production environments can boot automatically.

- **Why this priority**:
  Migration `20261106065814_FixSubOrderJsonSeed` crashes with error `23503` because dependent tables (`DiscountCodeUsages`, `OrderInvoices`, `OrderStatusHistories`) reference seed suborder `02000000-0000-0000-0000-000000000001`. Fixing the deletion order guarantees clean deployments.
- **Independent Test**:
  Run `FixSubOrderJsonSeed` against a PostgreSQL instance populated through `InitialCreate`.
  Assert migration executes to completion with 0 SQL exceptions.
- **Acceptance Scenarios**:
  1. **Given** an empty PostgreSQL database, **When** applying migrations up to the latest commit, **Then** dependent rows in `DiscountCodeUsages`, `OrderInvoices`, and `OrderStatusHistories` are safely removed prior to removing `SubOrders` row `02000000-0000-0000-0000-000000000001`.
  2. **Given** the migration snapshot, **When** inspected, **Then** all seed references remain completely consistent with the active model snapshot.

---

### User Story 6 - Internal S2S Dispute Resolution Refund Endpoint (Priority: P2)

As the User Service dispute resolution worker, when an administrator rules in favor of a buyer on an order dispute, I need to call a secured service-to-service endpoint on Commerce Service to execute an immediate full refund to the buyer's wallet and update the sub-order to `Refunded`, without requiring buyer authentication tokens.

- **Why this priority**:
  User Service operates asynchronously in the background. It cannot impersonate the buyer's JWT session. A dedicated S2S internal endpoint closes the loop on P2-05 dispute resolution.
- **Independent Test**:
  Call `POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund` with header `X-Internal-Service-Key: <secret>` and body `{ "disputeId": "<guid>", "refundAmount": 850000.0, "reason": "Item arrived damaged, buyer evidence verified." }`.
  Assert HTTP 200 OK is returned, buyer's wallet is credited via `_walletClient`, `SubOrder.Status` becomes `Refunded`, and `OrderRefund` record is persisted with `Status = Completed`.
- **Acceptance Scenarios**:
  1. **Given** a request without `X-Internal-Service-Key` or with an invalid key, **When** calling the dispute refund endpoint, **Then** Commerce returns HTTP 401 Unauthorized or HTTP 503 if secret unconfigured.
  2. **Given** a valid internal service key and a `SubOrder` in `Delivered` or `Completed` status, **When** `POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund` is called, **Then** Commerce:
     - Verifies `SubOrder` exists and was paid.
     - Creates or completes an `OrderRefund` record with `ReasonCode = "DISPUTE_RESOLUTION"`.
     - Calls `_walletClient.RefundSubOrderPaymentAsync`.
     - Sets `SubOrder.Status = SubOrderStatus.Refunded`.
     - Adds `OrderStatusHistory` logging that refund was executed via dispute resolution.
     - Returns HTTP 200 OK with `{ subOrderId, refundId, amount, status: "Completed" }`.
  3. **Given** a sub-order that is already `Refunded` or `Cancelled`, **When** the dispute refund endpoint is called, **Then** Commerce rejects with HTTP 400 Bad Request (`INVALID_REFUND_STATUS`).

---

## 3. Edge Cases

- **EC-001**: What if Auction Service sends `source: "BUY_NOW"` with `WinningBidId` provided (e.g., a dummy GUID)?  
  *Commerce accepts it, persists it, and evaluates idempotency against the provided GUID.*
- **EC-002**: What if an incoming settlement sends `source: "WINNER"` but omits `WinningBidId`?  
  *Commerce rejects with HTTP 400 `VAL_INVALID_SETTLEMENT` ("Winning bid ID is required for winner and second-chance settlements."). Non-BuyNow settlements strictly require a winning bid.*
- **EC-003**: What if `StockPolicy:FailOpen` is missing in `docker-compose.yml`?  
  *Commerce defaults to `false` in production/staging environments, logging an error and failing closed upon reservation errors.*
- **EC-004**: What if the carrier sends `"Returning"` twice?  
  *Idempotent handling: Commerce logs history only on change or updates the timestamp without altering the ongoing return status.*
- **EC-005**: What if a sub-order is COD when marked `"Returned"`?  
  *Commerce skips the wallet refund (since buyer never paid upfront), releases warehouse stock, and still executes the buyer reputation penalty.*
- **EC-006**: What if `UserService` reputation penalty call fails during package return?  
  *Commerce logs the failure but does NOT roll back the order cancellation or stock release. Platform state consistency takes priority.*
- **EC-007**: What if a dispute refund is triggered on a sub-order where an existing `OrderRefund` is already `Requested`?  
  *Commerce completes the existing pending refund, transitions it to `RefundStatus.Completed`, and executes the wallet refund.*

---

## 4. Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `AuctionSettlementRequest` MUST change `WinningBidId` from `[Required] Guid WinningBidId` to optional `Guid? WinningBidId = null`.
- **FR-002**: `AuctionOrder` domain entity MUST permit `WinningBidId` to be nullable (`Guid? WinningBidId`).
- **FR-003**: `AuctionOrderDetailResponse` and `AuctionSettlementResponse` MUST support nullable `Guid? WinningBidId`.
- **FR-004**: Idempotency check in `AuctionOrderService.ReceiveSettlementAsync` MUST safely compare `identicalOrder.WinningBidId != request.WinningBidId` when both are null.
- **FR-005**: `FulfillmentClient` MUST initialize `_failOpen` to `false` whenever `StockPolicy:FailOpen` is empty or unconfigured, unless `ASPNETCORE_ENVIRONMENT == "Development"`.
- **FR-006**: `FulfillmentClient` MUST log `LogError` whenever stock reservation returns a non-success HTTP status code.
- **FR-007**: `UpdateDeliveryStatusRequest` processing in `OrderService` MUST support `"Returning"` status, logging an `OrderStatusHistory` entry with reason and updating sub-order `UpdatedAt`.
- **FR-008**: `UpdateAuctionOrderDeliveryStatusAsync` in `OrderService` MUST NOT transition `AuctionOrder.Status` to `Cancelled` when receiving interim `DeliveryFailed` status. It MUST only transition to `Cancelled` on `"Returned"`.
- **FR-009**: When `SubOrder` transitions to `Cancelled` via `"Returned"` status, `OrderService` MUST invoke `_userClient.AdjustReputationAsync` with `TargetRole = "Buyer"`, `ScoreDelta = -5`, and `Reason = "PackageReturnedDeliveryFailed"`.
- **FR-010**: Migration `20261106065814_FixSubOrderJsonSeed` MUST delete referencing seed rows in `DiscountCodeUsages`, `OrderInvoices`, and `OrderStatusHistories` before deleting `SubOrders` row `02000000-0000-0000-0000-000000000001`.
- **FR-011**: Commerce MUST provide an internal endpoint `POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund` requiring `X-Internal-Service-Key`.
- **FR-012**: `POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund` MUST execute a wallet refund via `_walletClient.RefundSubOrderPaymentAsync`, transition `SubOrder.Status = SubOrderStatus.Refunded`, and record an `OrderRefund` with `Status = RefundStatus.Completed`.

---

### Non-Functional Requirements & Security

- **NFR-001**: 100% of error messages MUST follow English sentence-case formatting (e.g. `"Winning bid ID is required for winner and second-chance settlements."`).
- **NFR-002**: 100% of error codes MUST follow `SCREAMING_SNAKE_CASE` (e.g. `VAL_INVALID_SETTLEMENT`, `INSUFFICIENT_STOCK`, `UNAUTHORIZED`, `SUB_ORDER_NOT_FOUND`).
- **NFR-003**: Inward-Only compliance: No cross-service relative project references. All inter-service communications MUST use typed REST clients (`IWalletClient`, `IFulfillmentClient`, `IUserClient`).
- **NFR-004**: Service-to-service internal security MUST use `InternalServiceKeyGuard.Check(Request, configuration)` enforcing 401/503 fail-closed validation.
- **NFR-005**: Zero compilation warnings treated as errors, 100% test pass rate across `Commerce.sln`.

---

## 5. API Contracts & Interfaces

### 5.1 Updated Inbound Settlement Contract

`POST /api/v1/auction-orders/settlement` (Header: `X-Internal-Service-Key: <key>`)

```json
{
  "auctionId": "b1000000-0000-0000-0000-000000000001",
  "winnerId": "d1000000-0000-0000-0000-000000000001",
  "sellerId": "c1000000-0000-0000-0000-000000000001",
  "winningBidId": null,
  "productId": "e1000000-0000-0000-0000-000000000001",
  "productName": "MacBook Pro M3 Max",
  "winningAmount": 50000000.0,
  "depositAmount": 50000000.0,
  "paymentDeadlineAt": "2026-11-22T10:00:00Z",
  "source": "BUY_NOW"
}
```

Response: `201 Created`

```json
{
  "orderId": "f1000000-0000-0000-0000-000000000001",
  "orderNo": "AO-20261121-F10000",
  "auctionId": "b1000000-0000-0000-0000-000000000001",
  "buyerId": "d1000000-0000-0000-0000-000000000001",
  "status": "PendingPayment",
  "paymentDeadlineAt": "2026-11-22T10:00:00Z"
}
```

---

### 5.2 Internal S2S Dispute Refund Contract

`POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund`  
Header: `X-Internal-Service-Key: <key>`

Request:
```json
{
  "disputeId": "a1000000-0000-0000-0000-000000000001",
  "refundAmount": 850000.0,
  "reason": "Damaged goods in transit confirmed by support",
  "note": "Resolved in favor of buyer under Dispute #DISP-20261121-001"
}
```

Response: `200 OK`
```json
{
  "subOrderId": "02000000-0000-0000-0000-000000000001",
  "refundId": "r1000000-0000-0000-0000-000000000001",
  "amount": 850000.0,
  "status": "Completed",
  "completedAt": "2026-11-21T01:15:00Z"
}
```

Error Matrix:
- `401 Unauthorized`: Missing or invalid `X-Internal-Service-Key`.
- `404 Not Found`: SubOrder does not exist.
- `400 Bad Request` (`INVALID_REFUND_STATUS`): SubOrder is not in `Delivered` or `Completed` status, or is already refunded.
- `503 Service Unavailable`: Internal security key not configured or WalletService unreachable.

---

### 5.3 Updated Delivery Webhook Status Contract

`POST /api/v1/sub-orders/{subOrderId}/delivery-status`

Supported status wire values:
- `"ReadyToPick"`, `"Picking"`, `"Packed"`
- `"HandedOver"`, `"InTransit"`, `"OutForDelivery"`, `"Shipped"`
- `"Delivered"`
- `"DeliveryAttemptFailed"`, `"DeliveryFailed"` (interim attempts)
- `"Returning"` *(New)*: Logs transition to returning, keeps order active.
- `"Returned"`: Cancels order, refunds wallet balance, releases stock, applies `-5` reputation penalty to Buyer.

---

## 6. Success Criteria *(mandatory)*

- **SC-001**: 100% of Buy Now settlement requests without `WinningBidId` succeed with HTTP 201 Created and persist `WinningBidId = null`.
- **SC-002**: 100% of stock reservation failures in non-dev environments trigger Fail-Closed order rejection, preventing negative warehouse inventory.
- **SC-003**: 100% of carrier `"Returning"` status webhooks are accepted and recorded in `OrderStatusHistory` without premature cancellation.
- **SC-004**: 100% of returned packages due to delivery failures invoke buyer reputation penalty (-5 points) via `UserService`.
- **SC-005**: 100% of clean database installations via `dotnet ef database update` succeed with 0 foreign key constraint errors.
- **SC-006**: 100% test pass rate with 0 compilation errors across `Commerce.sln`.
