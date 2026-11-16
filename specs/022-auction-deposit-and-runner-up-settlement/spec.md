# Feature Specification: Auction Deposit Ingestion, Runner-Up Settlement & Split Reversal

**Feature Branch**: `022-auction-deposit-and-runner-up-settlement`  
**Created**: 2026-11-15  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port `8003`, Database `commerce_db:5435`)  
**Input**: User description: "Triển khai toàn diện nâng cấp phân hệ Đơn hàng Đấu giá (AuctionOrder) theo Hợp đồng tích hợp Đặt cọc đấu giá v0.4 (auction-deposit-integration) giữa CommerceService và AuctionService: (1) Tiếp nhận settlement tạo AuctionOrder hỗ trợ khấu trừ tiền cọc (`depositAmount`), hỗ trợ nguồn đơn Winner và Người về nhì (`source = WINNER | SECOND_CHANCE`); (2) Công thức tài chính chuẩn xác: RemainingAmount = WinningAmount - DepositAmount, TotalAmount = RemainingAmount + ShippingFeeAmount, SellerReceiveAmount = WinningAmount * 95%, PlatformFeeAmount = WinningAmount * 5%; (3) Kiểm tra tính hợp lệ số tiền nghiêm ngặt (422 DEPOSIT_EXCEEDS_WINNING_AMOUNT, 422 INVALID_AMOUNT, 409 SETTLEMENT_CONFLICT); (4) Buyer chỉ thanh toán khoản còn lại TotalAmount qua ví Nexus; (5) Mở rộng Outbound Callback IAuctionClient gửi trường `orderSource` cho tất cả các sự kiện Paid, SellerAccepted, SellerRejected, PaymentTimedOut, SellerExpired; (6) Phân định cơ chế hoàn tiền độc lập khi Seller từ chối hoặc hết hạn duyệt: Commerce hoàn phần Buyer đã trả cho order (TotalAmount), Auction Service tự động hoàn cọc qua Wallet W5; (7) Bổ sung depositAmount, remainingAmount, orderSource vào API xem chi tiết đơn hàng; (8) Tuân thủ 100% tiếng Anh Sentence-case cho error messages, mã lỗi SCREAMING_SNAKE_CASE, Inward-only, Clean Architecture, 0 compile errors và 100% test pass."

---

## 1. User Scenarios & Testing *(mandatory)*

### User Story 1 - Won Auction Settlement Ingestion with Deposit Deduction (Priority: P1)

As an external Auction Service, I need to push won auction settlement data including the held deposit amount to Commerce Service to automatically generate an auction order in `PendingPayment` status, so that the buyer is credited for their held deposit and only billed the remaining balance.

**Why this priority**:
In the v0.4 deposit architecture, winning buyers have already locked an auction deposit in their Nexus Wallet balance (`HeldBalance`). When creating an order from auction victory, Commerce must recognize this deposit, compute the net remaining merchandise amount, and preserve the total order value without double-charging or omitting the deposit.

**Independent Test**:
Call `POST /api/v1/auction-orders/settlement` with payload containing `depositAmount: 100000` and `winningAmount: 1000000`. Assert an `AuctionOrder` record is created in PostgreSQL with `DepositAmount = 100000`, `RemainingAmount = 900000`, `WinningAmount = 1000000`, `OrderSource = Winner`, `SellerReceiveAmount = 950000`, `PlatformFeeAmount = 50000`, and `Status = SubOrderStatus.PendingPayment`.

**Acceptance Scenarios**:
1. **Given** a valid won auction payload from Auction Service with `winningAmount = 1000000`, `depositAmount = 100000`, and `source = "WINNER"`, **When** `POST /api/v1/auction-orders/settlement` is called, **Then** Commerce creates an `AuctionOrder` with `DepositAmount = 100000`, `RemainingAmount = 900000`, `Status = SubOrderStatus.PendingPayment`, and returns HTTP 201 Created with `{ orderId, orderNo, auctionId, paymentDeadlineAt, status: "PendingPayment" }`.
2. **Given** a settlement payload where `depositAmount` is omitted or null, **When** processed by Commerce, **Then** Commerce defaults `DepositAmount = 0` and `RemainingAmount = WinningAmount`.
3. **Given** a settlement payload where `source` is omitted or null, **When** processed by Commerce, **Then** Commerce defaults `OrderSource = AuctionOrderSource.Winner`.
4. **Given** an existing `AuctionOrder` with matching `(AuctionId, OrderSource, BuyerId)`, **When** an identical settlement request is repeated, **Then** Commerce returns HTTP 200 OK with the existing order details without creating duplicate records (Idempotency).

---

### User Story 2 - Runner-Up (Second Chance) Settlement Ingestion (Priority: P1)

As an external Auction Service, when the primary winning bidder fails to pay within 24 hours, I need to create a new auction order for the runner-up bidder (`SECOND_CHANCE`) with zero deposit, so that the runner-up can purchase the product under an independent order lifecycle.

**Why this priority**:
Auction business logic permits runner-up purchase when the primary winner defaults. The runner-up's initial deposit was already released by Auction Service at auction conclusion; hence their order must start with `depositAmount = 0` and be assigned an independent 24-hour payment deadline.

**Independent Test**:
Given an auction where the original winner's order is `Status == SubOrderStatus.Cancelled`, call `POST /api/v1/auction-orders/settlement` with `source: "SECOND_CHANCE"`, `winnerId: <RunnerUpGuid>`, and `depositAmount: 0`. Assert a new `AuctionOrder` is created with `OrderSource = SecondChance`, `DepositAmount = 0`, `RemainingAmount = WinningAmount`, and HTTP 201 Created is returned.

**Acceptance Scenarios**:
1. **Given** an existing `AuctionOrder` for `AuctionId` in `Cancelled` status (original winner defaulted), **When** a settlement request arrives with `source = "SECOND_CHANCE"`, a new `winnerId`, and `depositAmount = 0`, **Then** Commerce creates a new `AuctionOrder` with `OrderSource = AuctionOrderSource.SecondChance`, `DepositAmount = 0`, `RemainingAmount = WinningAmount`, and returns HTTP 201 Created.
2. **Given** an existing `AuctionOrder` for `AuctionId` where `Status != SubOrderStatus.Cancelled` (the original order is still pending payment or confirmed), **When** a settlement arrives with `source = "SECOND_CHANCE"`, **Then** Commerce rejects with HTTP 409 Conflict and error code `SETTLEMENT_CONFLICT` with message `"An active auction order already exists for this auction."`.
3. **Given** an existing `SECOND_CHANCE` order for `(AuctionId, WinnerId)`, **When** repeated with conflicting amounts, **Then** Commerce rejects with HTTP 409 Conflict and error code `SETTLEMENT_CONFLICT`.

---

### User Story 3 - Financial & Amount Boundary Validation (Priority: P1)

As a robust Commerce Service, I want to strictly validate all incoming monetary values and settlement parameters, so that impossible financial conditions are rejected before any database state mutation occurs.

**Why this priority**:
Prevents negative balances, over-deductions, or corrupt financial calculations that could destabilize wallet escrow or merchant payouts.

**Independent Test**:
Call `POST /api/v1/auction-orders/settlement` with `depositAmount > winningAmount` or with negative values. Assert the service immediately returns HTTP 422 Unprocessable Entity with standardized error codes without modifying database records.

**Acceptance Scenarios**:
1. **Given** a settlement payload where `depositAmount > winningAmount` (e.g., deposit 600,000 VND on a 500,000 VND winning bid), **When** `POST /api/v1/auction-orders/settlement` is called, **Then** Commerce returns HTTP 422 Unprocessable Entity with error code `DEPOSIT_EXCEEDS_WINNING_AMOUNT` and message `"Deposit amount cannot exceed winning amount."`.
2. **Given** a settlement payload where `winningAmount <= 0` or `depositAmount < 0`, **When** settlement is called, **Then** Commerce returns HTTP 422 Unprocessable Entity with error code `INVALID_AMOUNT` and message `"Winning amount must be greater than zero and deposit amount cannot be negative."`.
3. **Given** a settlement payload where `source == "SECOND_CHANCE"` but `depositAmount > 0`, **When** settlement is called, **Then** Commerce returns HTTP 422 Unprocessable Entity with error code `INVALID_RUNNER_UP_DEPOSIT` and message `"Runner-up second chance order must have zero deposit."`.

---

### User Story 4 - Winner Shipping Submission & Net TotalAmount Calculation (Priority: P1)

As an authenticated buyer who won an auction, I want to submit my shipping destination address, so that the carrier shipping fee is calculated and added strictly to my net remaining balance (`RemainingAmount`) rather than the gross winning amount.

**Why this priority**:
Under the deposit model, the buyer has already committed their deposit. The total cash amount they must transfer via their wallet is strictly:
$$\text{TotalAmount} = \text{RemainingAmount} + \text{ShippingFeeAmount}$$
Charging against `WinningAmount + ShippingFeeAmount` would erroneously force the buyer to pay their deposit twice.

**Independent Test**:
Create an `AuctionOrder` with `WinningAmount = 1000000` and `DepositAmount = 100000` (`RemainingAmount = 900000`). Call `PUT /api/v1/auction-orders/{id}/shipping` with valid address returning a 30,000 VND shipping fee. Assert `TotalAmount` is persisted as `930000` (not `1030000`).

**Acceptance Scenarios**:
1. **Given** an `AuctionOrder` with `RemainingAmount = 900000` and `Status == PendingPayment`, **When** the buyer calls `PUT /api/v1/auction-orders/{id}/shipping` with valid address parameters, **Then** Commerce computes carrier shipping fee via Fulfillment Service (e.g., 30,000 VND), updates `ShippingFeeAmount = 30000`, sets `TotalAmount = RemainingAmount + ShippingFeeAmount` (930,000 VND), and returns HTTP 200 OK with the updated breakdown.
2. **Given** an `AuctionOrder` past `PaymentDeadlineAt`, **When** shipping submission is attempted, **Then** Commerce rejects with HTTP 400 `PAYMENT_DEADLINE_EXPIRED` and message `"Auction payment deadline has expired."`.
3. **Given** a caller who is not the `BuyerId` of the order, **When** shipping submission is attempted, **Then** Commerce returns HTTP 403 `FORBIDDEN`.

---

### User Story 5 - Net Remaining Wallet Payment & Callback with OrderSource (Priority: P1)

As an authenticated buyer, I want to pay for the auction order using my digital wallet, so that only the net balance `TotalAmount` is debited, the order moves to `Paid`, and Auction Service receives a callback specifying `orderSource` to capture the held deposit.

**Why this priority**:
Completes the buyer's payment obligation for the merchandise and logistics, notifies the seller, and instructs Auction Service via callback (`orderSource = WINNER`) to execute Wallet W2 capture (`ORDER_PAYMENT`) to transfer the held deposit into the order's escrow.

**Independent Test**:
Call `POST /api/v1/auction-orders/{id}/pay` for an order with `TotalAmount = 930000`. Assert wallet is debited exactly 930,000 VND, `Status` becomes `Paid`, and an outbound callback to Auction Service includes `{ orderId, winnerId, status: "Paid", amount: winningAmount, paidAt, eventId, orderSource: "WINNER" }`.

**Acceptance Scenarios**:
1. **Given** an `AuctionOrder` with `TotalAmount = 930000` and `Status == PendingPayment` within its payment deadline, **When** the buyer calls `POST /api/v1/auction-orders/{id}/pay`, **Then** Commerce debits the buyer's wallet for 930,000 VND via `IWalletClient.PayOrderAsync`.
2. **Given** successful wallet payment, **Then** Commerce sets `auctionOrder.Status = SubOrderStatus.Paid`, `PaidAt = UtcNow`, `SellerRespondDeadlineAt = UtcNow.AddHours(24)`, and dispatches an outbound HTTP POST to Auction Service (`POST /api/v1/auctions/{auctionId}/settlement/payment-status`) containing `{ orderId, winnerId, status: "Paid", amount: winningAmount, paidAt, eventId, orderSource: "WINNER" }`.
3. **Given** a paid order whose `OrderSource == AuctionOrderSource.SecondChance`, **When** the callback is dispatched, **Then** Commerce sends `orderSource: "SECOND_CHANCE"`.

---

### User Story 6 - Seller Order Acceptance & Callback with OrderSource (Priority: P2)

As a seller who listed the auctioned product, I want to review and accept the paid auction order within my 24-hour response deadline, so that logistics shipment is created and Auction Service is notified with `orderSource`.

**Why this priority**:
Seller confirmation initiates package shipment and signals Auction Service to permanently commit the product as sold in Catalog Service.

**Independent Test**:
Call `POST /api/v1/auction-orders/{id}/accept` as the authenticated seller. Assert `Status` becomes `Confirmed`, shipment is created in Fulfillment Service, and callback `status: "SellerAccepted"` with `orderSource` is dispatched to Auction Service.

**Acceptance Scenarios**:
1. **Given** an authenticated seller matching `auctionOrder.SellerId`, **When** they call `POST /api/v1/auction-orders/{id}/accept` while `Status == SubOrderStatus.Paid` and within deadline, **Then** Commerce updates `auctionOrder.Status = SubOrderStatus.Confirmed`, sets `SellerAcceptedAt = UtcNow`, creates a shipment via `IFulfillmentClient.CreateShipmentAsync`, and dispatches callback with `{ orderId, winnerId, status: "SellerAccepted", orderSource }`.
2. **Given** a paid order past `SellerRespondDeadlineAt`, **When** the seller attempts to accept, **Then** Commerce rejects with HTTP 400 `SELLER_RESPOND_TIMEOUT` and message `"Seller response deadline has expired. Order is awaiting cancellation."`.

---

### User Story 7 - Seller Order Rejection with Independent Reversal & OrderSource Callback (Priority: P1)

As an authenticated seller unable to fulfill an order, I want to reject the order, so that Commerce immediately refunds 100% of the amount the buyer paid to Commerce (`TotalAmount`), records seller penalty, and dispatches a callback with `orderSource` so Auction Service can independently refund the captured deposit via Wallet W5.

**Why this priority**:
Ensures strict independence of financial refunds: Commerce refunds merchandise + shipping paid through Commerce, while Auction Service refunds the captured deposit via Wallet W5. Commerce MUST NOT refund the deposit, preventing double-refunding to the buyer.

**Independent Test**:
Call `POST /api/v1/auction-orders/{id}/reject` as the seller on a paid order (`TotalAmount = 930000`, `DepositAmount = 100000`). Assert Commerce refunds 930,000 VND to the buyer (not 1,030,000 VND), records `OrderCancellation` and `OrderRefund`, applies penalty on the seller, and sends callback `status: "SellerRejected"`, `orderSource: "WINNER"` to Auction Service.

**Acceptance Scenarios**:
1. **Given** a paid `AuctionOrder` with `TotalAmount = 930000` and `DepositAmount = 100000`, **When** the seller calls `POST /api/v1/auction-orders/{id}/reject` with a reason note, **Then** Commerce updates `auctionOrder.Status = SubOrderStatus.Cancelled`, sets `CancelledAt = UtcNow`, and invokes `_walletClient.RefundOrderPaymentAsync` for `refundAmount = auctionOrder.TotalAmount` (930,000 VND).
2. **Given** the successful wallet refund, **Then** Commerce creates `OrderCancellation` (`ReasonCode = SellerRejected`, `WasPaid = true`) and `OrderRefund` (`Amount = 930000`, `Status = Completed`).
3. **Given** the cancellation of a paid order, **Then** Commerce invokes `_userClient.CreatePenaltyAsync` on `auctionOrder.SellerId` with `ViolationType = "SellerRejected"` and `PolicyClauseCode = "POLICY-AUC-SELLER-REJECT"`.
4. **Given** the completed rejection, **Then** Commerce dispatches outbound callback to Auction Service with `{ orderId, winnerId, status: "SellerRejected", orderSource }`. Transient failure of this callback does not rollback the buyer's refund (fail-safe).

---

### User Story 8 - Background Worker 24h Payment Timeout & OrderSource Callback (Priority: P1)

As a background worker in Commerce Service, I want to periodically scan for auction orders whose payment deadline has expired, so that unpaid orders are cancelled, buyer penalties are recorded, and Auction Service is notified with `orderSource` to forfeit the deposit and invite the runner-up.

**Why this priority**:
Automates inventory cleanup when winning bidders default. Emitting `orderSource` allows Auction Service to distinguish between a primary winner defaulting (which triggers deposit forfeiture via Wallet W2 and runner-up invitation) versus a runner-up defaulting (which triggers Catalog unlock without deposit forfeiture).

**Independent Test**:
Insert an `AuctionOrder` with `Status == SubOrderStatus.PendingPayment` and `PaymentDeadlineAt < UtcNow`. Trigger `PaymentTimeoutWorker.ProcessExpiredOrdersAsync`. Assert order is updated to `Cancelled`, `OrderCancellation` is recorded with `AuctionOrderId`, penalty is applied to buyer, and callback is dispatched to Auction Service with `status: "PaymentTimedOut"` and `orderSource`.

**Acceptance Scenarios**:
1. **Given** an `AuctionOrder` with `Status == PendingPayment` and `PaymentDeadlineAt < UtcNow`, **When** `PaymentTimeoutWorker` executes its scan cycle, **Then** Commerce updates `auctionOrder.Status = SubOrderStatus.Cancelled`, sets `CancelledAt = UtcNow`, and adds `OrderCancellation` (`AuctionOrderId = auctionOrder.Id`, `SubOrderId = null`, `ActorRole = System`, `ReasonCode = PaymentTimeout`).
2. **Given** cancellation of the expired auction order, **Then** Commerce invokes `_userClient.CreatePenaltyAsync` on `auctionOrder.BuyerId` with `ViolationType = "PaymentTimeout"`, `PolicyClauseCode = "POLICY-AUC-BUYER-TIMEOUT"` (deducting 5 points).
3. **Given** the cancellation, **Then** Commerce dispatches callback to Auction Service (`POST /api/v1/auctions/{auctionId}/settlement/payment-status`) with `{ orderId, winnerId, status: "PaymentTimedOut", orderSource: auctionOrder.OrderSource.ToString().ToUpperInvariant() }`.
4. **Given** the expired auction order, **Then** Commerce DOES NOT call Catalog Service to unlock product inventory (inventory unlocking is strictly managed by Auction Service).

---

### User Story 9 - Background Worker 24h Seller Response Timeout with Independent Refund (Priority: P1)

As a background worker in Commerce Service, I want to scan for paid auction orders where the seller failed to accept or reject within 24 hours, so that the order is auto-cancelled, the buyer's paid amount (`TotalAmount`) is auto-refunded, seller penalties are applied, and Auction Service is notified to refund the deposit.

**Why this priority**:
Protects buyers from abandoned orders by unfulfilled sellers, ensuring prompt return of paid funds.

**Independent Test**:
Insert an `AuctionOrder` with `Status == SubOrderStatus.Paid` and `SellerRespondDeadlineAt < UtcNow`. Trigger `PaymentTimeoutWorker.ProcessExpiredOrdersAsync`. Assert order is cancelled, buyer is refunded `TotalAmount`, seller penalty is called, and callback `status: "SellerExpired"` with `orderSource` is dispatched to Auction Service.

**Acceptance Scenarios**:
1. **Given** an `AuctionOrder` with `Status == Paid` and `SellerRespondDeadlineAt < UtcNow`, **When** `PaymentTimeoutWorker` scans, **Then** Commerce sets `auctionOrder.Status = SubOrderStatus.Cancelled`, `CancelledAt = UtcNow`, and triggers `_walletClient.RefundOrderPaymentAsync` for `auctionOrder.TotalAmount`.
2. **Given** refund execution, **Then** Commerce records `OrderCancellation` (`ReasonCode = SellerRejected`, `WasPaid = true`) and `OrderRefund` (`Amount = TotalAmount`).
3. **Given** seller default, **Then** Commerce calls `_userClient.CreatePenaltyAsync` for `auctionOrder.SellerId` with `ViolationType = "SellerResponseTimeout"`, `PolicyClauseCode = "POLICY-AUC-SELLER-TIMEOUT"`.
4. **Given** completion, **Then** Commerce sends callback to Auction Service with `{ orderId, winnerId, status: "SellerExpired", orderSource: auctionOrder.OrderSource.ToString().ToUpperInvariant() }`.

---

### User Story 10 - Auction Order Detail Transparency with Deposit & Remaining Breakdown (Priority: P2)

As an authenticated buyer, seller, or administrator, I want to view the full details of an auction order including `depositAmount`, `remainingAmount`, and `orderSource`, so that I have complete transparency regarding how much was held as deposit and how much was paid upon order checkout.

**Why this priority**:
Provides clear financial auditability on buyer receipts, seller invoices, and customer service dashboards.

**Independent Test**:
Call `GET /api/v1/auction-orders/{id}` as the buyer or seller. Assert the response JSON contains `depositAmount`, `remainingAmount`, and `orderSource` along with standard order fields.

**Acceptance Scenarios**:
1. **Given** an `AuctionOrder` with `DepositAmount = 100000`, `RemainingAmount = 900000`, `WinningAmount = 1000000`, `ShippingFeeAmount = 30000`, `TotalAmount = 930000`, and `OrderSource = Winner`, **When** the buyer calls `GET /api/v1/auction-orders/{id}`, **Then** Commerce returns HTTP 200 OK with `depositAmount = 100000`, `remainingAmount = 900000`, `orderSource = "Winner"`, `winningAmount = 1000000`, `shippingFeeAmount = 30000`, and `totalAmount = 930000`.
2. **Given** an unauthenticated caller or a caller whose ID matches neither `BuyerId` nor `SellerId`, **When** `GET /api/v1/auction-orders/{id}` is called, **Then** Commerce returns HTTP 403 `FORBIDDEN`.

---

### Edge Cases

- **Zero Deposit Order**: When `depositAmount == 0` (e.g. for runner-up or auctions with no deposit required), `RemainingAmount` equals `WinningAmount` and `TotalAmount` equals `WinningAmount + ShippingFeeAmount`. All formulas remain continuous without division or null issues.
- **Deposit Equals Winning Amount**: When `depositAmount == winningAmount`, `RemainingAmount` is `0`. The buyer only pays `TotalAmount = ShippingFeeAmount`. If shipping fee is 0, the order transitions directly to paid upon shipping submission or wallet zero-pay.
- **Callback Network Timeout / 5xx from Auction Service**: All outbound callback invocations are wrapped in fail-safe try-catch blocks. If Auction Service is down, Commerce logs a structured warning with `EventId` and does NOT rollback buyer wallet payment or refund.
- **Missing Optional Fields**: If `skuAttributes`, `productImageUrl`, or `contactPhone` are omitted, Commerce applies sensible fallbacks (`"{}"`, `null`, `shippingAddress.PhoneNumber`).
- **Concurrent Settlement Requests**: Optimistic concurrency or database uniqueness constraint on `(AuctionId, OrderSource, BuyerId)` prevents duplicate order rows from being inserted under race conditions.

---

## 2. Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST accept `depositAmount` (decimal) and `source` (string) in `AuctionSettlementRequest` at `POST /api/v1/auction-orders/settlement`.
- **FR-002**: System MUST default `depositAmount` to `0` and `source` to `"WINNER"` if either field is null or omitted.
- **FR-003**: System MUST compute and persist `RemainingAmount = WinningAmount - DepositAmount` upon settlement creation.
- **FR-004**: System MUST validate that `depositAmount <= winningAmount`; if violated, reject with HTTP 422 `DEPOSIT_EXCEEDS_WINNING_AMOUNT`.
- **FR-005**: System MUST validate that `winningAmount > 0` and `depositAmount >= 0`; if violated, reject with HTTP 422 `INVALID_AMOUNT`.
- **FR-006**: System MUST validate that if `source == "SECOND_CHANCE"`, `depositAmount` MUST equal `0`; if violated, reject with HTTP 422 `INVALID_RUNNER_UP_DEPOSIT`.
- **FR-007**: System MUST support creating a `SECOND_CHANCE` order for an auction whose prior `WINNER` order has `Status == Cancelled`.
- **FR-008**: System MUST reject settlement with HTTP 409 `SETTLEMENT_CONFLICT` if an active (non-cancelled) order already exists for the auction under another buyer.
- **FR-009**: System MUST calculate `TotalAmount = RemainingAmount + ShippingFeeAmount` upon buyer submission of shipping address.
- **FR-010**: System MUST debit the buyer's wallet strictly for `TotalAmount` upon payment execution.
- **FR-011**: System MUST include `orderSource` in all outbound callback payloads sent to Auction Service (`POST /api/v1/auctions/{auctionId}/settlement/payment-status`).
- **FR-012**: System MUST refund only `TotalAmount` (the amount paid to Commerce) to the buyer's wallet when an order is rejected or cancelled after payment; System MUST NOT refund `DepositAmount`.
- **FR-013**: System MUST NOT unlock products in Catalog Service when an auction order cancels on payment timeout (Catalog unlock is managed by Auction Service).
- **FR-014**: System MUST return `depositAmount`, `remainingAmount`, and `orderSource` in `AuctionOrderDetailResponse` at `GET /api/v1/auction-orders/{id}`.
- **FR-015**: System MUST enforce 100% English sentence-case for all error messages and SCREAMING_SNAKE_CASE for all error codes.

---

### Key Entities

- **`AuctionOrder`**: Represents a legally binding commerce order originating from a finalized auction.
  - Attributes: `Id` (Guid), `OrderNo` (string), `AuctionId` (Guid), `BuyerId` (Guid), `SellerId` (Guid), `WinningBidId` (Guid), `ProductId` (Guid), `SkuId` (Guid?), `ProductNameSnapshot` (string), `ImageUrlSnapshot` (string?), `SkuAttributesSnapshot` (string JSON), `WinningAmount` (decimal), `DepositAmount` (decimal - NEW), `RemainingAmount` (decimal - NEW), `OrderSource` (AuctionOrderSource enum - NEW), `ShippingAddress` (string JSON?), `ContactPhone` (string?), `ShippingFeeAmount` (decimal?), `TotalAmount` (decimal?), `SellerReceiveAmount` (decimal), `PlatformFeeAmount` (decimal), `Status` (SubOrderStatus enum), `PaymentMethod` (PaymentMethod enum), `WalletTransactionId` (Guid?), `PaymentDeadlineAt` (DateTimeOffset), `SellerRespondDeadlineAt` (DateTimeOffset?), `PaidAt` (DateTimeOffset?), `SellerAcceptedAt` (DateTimeOffset?), `ShippedAt` (DateTimeOffset?), `DeliveredAt` (DateTimeOffset?), `CompletedAt` (DateTimeOffset?), `CancelledAt` (DateTimeOffset?), `CarrierCode` (string?), `TrackingCode` (string?), `LabelPrintUrl` (string?), `RowVersion` (long).
- **`AuctionOrderSource` (Enum)**:
  - `Winner = 1` (`"WINNER"`): Primary winning bidder of the auction.
  - `SecondChance = 2` (`"SECOND_CHANCE"`): Runner-up bidder invited after the primary winner defaulted.

---

## 3. Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of won auction settlements with deposit deductions create `AuctionOrder` entities where `WinningAmount == RemainingAmount + DepositAmount` exactly.
- **SC-002**: 100% of runner-up settlements (`SECOND_CHANCE`) successfully generate independent orders when previous winner orders are cancelled, with zero deposit deducted.
- **SC-003**: In 100% of order cancellations or seller rejections of paid auction orders, Commerce refunds exactly `TotalAmount` to the buyer's wallet, with 0 VND duplicate deposit refunded.
- **SC-004**: 100% of outbound callbacks dispatched to Auction Service contain the `orderSource` property matching the order's true origin (`WINNER` or `SECOND_CHANCE`).
- **SC-005**: All financial validation rejections return HTTP 422 with standard RFC 9110 problem details and exact expected error codes.
- **SC-006**: The entire test suite (`dotnet test Commerce.sln -c Release`) passes with 100% success rate and zero compiler warnings or errors (`dotnet build Commerce.sln -c Release`).

---

## 4. Assumptions

- **Deposit Lifecycle Ownership**: The hold, release, and capture of deposits are strictly owned by Auction Service coordinating with Wallet Service (APIs W1, W2, W3, W5). Commerce Service only consumes `DepositAmount` for billing offsets and emits status callbacks.
- **Escrow Aggregation**: When Auction Service captures the deposit via Wallet W2 with `purpose: "ORDER_PAYMENT"`, Wallet Service aggregates the captured deposit into the escrow associated with the same `orderId`.
- **Independent Runner-Up Notification**: Auction Service decides when to invite runner-up bidders based on the `PaymentTimedOut` callback from Commerce Service.
- **Catalog Management**: Catalog product locking and unlocking are handled exclusively by Auction Service; Commerce Service never directly invokes Catalog lock/unlock endpoints for auction items.
