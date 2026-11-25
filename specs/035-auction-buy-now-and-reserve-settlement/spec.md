# Feature Specification: Auction Buy Now & Reserve Price Settlement Integration

**Feature Branch**: `035-auction-buy-now-and-reserve-settlement`  
**Created**: 2026-11-20  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port `8003`, Database `commerce_db:5435`)  
**Input**: User description: "Nâng cấp phân hệ Đơn hàng Đấu giá (AuctionOrder) hỗ trợ tính năng Mua ngay (Buy Now) và đồng bộ nghiệp vụ Giá sàn (Reserve Price) theo Hợp đồng tích hợp v0.4 giữa CommerceService và AuctionService: (1) Tiếp nhận Settlement Mua ngay (source = BUY_NOW), mở rộng POST /api/v1/auction-orders/settlement, enum AuctionOrderSource.BuyNow = 3, remainingAmount = 0 VND, Seller nhận 95%, Platform nhận 5%, trạng thái khởi tạo PendingPayment chờ Buyer nhập địa chỉ ship GHN; (2) Luồng chọn địa chỉ nhận hàng và thanh toán: TotalAmount = RemainingAmount (0) + ShippingFeeAmount = ShippingFeeAmount, Buyer thanh toán phí ship qua ví Nexus, chuyển Paid và gửi callback Paid kèm orderSource = BUY_NOW; (3) Quá hạn thanh toán 24h: PaymentTimeoutWorker chuyển Cancelled, gửi callback PaymentTimedOut kèm orderSource = BUY_NOW, không tự phạt điểm uy tín Buyer; (4) Đồng bộ nghiệp vụ Giá sàn: khi không đạt giá sàn Auction giữ AwaitingSellerApproval (12h) không đẩy Commerce, khi Seller đồng ý bán đẩy source = WINNER; (5) Chuẩn hóa định dạng chuỗi Callback ToWireValue() cho BuyNow => BUY_NOW, sửa lỗi ToUpperInvariant() sinh BUYNOW; (6) Tiêu chuẩn chất lượng: English sentence-case, SCREAMING_SNAKE_CASE, Inward-only, 0 compile errors và 100% test pass."

---

## 1. User Scenarios & Testing *(mandatory)*

### User Story 1 - Buy Now Settlement Ingestion with 100% Held Deposit (Priority: P1)

As an external Auction Service, when a buyer triggers "Buy Now" at the fixed buy-now price, I need to transmit the settlement payload with `source: "BUY_NOW"` and `depositAmount` equal to `winningAmount` to Commerce Service, so that an auction order is initialized in `PendingPayment` status with `RemainingAmount = 0 VND`, waiting for the buyer to provide their delivery address and pay the carrier shipping fee.

**Why this priority**:
In the Buy Now auction model, the Auction Service locks 100% of the merchandise amount (`BuyNowPrice`) in the buyer's Nexus Wallet (`HeldBalance`). Commerce Service must recognize this 100% held deposit, avoid double-charging the merchandise value, set `RemainingAmount = 0 VND`, and calculate the platform fee (5%) and net seller payout (95%).

**Independent Test**:
Call `POST /api/v1/auction-orders/settlement` with payload `{ sessionId, source: "BUY_NOW", winnerId, sellerId, itemId, winningAmount: 5000000.0, depositAmount: 5000000.0, paymentDeadlineAt: "<now+24h>" }`. Assert an `AuctionOrder` is created in PostgreSQL with `DepositAmount = 5000000.0`, `RemainingAmount = 0.0`, `WinningAmount = 5000000.0`, `OrderSource = AuctionOrderSource.BuyNow`, `PlatformFeeAmount = 250000.0`, `SellerReceiveAmount = 4750000.0`, `Status = SubOrderStatus.PendingPayment`, `ShippingFeeAmount = null`, and `TotalAmount = null`.

**Acceptance Scenarios**:
1. **Given** a valid Buy Now settlement payload from Auction Service with `source: "BUY_NOW"` and `winningAmount == depositAmount` (e.g. 5,000,000 VND), **When** `POST /api/v1/auction-orders/settlement` is called, **Then** Commerce creates an `AuctionOrder` record with `OrderSource = AuctionOrderSource.BuyNow`, `DepositAmount = 5000000.0`, `RemainingAmount = 0.0`, `Status = SubOrderStatus.PendingPayment`, and returns HTTP 201 Created with `{ orderId, orderNo, auctionId, paymentDeadlineAt, status: "PendingPayment" }`.
2. **Given** an incoming settlement payload where `source: "BUY_NOW"` and `depositAmount < winningAmount`, **When** processed by Commerce, **Then** Commerce accepts the valid amounts and correctly computes `RemainingAmount = WinningAmount - DepositAmount` without throwing validation errors.
3. **Given** a repeated settlement call with identical `(AuctionId, OrderSource, BuyerId)` for a Buy Now order, **When** `POST /api/v1/auction-orders/settlement` is called again, **Then** Commerce returns HTTP 200 OK with the existing order details idempotently without creating duplicate records.
4. **Given** a settlement payload with unsupported source string (e.g., `source: "UNKNOWN"`), **When** processed by Commerce, **Then** Commerce rejects with HTTP 400 Bad Request and error code `VAL_INVALID_SETTLEMENT` with message `"Invalid order source."`.

---

### User Story 2 - Winner Shipping Destination Submission & Carrier Fee Allocation (Priority: P1)

As an authenticated buyer who secured an item via "Buy Now", I want to submit my shipping destination address within 24 hours, so that the carrier shipping fee is calculated via GHN and added to my zero merchandise balance, setting `TotalAmount` strictly equal to the shipping fee.

**Why this priority**:
Because merchandise value is 100% covered by the held deposit, the buyer's net payment obligation for the physical order is solely the logistics shipping fee:
$$\text{TotalAmount} = \text{RemainingAmount} (0\text{ VND}) + \text{ShippingFeeAmount} = \text{ShippingFeeAmount}$$
Charging any additional amount would result in severe financial discrepancy and overcharging.

**Independent Test**:
Given an `AuctionOrder` with `RemainingAmount = 0` and `Status == PendingPayment`, call `PUT /api/v1/auction-orders/{id}/shipping` with a valid address returning a 35,000 VND shipping fee from GHN. Assert `ShippingFeeAmount` is set to `35000` and `TotalAmount` is persisted as `35000`.

**Acceptance Scenarios**:
1. **Given** a Buy Now `AuctionOrder` with `RemainingAmount = 0` and `Status == PendingPayment`, **When** the buyer submits a valid address via `PUT /api/v1/auction-orders/{id}/shipping`, **Then** Commerce calculates carrier shipping fee (e.g. 35,000 VND) via Fulfillment Service, updates `ShippingFeeAmount = 35000`, sets `TotalAmount = 35000`, and returns HTTP 200 OK with the detailed price breakdown.
2. **Given** a Buy Now order past its 24-hour `PaymentDeadlineAt`, **When** the buyer attempts to submit shipping address, **Then** Commerce rejects with HTTP 400 Bad Request and error code `PAYMENT_DEADLINE_EXPIRED` with message `"Auction payment deadline has expired."`.
3. **Given** a user who is not the `BuyerId` of the Buy Now order, **When** attempting to submit shipping address, **Then** Commerce returns HTTP 403 Forbidden.

---

### User Story 3 - Net Shipping Wallet Payment & Outbound Paid Callback Dispatch (Priority: P1)

As an authenticated buyer of a Buy Now order, I want to pay the logistics shipping fee using my Nexus Wallet, so that only the shipping fee (`TotalAmount`) is debited, the order status transitions to `Paid`, and Auction Service receives an outbound callback with `orderSource: "BUY_NOW"` instructing it to capture the 100% held deposit into Escrow.

**Why this priority**:
Completes the buyer's logistics obligation, commits the order for seller fulfillment, and notifies Auction Service to capture the held deposit and finalize auction settlement.

**Independent Test**:
Call `POST /api/v1/auction-orders/{id}/pay` for a Buy Now order with `TotalAmount = 35000`. Assert the buyer's Nexus Wallet is debited exactly 35,000 VND via `IWalletClient`, `Status` becomes `Paid`, and an outbound HTTP POST callback is dispatched to Auction Service containing `status: "Paid"`, `orderSource: "BUY_NOW"`, `amount: 5000000`, and `occurredAt`.

**Acceptance Scenarios**:
1. **Given** a Buy Now `AuctionOrder` with `TotalAmount = 35000` and `Status == PendingPayment`, **When** the buyer calls `POST /api/v1/auction-orders/{id}/pay`, **Then** Commerce debits 35,000 VND from the buyer's wallet, sets `Status = SubOrderStatus.Paid`, records `PaidAt = UtcNow`, sets `SellerRespondDeadlineAt = UtcNow.AddHours(24)`, and returns HTTP 200 OK.
2. **Given** successful payment of a Buy Now order, **When** Commerce dispatches the outbound callback to `POST /api/v1/auctions/{auctionId}/settlement/payment-status`, **Then** the payload includes `{ orderId, winnerId, status: "Paid", amount: winningAmount, paidAt, eventId, occurredAt, orderSource: "BUY_NOW" }`.
3. **Given** insufficient wallet balance for the shipping fee, **When** the buyer calls pay, **Then** Commerce fails gracefully with HTTP 400 `PAYMENT_FAILED` without altering order status.

---

### User Story 4 - 24-Hour Payment Timeout Cancellation & Dispute-Free Notification (Priority: P1)

As the Commerce background monitoring system, when a Buy Now order exceeds its 24-hour payment deadline without shipping fee payment, I need to cancel the order and send an outbound callback `PaymentTimedOut` with `orderSource: "BUY_NOW"` to Auction Service without invoking user reputation penalties, allowing Auction Service to handle deposit forfeiture and remaining unhold.

**Why this priority**:
Prevents orphaned pending orders. Ensures clean responsibility boundaries: Auction Service manages deposit forfeiture and user reputation penalties (API 20 `auction-violations`). Commerce must not penalize the buyer to prevent double-penalization.

**Independent Test**:
Create a Buy Now `AuctionOrder` with `PaymentDeadlineAt = UtcNow.AddMinutes(-5)` and `Status = PendingPayment`. Execute `PaymentTimeoutWorker`. Assert the order status changes to `SubOrderStatus.Cancelled`, `CancelledReason = "Payment deadline expired"`, an outbound callback is dispatched with `status: "PaymentTimedOut"` and `orderSource: "BUY_NOW"`, and no call is made to UserService for buyer penalty.

**Acceptance Scenarios**:
1. **Given** a Buy Now `AuctionOrder` with expired `PaymentDeadlineAt` and `Status == PendingPayment`, **When** `PaymentTimeoutWorker` executes its sweep loop, **Then** Commerce updates order `Status = SubOrderStatus.Cancelled`, persists `CancelledAt` and `CancelledReason = "Payment deadline expired"`.
2. **Given** the order cancellation in step 1, **When** dispatching outbound callback to Auction Service, **Then** Commerce sends `{ orderId, winnerId, status: "PaymentTimedOut", amount: winningAmount, eventId, occurredAt, orderSource: "BUY_NOW" }`.
3. **Given** the payment timeout cancellation, **Then** Commerce does NOT call `IUserClient` or `UserService` to penalize the buyer.

---

### User Story 5 - Architectural Reserve Price Settlement Ingestion Parity (Priority: P2)

As a Commerce Service engineer, I want the system architecture to handle Reserve Price settlements through standard `source: "WINNER"` channels without introducing branching logic or custom state machines, matching the upstream Auction Service contract.

**Why this priority**:
When an auction does not meet the seller's reserve price, Auction Service holds the session in `AwaitingSellerApproval` (12h window) and does NOT push settlement to Commerce. If the seller accepts the below-reserve bid, Auction pushes a normal `source: "WINNER"` settlement. Treating this identically to standard auctions guarantees zero unnecessary complexity.

**Independent Test**:
Verify through architectural review and test suite that `source: "WINNER"` settlements handle any winning bid amount, deduct held deposits appropriately, and require zero conditional branching for reserve-priced auctions.

**Acceptance Scenarios**:
1. **Given** an auction that finished below reserve price where the seller approved the sale, **When** Auction Service pushes settlement with `source: "WINNER"`, **Then** Commerce processes it under standard winner rules without requiring any reserve-price-specific flags.
2. **Given** an auction that finished below reserve price where the seller rejected or timed out, **Then** Auction Service never calls Commerce Service, leaving zero trace in `commerce_db`.

---

### User Story 6 - Strict Outbound Callback Wire Value Standardization (Priority: P2)

As an integration engineer, I want all outbound callbacks and worker notifications across the auction order lifecycle to serialize `OrderSource` using `.ToWireValue()`, so that string representations are strictly snake_case (`"BUY_NOW"`, `"SECOND_CHANCE"`, `"WINNER"`) and never accidentally serialize to concatenated strings (`"BUYNOW"`, `"SECONDCHANCE"`).

**Why this priority**:
Using `.ToString().ToUpperInvariant()` on C# enum values produces `"BUYNOW"` and `"SECONDCHANCE"` (missing underscores), violating the agreed RFC contract with Auction Service and causing downstream webhook parsing failures.

**Independent Test**:
Inspect all callback dispatches in `AuctionOrderService` (`SellerAcceptAsync`, `SellerRejectAsync`, `ReceiveSettlementAsync`) and `PaymentTimeoutWorker` (Loop 1, Loop 2, Loop 3). Assert every `OrderSource` serialization calls `.ToWireValue()`.

**Acceptance Scenarios**:
1. **Given** an `AuctionOrder` with `OrderSource == AuctionOrderSource.BuyNow`, **When** `auctionOrder.OrderSource.ToWireValue()` is called, **Then** the resulting string is strictly `"BUY_NOW"`.
2. **Given** an `AuctionOrder` with `OrderSource == AuctionOrderSource.SecondChance`, **When** `auctionOrder.OrderSource.ToWireValue()` is called, **Then** the resulting string is strictly `"SECOND_CHANCE"`.
3. **Given** an `AuctionOrder` with `OrderSource == AuctionOrderSource.Winner`, **When** `auctionOrder.OrderSource.ToWireValue()` is called, **Then** the resulting string is strictly `"WINNER"`.
4. **Given** a seller rejection callback dispatched for a Buy Now or Second Chance order, **When** the payload is serialized, **Then** the `orderSource` field in the JSON string is `"BUY_NOW"` or `"SECOND_CHANCE"` respectively.

---

### Edge Cases

- **EC-001**: What happens if Auction Service sends `winningAmount == 0` or negative numbers?  
  *Commerce rejects with HTTP 422 `INVALID_AMOUNT`.*
- **EC-002**: What happens if Auction Service sends `depositAmount > winningAmount` on a Buy Now settlement?  
  *Commerce rejects with HTTP 422 `DEPOSIT_EXCEEDS_WINNING_AMOUNT`.*
- **EC-003**: What happens if a buyer submits shipping address multiple times before paying?  
  *Commerce recalculates the shipping fee for the new address and updates `ShippingFeeAmount` and `TotalAmount` accordingly.*
- **EC-004**: What happens if the buyer attempts to pay before submitting a shipping address?  
  *Commerce rejects with HTTP 400 `SHIPPING_ADDRESS_REQUIRED` because `TotalAmount` is null.*
- **EC-005**: What happens if the seller rejects or fails to confirm a Buy Now order after it was paid?  
  *Commerce marks order `SellerRejected` or `SellerExpired`, refunds the shipping fee (`TotalAmount`) via Wallet W3, and dispatches callback with `orderSource: "BUY_NOW"`. Auction Service receives callback and unholds/refunds merchandise deposit.*

---

## 2. Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST expand `AuctionOrderSource` domain enum to include `BuyNow = 3`.
- **FR-002**: System MUST implement `AuctionOrderSourceExtensions.ToWireValue()` to map `AuctionOrderSource.BuyNow` to `"BUY_NOW"`.
- **FR-003**: System MUST accept `"BUY_NOW"` in `POST /api/v1/auction-orders/settlement` in addition to `"WINNER"` and `"SECOND_CHANCE"`.
- **FR-004**: System MUST map `source: "BUY_NOW"` to `AuctionOrderSource.BuyNow` and persist it to PostgreSQL column `order_source`.
- **FR-005**: System MUST compute `RemainingAmount = Math.Max(0m, WinningAmount - DepositAmount)` which evaluates to `0 VND` when `DepositAmount == WinningAmount`.
- **FR-006**: System MUST compute `PlatformFeeAmount = Math.Round(WinningAmount * 0.05m, 2)` and `SellerReceiveAmount = WinningAmount - PlatformFeeAmount` for Buy Now orders.
- **FR-007**: System MUST initialize Buy Now orders in `SubOrderStatus.PendingPayment` with `ShippingFeeAmount = null` and `TotalAmount = null`.
- **FR-008**: System MUST compute `TotalAmount = RemainingAmount + ShippingFeeAmount` when buyer selects shipping address, yielding strictly the carrier shipping fee for Buy Now orders.
- **FR-009**: System MUST debit only `TotalAmount` from buyer's Nexus Wallet when `POST /api/v1/auction-orders/{id}/pay` is called.
- **FR-010**: System MUST include `orderSource: "BUY_NOW"` and `occurredAt` in all outbound callbacks (`Paid`, `PaymentTimedOut`, `SellerAccepted`, `SellerRejected`, `SellerExpired`) dispatched to Auction Service.
- **FR-011**: System MUST ensure `PaymentTimeoutWorker` cancels expired Buy Now orders after 24 hours without calling `UserService` to penalize the buyer.
- **FR-012**: System MUST replace all instances of `.ToString().ToUpperInvariant()` with `.ToWireValue()` for `OrderSource` serialization across `AuctionOrderService` and `PaymentTimeoutWorker`.

---

### Key Entities

- **AuctionOrder**: Domain aggregate entity representing an order spawned from auction settlement. Key attributes: `Id`, `AuctionId`, `OrderSource` (`Winner`, `SecondChance`, `BuyNow`), `BuyerId`, `SellerId`, `WinningAmount`, `DepositAmount`, `RemainingAmount`, `ShippingFeeAmount`, `TotalAmount`, `PlatformFeeAmount`, `SellerReceiveAmount`, `Status`, `PaymentDeadlineAt`, `PaidAt`.
- **AuctionOrderSource**: Domain enum representing the origin of the auction settlement: `Winner = 1`, `SecondChance = 2`, `BuyNow = 3`.
- **AuctionSettlementRequest**: Application DTO representing inbound settlement payload from Auction Service.
- **AuctionPaymentCallbackRequest**: Outbound DTO sent to Auction Service reporting order status transitions (`Paid`, `PaymentTimedOut`, `SellerAccepted`, `SellerRejected`, `SellerExpired`) containing `OrderSource`.

---

## 3. Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of valid Buy Now settlement requests (`source = "BUY_NOW"`, `winningAmount == depositAmount`) create an `AuctionOrder` with `RemainingAmount = 0 VND` and return HTTP 201 within 200ms.
- **SC-002**: 100% of Buy Now payment flows debit only the carrier shipping fee (`TotalAmount`) from the buyer's wallet, with 0 VND duplicate charges for merchandise.
- **SC-003**: 100% of outbound callbacks dispatched to Auction Service serialize `orderSource` as `"BUY_NOW"`, `"SECOND_CHANCE"`, or `"WINNER"` with 0 formatting anomalies (no `"BUYNOW"` or `"SECONDCHANCE"`).
- **SC-004**: 0 duplicate reputation penalty calls executed against buyers during 24-hour payment timeout cancellation.
- **SC-005**: 100% automated test pass rate with 0 compilation errors across `Commerce.sln`.

---

## 4. Assumptions

- **A-001**: Auction Service has already validated that the buyer's wallet has sufficient funds to lock 100% of `BuyNowPrice` before dispatching settlement to Commerce.
- **A-002**: Auction Service handles unlocking or refunding the held merchandise deposit if the order is cancelled, rejected by seller, or timed out.
- **A-003**: Reserve price evaluation and seller 12-hour approval are handled entirely within Auction Service boundaries; Commerce Service is only invoked if the sale is authorized.
- **A-004**: Carrier shipping fee calculation is powered by Fulfillment Service / GHN integration and operates fail-closed.
