# Feature Specification: Order Financial Lifecycle, Safe Multi-Gateway Settlement & Voucher Extensibility

**Feature Branch**: `023-order-financial-lifecycle-and-voucher-extensibility`  
**Created**: 2026-11-15  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  
**Input**: User prompt: "Bạn hãy đọc toàn bộ src code cũng như các file của spekit. Rồi lập cho tôi các đặc tả thật chi tiết cho từng use case. Để triển khai speckit thực thi qui trình lifecycer thực hiện các feature này. Đảm bảo vấn đề mở rộng về sau khi tôi triển khai thực hiện feature Vouchers."

---

## 1. Executive Summary & Architectural Goals

`CommerceService` chịu trách nhiệm độc quyền về quản lý giỏ hàng, điều phối thanh toán và vòng đời tài chính của đơn hàng thương mại điện tử đa người bán (Multi-Vendor Marketplace). 

Đặc tả này chuẩn hóa toàn diện:
1. **Động cơ hạch toán tài chính (Financial Invariant Engine)**: Khép kín công thức tài chính $\text{TotalAmount} = \text{SellerReceiveAmount} + \text{PlatformFeeAmount} + \text{ShippingFeeAmount}$ trên từng đơn hàng con (`SubOrder`), hỗ trợ hoa hồng sàn linh hoạt và cho phép `PlatformFeeAmount < 0` khi Sàn trợ giá voucher kích cầu.
2. **Bảo vệ Ví hệ thống (System Escrow Protection & Safe Checkout)**: Đảm bảo dòng tiền từ Buyer được tạm giữ an toàn tại Ví hệ thống trong suốt thời gian vận chuyển. Chặn đứng lỗi tạo đơn treo (`PendingPayment`) khi Buyer không đủ số dư ví (`INSUFFICIENT_BALANCE`) hoặc khi cổng VNPay gặp lỗi kết nối.
3. **Triệt tiêu Race Condition giữa Timeout và Polling Worker (Zero-Loss Reconciliation)**: Ngăn chặn tuyệt đối việc `PaymentTimeoutWorker` hủy đơn nhầm khi khách hàng đã quét mã VNPay thành công sát giờ hết hạn (15 phút).
4. **Quyết toán giải ngân cho Người bán (Escrow Settlement to Seller Wallet)**: Tự động trích đúng `SellerReceiveAmount` từ Ví hệ thống sang ví cá nhân của Shop khi Buyer xác nhận nhận hàng, đồng thời giữ lại doanh thu ròng của Sàn.
5. **Hoàn tiền phân loại theo phương thức thanh toán (Method-Aware Refund)**: Chặn đứng lỗi thất thoát tiền quỹ khi hoàn tiền nhầm cho đơn COD chưa thanh toán; thực hiện bồi hoàn ví cá nhân cho đơn thanh toán ví hoặc VNPay; tự động giải phóng hạn mức voucher khi hủy đơn.
6. **Kiến trúc sẵn sàng mở rộng cho Feature Vouchers (Voucher Extensibility Architecture)**: Chuẩn hóa abstractions và cấu trúc phân bổ 3 tầng giảm giá (`ProductDiscountAmount`, `ShopDiscountAmount`, `SystemDiscountAmount`) để khi triển khai chi tiết tính năng Vouchers (Shop Voucher, Platform Voucher, Free Shipping Voucher) không phải thay đổi cấu trúc bảng hay luồng thanh toán cốt lõi.

---

## 2. User Scenarios & Testing *(mandatory)*

### User Story 1 - Multi-Tier Financial Invariant Engine & Fee Calculation (Priority: P1)

As a Finance Auditor and Marketplace Platform Owner, I want every sub-order to accurately calculate and persist all 8 financial dimensions with strict zero-sum balancing, so that seller payouts, platform commissions, shipping fees, and buyer payments are mathematically consistent and verifiable down to the penny.

**Why this priority**:
Financial integrity is the foundation of the marketplace. If order totals do not balance against seller payouts and platform fees, either sellers are underpaid, platform revenue is lost, or accounting reconciliation fails completely.

**Independent Test**:
Can be independently tested by executing `CalculateFinancials` on a `SubOrder` with varying item prices, shop discounts, system discounts, and platform markup percentages. Verify that:
1. `ItemsAmount == Sum(Quantity * UnitPrice)`.
2. `SellerReceiveAmount == ItemsAmount - ProductDiscountAmount - ShopDiscountAmount`.
3. `PlatformFeeAmount == PlatformMarkupAmount - SystemDiscountAmount`.
4. `TotalAmount == SellerReceiveAmount + PlatformFeeAmount + ShippingFeeAmount`.
5. An exception is thrown if the invariant is violated.

**Acceptance Scenarios**:
1. **Given** a sub-order with items totaling 1,000,000 VND, shop discount of 50,000 VND, platform markup of 5%, system voucher of 20,000 VND, and shipping fee of 30,000 VND, **When** financial calculation executes, **Then** `SellerReceiveAmount` is 950,000 VND, `PlatformMarkupAmount` is 47,500 VND, `PlatformFeeAmount` is 27,500 VND, and `TotalAmount` is 1,007,500 VND.
2. **Given** a high-value system promotional campaign where `SystemDiscountAmount` (80,000 VND) exceeds `PlatformMarkupAmount` (42,500 VND), **When** calculation executes, **Then** `PlatformFeeAmount` calculates as negative (-37,500 VND), `TotalAmount` reflects the buyer's subsidized payment, and the invariant holds true with zero decimal drift.
3. **Given** order placement in `OrderService.CreateOrderAsync`, **When** the sub-orders are persisted to PostgreSQL, **Then** all financial fields are populated using the domain calculation engine rather than hardcoded zeroes.

---

### User Story 2 - Safe Multi-Gateway Checkout & System Escrow Inflow (Priority: P1)

As a Buyer checking out items from multiple shops, I want my payment transaction to be verified atomically before my shopping cart is cleared, so that if my wallet balance is insufficient or the payment gateway is unavailable, my cart remains intact and no phantom unpaid orders are created.

**Why this priority**:
Currently, when a wallet payment fails due to insufficient balance or VNPay fails to generate a payment URL, the system removes cart items and leaves a dead `PendingPayment` order with no recovery path, causing immediate user frustration and abandoned sales.

**Independent Test**:
1. Simulate checkout with `PaymentMethod.Wallet` where buyer has insufficient funds. Assert HTTP 400 `INSUFFICIENT_WALLET_BALANCE` is returned, cart items are NOT deleted, and reserved stock is released.
2. Simulate checkout with `PaymentMethod.VnPay` where `InitiateDirectCheckoutAsync` fails. Assert HTTP 503 `PAYMENT_GATEWAY_UNAVAILABLE` is returned, cart items remain intact, and no order is committed.
3. Simulate successful checkout with `PaymentMethod.Wallet` or `PaymentMethod.VnPay`. Assert order is saved with `PendingPayment` (VNPay) or `Paid` (Wallet), full `TotalAmount` is routed to System Escrow, and cart items are cleanly purged.

**Acceptance Scenarios**:
1. **Given** an authenticated buyer with insufficient wallet balance, **When** they submit `CreateOrderAsync` with `PaymentMethod.Wallet`, **Then** the service fails fast, rolls back any inventory reservations, retains cart items, and returns HTTP 400 with error code `INSUFFICIENT_WALLET_BALANCE`.
2. **Given** a buyer selecting `PaymentMethod.VnPay`, **When** `CreateOrderAsync` runs, **Then** `InitiateDirectCheckoutAsync` is invoked *before* committing the order to the database. If successful, the valid `paymentUrl` and `transactionId` are saved; if it fails, inventory is released, the cart is preserved, and HTTP 503 is returned.
3. **Given** a successful wallet payment, **When** the debit succeeds, **Then** the full `TotalAmount` is transferred into the System Escrow Wallet, `order.Status` is set to `Paid`, and stock is permanently committed via `IFulfillmentClient.CommitStockAsync`.

---

### User Story 3 - Zero-Loss Payment Reconciliation & Timeout Deconfliction (Priority: P1)

As a Buyer who completed VNPay QR payment near the 15-minute deadline, I want the system to confirm my payment rather than cancelling my order, so that my money is not lost and my order is fulfilled as expected.

**Why this priority**:
Currently, `PaymentTimeoutWorker` cancels expired orders blindly based solely on `PaymentDeadlineAt < now` without checking `WalletService`. If a buyer pays at minute 14:45 and IPN arrives at 14:55, `PaymentTimeoutWorker` at minute 15:01 cancels the order and releases inventory, causing financial discrepancies and lost merchandise.

**Independent Test**:
Seed an expired order (`PaymentDeadlineAt = now - 1 minute`) in `PendingPayment` with `PaymentMethod.VnPay`. Mock `_walletClient.GetDirectCheckoutStatusAsync` to return `"Success"`. Run `PaymentTimeoutWorker.ProcessExpiredOrdersAsync`. Verify that:
1. The order is NOT cancelled.
2. `order.Status` transitions to `Paid`.
3. `so.Status` transitions to `Paid` with `PaidAt` populated.
4. Stock is committed via `CommitStockAsync` instead of released.

**Acceptance Scenarios**:
1. **Given** an expired order with `PaymentMethod.VnPay`, **When** `PaymentTimeoutWorker` processes it, **Then** it queries `_walletClient.GetDirectCheckoutStatusAsync(order.Id, order.BuyerId)`.
2. **Given** the query returns status `"Success"`, **When** reconciliation runs, **Then** the worker transitions the order and all sub-orders to `Paid`, commits warehouse stock, and logs an informational audit trail.
3. **Given** the query returns `"Failed"`, `"Pending"`, or `null`, **When** reconciliation runs, **Then** the worker safely transitions the order to `Cancelled`, releases warehouse inventory via `BatchReleaseStockAsync`, and releases any reserved voucher usage records.

---

### User Story 4 - Automated Seller Escrow Settlement to Seller Wallet (Priority: P1)

As a Seller whose sub-order was confirmed delivered by the buyer or completed by the platform, I want my net earnings (`SellerReceiveAmount`) to be automatically disbursed from the System Escrow Wallet into my personal seller wallet, so that I receive my payout promptly without manual invoicing.

**Why this priority**:
Timely seller settlement is required for multi-vendor retention and regulatory compliance. Automatic escrow release guarantees that only earned funds (excluding platform fees and carrier costs) are transferred to the seller.

**Independent Test**:
Call `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt` as the authenticated buyer for a `Delivered` sub-order. Verify that:
1. `subOrder.Status` transitions to `Completed` with `CompletedAt` set.
2. `_walletClient.SettleSellerSubOrderAsync` is invoked with `sellerId` and the exact `SellerReceiveAmount`.
3. The remaining balance (`PlatformFeeAmount + ShippingFeeAmount`) stays in the System Escrow Wallet.
4. `409 SETTLEMENT_CONFLICT` is handled cleanly without suppressing genuine conflict errors.

**Acceptance Scenarios**:
1. **Given** a sub-order in `Delivered` status, **When** the buyer confirms receipt, **Then** `subOrder.Status` becomes `Completed`, and an outbound S2S call `POST /api/v1/wallets/sub-orders/{subOrderId}/settle` transfers `SellerReceiveAmount` to the seller.
2. **Given** the payout is successful, **Then** the response reports `EscrowSettled: true` with `AmountSettled: SellerReceiveAmount`.
3. **Given** an identical settlement request is retried, **When** WalletService returns HTTP 200 (idempotent replay), **Then** CommerceService logs successful idempotency resolution.
4. **Given** WalletService returns `409 SETTLEMENT_CONFLICT` due to mismatched seller or amount, **When** processed, **Then** `WalletClient` reports `false` and flags the record for administrative investigation rather than falsely reporting success.

---

### User Story 5 - Payment-Method-Aware Sub-Order Refund & Voucher Reversion (Priority: P1)

As a Buyer whose order was cancelled before shipping, I want my payment to be refunded according to the payment method I used, and any applied vouchers to be restored to my account, so that I do not suffer financial loss or forfeit promotional discounts.

**Why this priority**:
Currently, `CancelOrderAsync` and `SellerRejectSubOrderAsync` call `_walletClient.RefundSubOrderPaymentAsync` whenever `wasPaid` is true, without checking `PaymentMethod`. This causes unearned platform funds to be credited to COD buyers who never paid in advance, causing direct financial leakage.

**Independent Test**:
1. Cancel a paid sub-order with `PaymentMethod.Cod`. Verify that NO call to `_walletClient.RefundSubOrderPaymentAsync` is made, and no wallet refund transaction is generated.
2. Cancel a paid sub-order with `PaymentMethod.Wallet`. Verify that `_walletClient.RefundSubOrderPaymentAsync` is invoked for `so.TotalAmount`, `OrderRefund.Status` becomes `Completed`, and applied vouchers in `DiscountCodeUsages` are marked as `Released`.
3. Cancel a paid sub-order with `PaymentMethod.VnPay`. Verify that the system executes store-credit refund to buyer wallet (or queues for gateway refund per platform policy) and releases inventory and vouchers.

**Acceptance Scenarios**:
1. **Given** a cancelled sub-order with `PaymentMethod.Cod`, **When** cancellation executes, **Then** the system transitions `so.Status = Cancelled`, creates `OrderCancellation`, releases stock, and strictly bypasses wallet refund calls.
2. **Given** a cancelled sub-order with `PaymentMethod.Wallet`, **When** cancellation executes, **Then** the system calls `_walletClient.RefundSubOrderPaymentAsync` to credit `so.TotalAmount` from System Escrow back to the buyer's wallet, records an `OrderRefund` with `Status = Completed`, and sets all associated `DiscountCodeUsages.Status = Released`.
3. **Given** an order cancellation that fails to communicate with WalletService, **When** the error occurs, **Then** `OrderRefund.Status` remains `Requested` with `FailureCode` populated, and an audit warning is recorded for reconciliation.

---

### User Story 6 - Voucher Extensibility Architecture (Priority: P2)

As a Software Architect and Backend Engineer, I want the order calculation and checkout pipeline to provide clear extension points and pure interfaces for voucher application, so that the future Vouchers feature can be introduced seamlessly without altering existing database schemas or core payment flows.

**Why this priority**:
Building the foundation for vouchers now prevents costly refactoring later. The database schema already contains `DiscountCodes`, `DiscountCodeScopes`, and `DiscountCodeUsages`. We must ensure the financial engine and quoting pipeline cleanly distinguish between Shop Vouchers, Platform Vouchers, and Free Shipping Vouchers.

**Independent Test**:
Verify that the `IVoucherDiscountCalculator` interface and calculation pipeline:
1. Accepts a list of voucher codes, cart items grouped by seller, and shipping quotes.
2. Correctly categorizes discounts into:
   - `ProductDiscountAmount` (item-level)
   - `ShopDiscountAmount` (seller-level, reduces seller receive amount)
   - `SystemDiscountAmount` (platform-level, reduces platform fee)
   - `ShippingDiscountAmount` (carrier subsidy, reduces shipping fee)
3. Produces a deterministic `VoucherApplicationResult` that feeds directly into `ShopOrderQuote` and `SubOrder.CalculateFinancials`.

**Acceptance Scenarios**:
1. **Given** a request with voucher codes, **When** `QuoteCheckoutAsync` runs, **Then** it passes the request through `IVoucherDiscountCalculator` to compute discount amounts for each shop.
2. **Given** a Shop Voucher, **When** applied, **Then** it reduces only the sub-total of the issuing shop and is tracked in `SubOrder.ShopDiscountAmount`.
3. **Given** a Platform Voucher, **When** applied, **Then** it distributes proportionally across participating shops, is tracked in `SubOrder.SystemDiscountAmount`, and does not reduce any seller's `SellerReceiveAmount`.
4. **Given** checkout completion, **When** the order is saved, **Then** each applied voucher is recorded in `DiscountCodeUsages` with `Status = Reserved` (transitioning to `Applied` on payment, or `Released` on cancellation/timeout).

---

## 3. Edge Cases & Boundary Conditions

- **EC-001 (Zero-Value Payout)**: A sub-order where shop discounts equal the entire items amount (`SellerReceiveAmount == 0`).  
  *Mitigation*: The settlement engine skips wallet credit if amount is zero, logs an informational event, and marks the sub-order as `Completed`.
- **EC-002 (Negative Platform Fee Exceeds System Escrow Reserves)**: Heavy system vouchers reduce `PlatformFeeAmount` to a large negative number.  
  *Mitigation*: The platform marketing reserve must guarantee funds. The invariant mathematically balances: the platform covers the difference when releasing `SellerReceiveAmount`.
- **EC-003 (Carrier Fee Waiver / Free Shipping Subsidy)**: A promotional code covers 100% of shipping fees.  
  *Mitigation*: `ShippingFeeAmount` reflects the net buyer cost, while carrier payable is logged in logistics reconciliation.
- **EC-004 (Partial Order Cancellation)**: A buyer cancels SubOrder 1 but keeps SubOrder 2.  
  *Mitigation*: Only SubOrder 1 triggers stock release and refund. SubOrder 2 continues along its normal delivery and settlement lifecycle.
- **EC-005 (Post-Payment VNPay Return Tampering)**: Buyer alters return URL query parameters (e.g. `vnp_ResponseCode=00`).  
  *Mitigation*: CommerceService completely ignores browser redirects for payment confirmation and relies exclusively on `GetDirectCheckoutStatusAsync` verified by WalletService.

---

## 4. Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every `SubOrder` MUST calculate and persist `ItemsAmount`, `ProductDiscountAmount`, `ShopDiscountAmount`, `PlatformMarkupAmount`, `SystemDiscountAmount`, `ShippingFeeAmount`, `SellerReceiveAmount`, `PlatformFeeAmount`, and `TotalAmount` satisfying:
  $$\text{TotalAmount} = \text{SellerReceiveAmount} + \text{PlatformFeeAmount} + \text{ShippingFeeAmount}$$
- **FR-002**: `OrderService.CreateOrderAsync` MUST evaluate the buyer's payment ability before clearing the cart:
  - For `PaymentMethod.Wallet`: Execute debit via `_walletClient.PayOrderAsync`. If balance is insufficient, fail with HTTP 400 `INSUFFICIENT_WALLET_BALANCE`, retain cart items, and release stock.
  - For `PaymentMethod.VnPay`: Execute `_walletClient.InitiateDirectCheckoutAsync`. If gateway URL generation fails, fail with HTTP 503 `PAYMENT_GATEWAY_UNAVAILABLE`, retain cart items, and release stock.
- **FR-003**: `PaymentTimeoutWorker` MUST query `_walletClient.GetDirectCheckoutStatusAsync` for any expired `PendingPayment` VNPay order before cancelling:
  - If status is `"Success"`, update order and sub-orders to `Paid`, record `WalletTransactionId`, and commit stock.
  - If status is NOT `"Success"`, cancel order, release warehouse inventory, and release any reserved vouchers.
- **FR-004**: `BuyerConfirmSubOrderReceiptAsync` and `CompleteOrderAsync` MUST disburse `so.SellerReceiveAmount` to the seller's wallet via `_walletClient.SettleSellerSubOrderAsync`, retaining `PlatformFeeAmount + ShippingFeeAmount` within the System Escrow Wallet.
- **FR-005**: `WalletClient.SettleSellerSubOrderAsync` MUST NOT treat HTTP 409 `SETTLEMENT_CONFLICT` as a successful response. It must return `false` to prevent masking parameter mismatches.
- **FR-006**: `CancelOrderAsync` and `SellerRejectSubOrderAsync` MUST verify the order's payment method:
  - For `PaymentMethod.Cod`: Do NOT invoke wallet refund; only release stock and mark cancelled.
  - For `PaymentMethod.Wallet`: Call `_walletClient.RefundSubOrderPaymentAsync` for `so.TotalAmount`, set `OrderRefund.Status = Completed`, and release applied vouchers.
  - For `PaymentMethod.VnPay`: Process refund in accordance with platform policy (credit buyer wallet or record manual gateway refund) without unearned cashout.
- **FR-007**: When any order cancellation or expiration occurs, all associated `DiscountCodeUsages` records with `Status == Reserved` MUST be transitioned to `Status = Released`, and the corresponding `DiscountCode.ReservedCount` decremented.
- **FR-008**: The system MUST expose `IVoucherDiscountCalculator` abstraction supporting multi-tenant discount separation (`ShopDiscountAmount` vs `SystemDiscountAmount`), enabling drop-in implementation of the future Vouchers feature.

### Non-Functional Requirements

- **NFR-001 (Zero Repository Pattern)**: All database access MUST use `ICommerceDbContext` / `AppDbContext` directly in Application services.
- **NFR-002 (Inward-Only)**: Zero external references outside `CommerceService`.
- **NFR-003 (Error Standards)**: 100% English Sentence-case for all exception and error messages; `SCREAMING_SNAKE_CASE` for all error codes.
- **NFR-004 (Performance)**: Financial invariant calculation for a 50-item multi-shop cart MUST execute in under 15ms in-memory.
- **NFR-005 (Compilation & Test)**: Solution MUST compile with 0 errors and 0 warnings on `Release` configuration, and pass 100% of the unit test suite.

---

## 5. Verification Checklist

- [ ] `CalculateFinancials` domain method enforces invariant and handles both positive and negative `PlatformFeeAmount`.
- [ ] `CreateOrderAsync` fails fast on `INSUFFICIENT_BALANCE` without deleting cart items.
- [ ] `CreateOrderAsync` validates `InitiateDirectCheckoutAsync` before DB commit for VNPay orders.
- [ ] `PaymentTimeoutWorker` queries `GetDirectCheckoutStatusAsync` before cancelling VNPay orders.
- [ ] `BuyerConfirmSubOrderReceiptAsync` settles exact `SellerReceiveAmount` to seller.
- [ ] `WalletClient` does not return `true` on HTTP 409 `SETTLEMENT_CONFLICT`.
- [ ] `CancelOrderAsync` and `SellerRejectSubOrderAsync` bypass wallet refund for COD orders.
- [ ] `DiscountCodeUsages` records are released when orders cancel or expire.
- [ ] `IVoucherDiscountCalculator` abstraction is defined and integrated into `QuoteCheckoutAsync`.
- [ ] `dotnet build Commerce.sln -c Release` compiles with 0 errors and 0 warnings.
- [ ] `dotnet test Commerce.sln -c Release --no-build` passes with 100% success rate.
