# Feature Specification: Auction Order Lifecycle, Settlement & Automated Reversal

**Feature Branch**: `019-auction-order-lifecycle-and-settlement`  
**Created**: 2026-11-11  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port `8003`, Database `commerce_db:5435`)  
**Input**: User description: "Triển khai toàn diện phân hệ Đơn hàng Đấu giá (AuctionOrder) theo Hợp đồng phản hồi v1.1.0 giữa CommerceService và AuctionService: (1) Tiếp nhận settlement tạo AuctionOrder với Idempotency theo cặp (AuctionId, WinnerId), hỗ trợ kịch bản người về nhì; (2) Winner nhập địa chỉ giao hàng và báo giá ship GHN chốt TotalAmount; (3) Winner thanh toán ví Nexus thành công, kích hoạt hạn Seller duyệt 24h và gửi callback Paid kèm paidAt; (4) Seller duyệt đơn chuyển Confirmed, tạo vận đơn GHN và gửi callback SellerAccepted; (5) Seller từ chối đơn chuyển Cancelled, tự động hoàn tiền ví 100% cho Buyer (fail-safe) và gửi callback SellerRejected; (6) Background worker quét tự hủy đơn quá hạn thanh toán 24h (sửa bug Foreign Key SubOrderId -> AuctionOrderId) và gửi callback PaymentTimedOut; (7) Background worker quét tự hủy đơn quá hạn Seller duyệt 24h, tự động hoàn tiền ví và gửi callback SellerExpired; (8) Mở rộng webhook giao vận GHN hỗ trợ cập nhật trạng thái đơn đấu giá; (9) Tích hợp Outbound REST Client IAuctionClient với header X-Internal-Service-Key tại cổng 8005; (10) Đảm bảo 100% tiếng Anh Sentence-case cho error messages, mã lỗi SCREAMING_SNAKE_CASE, dotnet build 0 error 0 warning và pass 100% test suite."

---

## 1. User Scenarios & Testing *(mandatory)*

### User Story 1 - Auction Settlement Ingestion & Multi-Winner Idempotency (Priority: P1)

As an external Auction Service, I need to push won auction settlement data to Commerce Service to automatically generate an auction order in `PendingPayment` status, with robust idempotency supporting runner-up winners if the original winner defaults.

**Why this priority**:
Auction outcomes must be captured as formal commerce orders. Without settlement ingestion, winning bidders cannot view, quote shipping, or pay for their auction items. Furthermore, if a defaulting winner is replaced by the runner-up bidder, the system must cleanly handle creating a new order for the same auction without conflict.

**Independent Test**:
Send `POST /api/v1/auction-orders/settlement` with an auction payload. Assert an `AuctionOrder` record is created in PostgreSQL with `OrderNo` prefix `AO-`, `Status = PendingPayment`, and HTTP 201 Created returns `orderId`, `orderNo`, and `paymentDeadlineAt`. Resending the same payload returns HTTP 200 OK with the existing order.

**Acceptance Scenarios**:
1. **Given** a valid won auction payload from Auction Service with matching `X-Internal-Service-Key`, **When** `POST /api/v1/auction-orders/settlement` is called, **Then** Commerce creates an `AuctionOrder` with `Status = SubOrderStatus.PendingPayment`, stores the exact `PaymentDeadlineAt` from Auction Service, and returns HTTP 201 Created with `{ orderId, orderNo, auctionId, paymentDeadlineAt, status: "PendingPayment" }`.
2. **Given** an existing `AuctionOrder` with identical `(AuctionId, WinnerId)`, **When** the settlement request is repeated, **Then** Commerce does not create a duplicate order, and returns HTTP 200 OK with the existing order details (idempotent).
3. **Given** an existing `AuctionOrder` for `AuctionId` where `Status == SubOrderStatus.Cancelled` (the original winner defaulted after 24 hours), **When** a new settlement arrives for the same `AuctionId` with a different `WinnerId` (runner-up bidder), **Then** Commerce accepts the request and creates a new `AuctionOrder` for the new winner.
4. **Given** an existing `AuctionOrder` for `AuctionId` where `Status != SubOrderStatus.Cancelled`, **When** a settlement arrives with a different `WinnerId`, **Then** Commerce rejects with HTTP 409 Conflict and error code `AUCTION_ALREADY_SETTLED`.

---

### User Story 2 - Winner Shipping Address Submission & Logistics Quote (Priority: P1)

As an authenticated winning bidder, I want to submit my shipping address for the auction order, so that the system calculates the real-time shipping fee and finalizes the total amount due before payment.

**Why this priority**:
Auction winning amounts do not include logistics costs. The winning buyer must confirm their delivery destination to lock in carrier shipping fees and enable wallet debit.

**Independent Test**:
Call `PUT /api/v1/auction-orders/{id}/shipping` with valid address fields. Assert `ShippingAddress`, `ContactPhone`, and `ShippingFeeAmount` are persisted, and `TotalAmount` equals `WinningAmount + ShippingFeeAmount`.

**Acceptance Scenarios**:
1. **Given** an authenticated buyer who owns an `AuctionOrder` in `PendingPayment` status within its payment deadline, **When** they call `PUT /api/v1/auction-orders/{id}/shipping` with recipient details, **Then** Commerce coordinates with Fulfillment Service to calculate carrier shipping fee, persists `ShippingFeeAmount` and `TotalAmount = WinningAmount + ShippingFeeAmount`, and returns HTTP 200 OK with updated totals.
2. **Given** an `AuctionOrder` whose `PaymentDeadlineAt` has elapsed, **When** shipping submission is attempted, **Then** Commerce rejects with HTTP 400 `PAYMENT_DEADLINE_EXPIRED` and message `"Auction payment deadline has expired."`.
3. **Given** a caller whose `currentUserId` does not match `BuyerId`, **When** shipping submission is attempted, **Then** Commerce rejects with HTTP 403 `FORBIDDEN` and message `"You do not have permission to manage this auction order."`.

---

### User Story 3 - Winner Wallet Payment & Immediate "Paid" Callback Dispatch (Priority: P1)

As an authenticated winning bidder, I want to pay for my auction order using my digital wallet, so that the order transitions to `Paid`, the seller is notified to dispatch within 24 hours, and Auction Service receives confirmation.

**Why this priority**:
Completing payment commits the financial transaction and triggers the seller's fulfillment deadline.

**Independent Test**:
Call `POST /api/v1/auction-orders/{id}/pay` for a quoted auction order. Assert wallet balance is debited, `Status` becomes `Paid`, `SellerRespondDeadlineAt` is set to `UtcNow + 24h`, and an outbound HTTP call targets Auction Service with `status: "Paid"` and `paidAt`.

**Acceptance Scenarios**:
1. **Given** an `AuctionOrder` with finalized `TotalAmount` and `Status == PendingPayment` within deadline, **When** the buyer calls `POST /api/v1/auction-orders/{id}/pay`, **Then** Commerce debits the buyer's wallet via `IWalletClient.PayOrderAsync`.
2. **Given** successful wallet payment, **Then** Commerce updates `auctionOrder.Status = SubOrderStatus.Paid`, `PaidAt = UtcNow`, sets `SellerRespondDeadlineAt = UtcNow.AddHours(24)`, logs `OrderStatusHistory`, and dispatches an outbound callback to Auction Service (`POST /api/v1/auctions/{auctionId}/settlement/payment-status`) with `{ orderId, winnerId, status: "Paid", amount: winningAmount, paidAt: UtcNow, eventId }`.
3. **Given** an `AuctionOrder` without shipping address or total amount, **When** payment is attempted, **Then** Commerce rejects with HTTP 400 `SHIPPING_ADDRESS_REQUIRED` and message `"Shipping address must be submitted before paying for the auction order."`.
4. **Given** an `AuctionOrder` past `PaymentDeadlineAt`, **When** payment is attempted, **Then** Commerce rejects immediately with HTTP 400 `PAYMENT_DEADLINE_EXPIRED` and does not call Wallet Service.

---

### User Story 4 - Seller Order Acceptance & "SellerAccepted" Callback Dispatch (Priority: P1)

As a seller who listed the auctioned product, I want to review and accept the paid auction order within my 24-hour response deadline, so that a logistics shipment is created and the product is permanently committed.

**Why this priority**:
Seller confirmation initiates physical package fulfillment and signals Auction Service to permanently mark the product as sold in Catalog.

**Independent Test**:
Call `POST /api/v1/auction-orders/{id}/accept` as the authenticated seller. Assert `Status` becomes `Confirmed`, `SellerAcceptedAt` is recorded, shipment is created, and callback `status: "SellerAccepted"` is sent to Auction Service.

**Acceptance Scenarios**:
1. **Given** an authenticated seller matching `auctionOrder.SellerId`, **When** they call `POST /api/v1/auction-orders/{id}/accept` while `Status == SubOrderStatus.Paid` and `UtcNow <= SellerRespondDeadlineAt`, **Then** Commerce updates `auctionOrder.Status = SubOrderStatus.Confirmed`, sets `SellerAcceptedAt = UtcNow`, creates a logistics shipment via `IFulfillmentClient.CreateShipmentAsync`, and calls Auction Service callback with `status: "SellerAccepted"`.
2. **Given** a paid `AuctionOrder` where `UtcNow > SellerRespondDeadlineAt`, **When** the seller attempts to accept, **Then** Commerce rejects with HTTP 400 `SELLER_RESPOND_TIMEOUT` and message `"Seller response deadline has expired. Order is awaiting cancellation."`.
3. **Given** a caller who is not the seller of the auction order, **When** accept is attempted, **Then** Commerce returns HTTP 403 `FORBIDDEN`.

---

### User Story 5 - Seller Order Rejection with Automated Wallet Refund & "SellerRejected" Callback (Priority: P1)

As a seller who cannot fulfill an auction order, I want to reject the order, so that 100% of the buyer's paid amount is immediately refunded to their wallet and Auction Service is notified to adjust seller reputation.

**Why this priority**:
Prevents customer funds from remaining locked when stock is damaged or unavailable, while maintaining an immutable audit trail and fair marketplace reputation.

**Independent Test**:
Call `POST /api/v1/auction-orders/{id}/reject` as the seller. Assert `Status` becomes `Cancelled`, an `OrderCancellation` is recorded with `AuctionOrderId`, a 100% wallet refund is completed for the buyer, and callback `status: "SellerRejected"` is sent to Auction Service.

**Acceptance Scenarios**:
1. **Given** a paid `AuctionOrder`, **When** the seller calls `POST /api/v1/auction-orders/{id}/reject` with a valid reason, **Then** Commerce updates `auctionOrder.Status = SubOrderStatus.Cancelled`, sets `CancelledAt = UtcNow`, creates `OrderCancellation` (`AuctionOrderId = auctionOrder.Id`, `ActorRole = Seller`, `WasPaid = true`), and triggers automated refund via `_walletClient.RefundOrderPaymentAsync`.
2. **Given** automated refund execution, **Then** Commerce records `OrderRefund` with `AuctionOrderId = auctionOrder.Id` and `Status = RefundStatus.Completed`.
3. **Given** successful cancellation and refund, **When** notifying Auction Service, **Then** Commerce calls callback with `status: "SellerRejected"` and `eventId`. If Auction Service experiences a transient network failure, Commerce logs the warning for retry but DOES NOT rollback the buyer's refund (fail-safe).
4. **Given** an auction order that is already shipped, delivered, or completed, **When** reject is attempted, **Then** Commerce rejects with HTTP 400 `INVALID_STATUS` and message `"Auction order cannot be rejected in its current status."`.

---

### User Story 6 - Automated 24h Payment Timeout Cancellation & "PaymentTimedOut" Callback (Priority: P1)

As a system background worker, I need to scan for pending auction orders exceeding their 24-hour payment deadline and cancel them cleanly with correct database foreign keys, notifying Auction Service to penalize or invite runner-ups.

**Why this priority**:
Cleans up abandoned winning bids and releases the auction state so Auction Service can engage runner-up bidders.

**Independent Test**:
Run `PaymentTimeoutWorker.ProcessExpiredOrdersAsync` against a test database with an expired `AuctionOrder`. Assert `Status` transitions to `Cancelled`, `OrderCancellation` has `AuctionOrderId` set (not `SubOrderId`), and callback `PaymentTimedOut` is sent.

**Acceptance Scenarios**:
1. **Given** an `AuctionOrder` with `Status == SubOrderStatus.PendingPayment` and `PaymentDeadlineAt < UtcNow`, **When** `PaymentTimeoutWorker` executes, **Then** the worker transitions `Status = SubOrderStatus.Cancelled`, sets `CancelledAt = UtcNow`, and inserts `OrderCancellation` with `AuctionOrderId = auctionOrder.Id` and `SubOrderId = null` (remediating the Foreign Key defect).
2. **Given** cancellation completes, **When** post-processing, **Then** Commerce dispatches callback `POST /api/v1/auctions/{auctionId}/settlement/payment-status` with `{ orderId, winnerId, status: "PaymentTimedOut", eventId }`.

---

### User Story 7 - Automated 24h Seller Response Timeout with Wallet Refund & "SellerExpired" Callback (Priority: P1)

As a system background worker, I need to scan for paid auction orders where the seller failed to accept within 24 hours (`SellerRespondDeadlineAt < UtcNow`), automatically cancel the order, refund 100% of buyer funds, and notify Auction Service.

**Why this priority**:
Protects buyers from unresponsive sellers by guaranteeing automated release of funds after 24 hours without customer service intervention.

**Independent Test**:
Execute worker against an unaccepted paid order past `SellerRespondDeadlineAt`. Assert order cancels, 100% wallet refund completes, and callback `status: "SellerExpired"` is dispatched.

**Acceptance Scenarios**:
1. **Given** an `AuctionOrder` with `Status == SubOrderStatus.Paid` and `SellerRespondDeadlineAt < UtcNow`, **When** `PaymentTimeoutWorker` executes, **Then** the worker transitions `Status = SubOrderStatus.Cancelled`, sets `CancelledAt = UtcNow`, inserts `OrderCancellation` with `ActorRole = System`, `ReasonCode = SellerRejected`, `ReasonNote = "Seller response deadline expired after 24 hours."`, and `WasPaid = true`.
2. **Given** cancellation, **Then** the worker triggers automated wallet refund for the buyer and creates `OrderRefund` with `AuctionOrderId`.
3. **Given** refund, **Then** the worker calls Auction Service callback with `status: "SellerExpired"` and `eventId`.

---

### User Story 8 - Buyer Delivery Confirmation & Seller Escrow Settlement (Priority: P2)

As an authenticated buyer who has received the physical package, I want to confirm receipt, so that the auction order marks as completed and the seller receives their payout from escrow.

**Why this priority**:
Completes the order lifecycle and releases seller earnings.

**Independent Test**:
Call `POST /api/v1/auction-orders/{id}/confirm-receipt` as the buyer when status is `Delivered`. Assert status becomes `Completed` and wallet settlement is triggered.

**Acceptance Scenarios**:
1. **Given** an authenticated buyer and an `AuctionOrder` with `Status == SubOrderStatus.Delivered` (or `Shipped`), **When** calling `POST /api/v1/auction-orders/{id}/confirm-receipt`, **Then** Commerce updates `Status = SubOrderStatus.Completed`, sets `CompletedAt = UtcNow`, and invokes `_walletClient.SettleSellerSubOrderAsync(auctionOrder.Id, auctionOrder.SellerId, auctionOrder.SellerReceiveAmount)`.
2. **Given** an unauthorized caller or invalid status, **Then** Commerce returns HTTP 403 or 400 accordingly.

---

### User Story 9 - Logistics Delivery Webhook Synchronization for Auction Orders (Priority: P2)

As an external logistics carrier / fulfillment service, I want to deliver shipment status updates to Commerce Service, and have the system correctly resolve and update auction orders when the tracking ID belongs to an auction purchase.

**Why this priority**:
Ensures carrier delivery progress updates (`Shipped`, `Delivered`) automatically transition auction orders to their next lifecycle step without 404 errors.

**Independent Test**:
Call `POST /api/v1/sub-orders/{subOrderId}/delivery-status` where `subOrderId` equals an `AuctionOrder.Id`. Assert HTTP 200 OK and `AuctionOrder.Status` updates to `Shipped` or `Delivered`.

**Acceptance Scenarios**:
1. **Given** a delivery webhook payload targeting an identifier, **When** Commerce does not find a match in `SubOrders`, **Then** Commerce falls back to search in `AuctionOrders`.
2. **Given** an `AuctionOrder` match, **When** status is `InTransit`/`Shipped` or `Delivered`, **Then** Commerce updates `Status` and timestamps (`ShippedAt` or `DeliveredAt`) accordingly.

---

### User Story 10 - Outbound Typed REST Client with Service Key Security (Priority: P1)

As a Commerce Service component, I need a dedicated typed HTTP client `IAuctionClient` / `AuctionClient` configured with `AUCTION_SERVICE_URL:8005` and `X-Internal-Service-Key` to communicate reliably with Auction Service.

**Why this priority**:
Provides standard, observable, retryable HTTP communication matching project constitution rules.

**Independent Test**:
Invoke `IAuctionClient.NotifyPaymentStatusAsync` and assert HTTP request targets `http://auction-service:8005/api/v1/auctions/{auctionId}/settlement/payment-status` with `X-Internal-Service-Key` header and correct payload.

**Acceptance Scenarios**:
1. **Given** `AUCTION_SERVICE_URL` in configuration, **When** `AuctionClient` sends a callback, **Then** `InternalServiceAuthHandler` attaches `X-Internal-Service-Key`.
2. **Given** a transient 5xx or network timeout response, **When** retrying, **Then** the client maintains the exact same `eventId` to ensure idempotency.

---

## 2. Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST accept settlement data via `POST /api/v1/auction-orders/settlement` and validate `X-Internal-Service-Key` authentication.
- **FR-002**: System MUST enforce idempotency on settlement ingestion using the composite pair `(AuctionId, WinnerId)`. If an active order for another winner exists, return HTTP 409 `AUCTION_ALREADY_SETTLED`. If an order for another winner is `Cancelled`, allow creating a new order.
- **FR-003**: System MUST generate a sequential, human-readable order number with prefix `AO-YYYYMMDD-XXXXX` for each auction order.
- **FR-004**: System MUST store `paymentDeadlineAt` exactly as provided by Auction Service, without applying regular order 15-minute checkout expiration.
- **FR-005**: System MUST permit the authenticated winner to submit shipping address via `PUT /api/v1/auction-orders/{id}/shipping`, calculate carrier shipping fees via Fulfillment Service, and finalize `TotalAmount = WinningAmount + ShippingFeeAmount`.
- **FR-006**: System MUST enforce strict deadline checking on payment: if `DateTimeOffset.UtcNow > PaymentDeadlineAt`, immediately reject payment with HTTP 400 `PAYMENT_DEADLINE_EXPIRED`.
- **FR-007**: System MUST process wallet payments via `IWalletClient.PayOrderAsync`. Upon success, transition `Status = Paid`, record `PaidAt`, calculate `SellerRespondDeadlineAt = PaidAt + 24h`, and immediately dispatch callback `status: "Paid"` with `paidAt` and `amount = WinningAmount` to Auction Service.
- **FR-008**: System MUST permit the authenticated seller to accept a paid auction order within `SellerRespondDeadlineAt` via `POST /api/v1/auction-orders/{id}/accept`, transition `Status = Confirmed`, create logistics shipment, and dispatch callback `status: "SellerAccepted"`.
- **FR-009**: System MUST permit the authenticated seller to reject a paid auction order via `POST /api/v1/auction-orders/{id}/reject`, transition `Status = Cancelled`, create `OrderCancellation`, trigger 100% automated wallet refund to the buyer via `IWalletClient.RefundOrderPaymentAsync`, and dispatch callback `status: "SellerRejected"`.
- **FR-010**: System MUST remediate `PaymentTimeoutWorker` by setting `OrderCancellation.AuctionOrderId = auctionOrder.Id` and `SubOrderId = null` when auto-cancelling expired auction orders.
- **FR-011**: System MUST scan every 60 seconds for auction orders with `Status == PendingPayment` and `PaymentDeadlineAt < UtcNow`, cancel them, and dispatch callback `status: "PaymentTimedOut"` to Auction Service.
- **FR-012**: System MUST scan every 60 seconds for auction orders with `Status == Paid` and `SellerRespondDeadlineAt < UtcNow`, cancel them, execute 100% automated wallet refund for the buyer, and dispatch callback `status: "SellerExpired"` to Auction Service.
- **FR-013**: System MUST ensure financial refund fail-safety: if outbound callback to Auction Service encounters a network error, the buyer's wallet refund and cancellation MUST NOT be rolled back.
- **FR-014**: System MUST support buyer delivery confirmation via `POST /api/v1/auction-orders/{id}/confirm-receipt`, transition `Status = Completed`, and release escrow payout to the seller via `IWalletClient.SettleSellerSubOrderAsync`.
- **FR-015**: System MUST extend the carrier delivery webhook `POST /api/v1/sub-orders/{subOrderId}/delivery-status` to fallback to `AuctionOrders` when `subOrderId` matches an auction order.

---

### Key Entities

- **AuctionOrder**: Root entity representing the winning outcome of an auction. Contains auction reference (`AuctionId`, `WinningBidId`), buyer/seller references (`BuyerId`, `SellerId`), product snapshot (`ProductId`, `SkuId`, `ProductNameSnapshot`), financial snapshot (`WinningAmount`, `ShippingFeeAmount`, `TotalAmount`, `SellerReceiveAmount`, `PlatformFeeAmount`), status (`SubOrderStatus`), timestamps (`PaymentDeadlineAt`, `SellerRespondDeadlineAt`, `PaidAt`, `SellerAcceptedAt`, `CancelledAt`, `CompletedAt`), and concurrency token (`RowVersion`).
- **OrderCancellation**: Audit record capturing cancellation details (`AuctionOrderId`, `ActorRole`, `ReasonCode`, `ReasonNote`, `WasPaid`).
- **OrderRefund**: Financial audit record tracking wallet reversal (`AuctionOrderId`, `Amount`, `Status`, `ReasonCode`).
- **OrderStatusHistory**: State machine tracking record capturing all transitions of the auction order.

---

## 3. Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of valid auction settlements generate an `AuctionOrder` with accurate snapshot amounts and deadline timestamps within 1 second.
- **SC-002**: 100% of duplicate settlement requests for the same `(AuctionId, WinnerId)` return identical order identifiers without creating duplicate records.
- **SC-003**: 100% of expired auction orders (unpaid after 24h, or unaccepted by seller after 24h) are detected and transitioned to `Cancelled` within 60 seconds of deadline expiry.
- **SC-004**: 100% of paid orders rejected by seller or expired due to seller inaction trigger an automated, idempotent wallet refund to the buyer without manual CSKH intervention.
- **SC-005**: 100% of auction callbacks (`Paid`, `SellerAccepted`, `SellerRejected`, `SellerExpired`, `PaymentTimedOut`) are dispatched with correct parameters and stable `eventId` for safe receiver de-duplication.
- **SC-006**: 0% regression on regular cart checkout, multi-vendor order placement, and existing unit tests (`dotnet build` succeeds with 0 errors and 0 warnings, `dotnet test` achieves 100% pass rate).

---

## 4. Assumptions

- System does not collect or manage auction deposits; buyer pays 100% of `TotalAmount = WinningAmount + ShippingFeeAmount` via Commerce.
- Auction Service operates at internal Docker container address `http://auction-service:8005`.
- Fulfillment Service operates at internal Docker container address `http://fulfillment-service:8006`.
- Communication between internal microservices uses shared secret key `X-Internal-Service-Key`.
- Platform fee percentage for auction orders is dynamically loaded from system settings (defaulting to 5%).
- Outbound network failures when notifying Auction Service will be logged and can be retried safely using the identical `eventId`.
