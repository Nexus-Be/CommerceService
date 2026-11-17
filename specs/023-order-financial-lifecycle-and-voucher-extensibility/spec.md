# Feature Specification: Order Financial Lifecycle, Staff-Managed Platform Fee & Voucher Extensibility (VNPay Decommissioned)

**Feature Branch**: `023-order-financial-lifecycle-and-voucher-extensibility`  
**Created**: 2026-11-15  
**Updated**: 2026-11-16  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  
**Input**: User prompt: "Bạn hãy hoàn thiện lại file specs/023-order-financial-lifecycle-and-voucher-extensibility/spec.md phí sàn chỉ staff có quyền điều chỉnh. Và đóng code liên quan đến VNPay dự án tôi không dùng VNPay nữa."

---

## 1. Executive Summary & Architectural Goals

`CommerceService` chịu trách nhiệm độc quyền về quản lý giỏ hàng, điều phối thanh toán và vòng đời tài chính của đơn hàng thương mại điện tử đa người bán (Multi-Vendor Marketplace). 

Đặc tả này chuẩn hóa toàn diện 6 trụ cột nghiệp vụ:
1. **Phí sàn do Nhân viên (Staff) quản trị độc quyền**:
   - Tỷ lệ phần trăm phí sàn (`PlatformMarkupPercent`) **hoàn toàn tùy biến được (Configurable)** và được bảo vệ nghiêm ngặt: **CHỈ CÓ TÀI KHOẢN VỚI ROLE `staff` (HOẶC `admin`) MỚI CÓ QUYỀN ĐIỀU CHỈNH** qua endpoint quản trị chuyên biệt (`PUT/PATCH /api/v1/admin/platform-fee`).
   - Mọi nỗ lực thay đổi tỷ lệ phí sàn từ Người mua (`buyer`) hoặc Người bán (`seller`) đều bị từ chối với HTTP 403 `FORBIDDEN`.
2. **Khai tử & Đóng toàn bộ mã nguồn liên quan đến VNPay**:
   - Dự án chính thức bãi bỏ VNPay. Loại bỏ hoàn toàn `VnPayPaymentPollingWorker`, các hàm direct checkout trong `IWalletClient`, `WalletClient` và các nhánh logic xử lý VNPay.
   - Bề mặt thanh toán thu gọn về **2 phương thức cốt lõi**:
     - `PaymentMethod.Wallet (0)`: Thanh toán bằng Ví điện tử cá nhân (trừ tiền Buyer $\rightarrow$ tạm giữ tại Ví Hệ Thống $\rightarrow$ giải ngân cho Shop khi nhận hàng).
     - `PaymentMethod.Cod (2)`: Thanh toán tiền mặt khi nhận hàng (Shipper thu tiền $\rightarrow$ đối soát vào Ví Hệ Thống $\rightarrow$ giải ngân cho Shop khi nhận hàng).
   - Nếu client gửi `PaymentMethod.VnPay`, hệ thống lập tức từ chối với HTTP 400 `UNSUPPORTED_PAYMENT_METHOD`.
3. **Động cơ Hạch toán Tài chính Bất biến (Financial Invariant Engine)**:
   - Khép kín đẳng thức kế toán trên từng đơn con ([`SubOrder.cs`](file:///d:/CommerceService/Commerce.Domain/Entities/SubOrder.cs)):
     $$\boxed{\text{TotalAmount} = \text{SellerReceiveAmount} + \text{PlatformFeeAmount} + \text{ShippingFeeAmount}}$$
   - Hỗ trợ đầy đủ trường hợp `PlatformFeeAmount < 0` khi Sàn trợ giá voucher kích cầu lớn hơn tiền hoa hồng thu được.
4. **Bảo vệ Ví Hệ Thống (System Escrow Protection & Safe Checkout)**:
   - Toàn bộ dòng tiền thanh toán trực tuyến được bảo lãnh tại Ví Hệ Thống trong suốt quá trình vận chuyển.
   - Fail-closed khi thanh toán ví không đủ số dư: Trả về HTTP 400 `INSUFFICIENT_WALLET_BALANCE`, giữ nguyên giỏ hàng, không tạo đơn treo, giải phóng hàng tạm giữ.
5. **Quyết toán giải ngân & Hoàn tiền chuẩn hóa theo phương thức**:
   - Khi hoàn tất đơn: Tự động trích đúng `SellerReceiveAmount` từ Ví Hệ Thống sang ví Shop; phần chênh lệch (`PlatformFeeAmount + ShippingFeeAmount`) tự động nằm lại Ví Hệ Thống làm doanh thu sàn và chi phí logistics.
   - Khi hủy đơn: Đơn COD tuyệt đối không hoàn tiền ví; đơn Wallet hoàn tiền tự động; voucher đã áp dụng tự động được bồi hoàn lượt sử dụng (`DiscountCodeUsage.Status = Released`).
6. **Kiến trúc Sẵn sàng Mở rộng cho Feature Vouchers (Voucher Extensibility)**:
   - Phân định rõ 3 tầng giảm giá: `ProductDiscountAmount` (giá sản phẩm), `ShopDiscountAmount` (Shop tự chịu, giảm doanh thu Shop) và `SystemDiscountAmount` (Sàn tài trợ, giảm phí sàn).
   - Chuẩn bị sẵn abstraction `IVoucherDiscountCalculator` để khi triển khai chi tiết tính năng Vouchers không phải thay đổi cơ sở dữ liệu hay luồng thanh toán cốt lõi.

---

## 2. User Scenarios & Testing *(mandatory)*

### User Story 1 - Multi-Tier Financial Invariant Engine & Fee Calculation (Priority: P1)

As a Finance Auditor and Marketplace Platform Owner, I want every sub-order to calculate and persist all 8 financial dimensions with zero-sum balancing, so that seller payouts, platform fees, shipping fees, and buyer payments are mathematically consistent and verifiable down to the penny.

**Why this priority**:
Financial integrity is the foundation of the marketplace. If order totals do not balance against seller payouts and platform fees, either sellers are underpaid, platform revenue is lost, or accounting reconciliation fails completely.

**Independent Test**:
Can be independently tested by executing `CalculateFinancials` on a `SubOrder` with varying item prices, shop discounts, system discounts, and staff-configured platform markup percentages. Verify that:
1. `ItemsAmount == Sum(Quantity * UnitPrice)`.
2. `SellerReceiveAmount == ItemsAmount - ProductDiscountAmount - ShopDiscountAmount`.
3. `PlatformFeeAmount == PlatformMarkupAmount - SystemDiscountAmount`.
4. `TotalAmount == SellerReceiveAmount + PlatformFeeAmount + ShippingFeeAmount`.
5. An exception is thrown if the invariant is violated.

**Acceptance Scenarios**:
1. **Given** a sub-order with items totaling 1,000,000 VND, shop discount of 50,000 VND, staff-configured platform markup of 5%, system voucher of 20,000 VND, and shipping fee of 30,000 VND, **When** financial calculation executes, **Then** `SellerReceiveAmount` is 950,000 VND, `PlatformMarkupAmount` is 47,500 VND, `PlatformFeeAmount` is 27,500 VND, and `TotalAmount` is 1,007,500 VND.
2. **Given** a high-value system promotional campaign where `SystemDiscountAmount` (80,000 VND) exceeds `PlatformMarkupAmount` (42,500 VND), **When** calculation executes, **Then** `PlatformFeeAmount` calculates as negative (-37,500 VND), `TotalAmount` reflects the buyer's subsidized payment, and the invariant holds true with zero decimal drift.
3. **Given** order placement in `OrderService.CreateOrderAsync`, **When** the sub-orders are persisted to PostgreSQL, **Then** all financial fields are populated using the domain calculation engine rather than hardcoded zeroes.

---

### User Story 2 - Staff-Governed Platform Fee Rate Management (Priority: P1)

As a Marketplace Staff member or System Administrator, I want to view and adjust the platform markup percentage via an authenticated administrative REST endpoint, so that our organization can adjust fee policies without code redeployments, while unauthorized users (buyers and sellers) are strictly barred from altering fee rates.

**Why this priority**:
Platform fees are a strategic business lever. Only authenticated internal operations staff should control monetization rates. Hardcoded fees require engineering deployments, while exposing fee configuration to regular users would be a severe security flaw.

**Independent Test**:
1. Call `PUT /api/v1/admin/platform-fee` with role `buyer` or `seller`. Verify the server rejects with HTTP 403 `FORBIDDEN` and message `"Access denied: Only staff members can adjust platform fee rates."`.
2. Call `PUT /api/v1/admin/platform-fee` with role `staff` providing `{ "defaultPercent": 5.0 }`. Verify HTTP 200 returns the updated rate.
3. Create a subsequent order quote as a buyer. Verify the calculation uses the newly configured 5.0% rate.

**Acceptance Scenarios**:
1. **Given** an unauthenticated request or an authenticated user with role `buyer` or `seller`, **When** they call `GET` or `PUT /api/v1/admin/platform-fee`, **Then** the request is rejected with HTTP 403 `FORBIDDEN`.
2. **Given** an authenticated user with role `staff` or `admin`, **When** they call `PUT /api/v1/admin/platform-fee` with a valid rate between 0.0% and 100.0%, **Then** the system updates the active platform markup rate and returns HTTP 200 with the new setting.
3. **Given** a staff user submitting an invalid fee rate (e.g. -5% or 150%), **When** validation executes, **Then** the request is rejected with HTTP 400 `VAL_INVALID_INPUT` and message `"Platform fee rate must be between 0% and 100%."`.

---

### User Story 3 - Safe Multi-Vendor Checkout & VNPay Decommissioning Guard (Priority: P1)

As a Buyer checking out items from multiple shops, I want my payment transaction to be verified atomically before my shopping cart is cleared, and to be guided to supported payment methods (Wallet or COD), so that I experience seamless purchasing and do not encounter obsolete payment options.

**Why this priority**:
Decommissioning VNPay simplifies operations, removes fragile external polling workers, and aligns with the project's wallet-first payment architecture. Failing fast on wallet balance issues protects customer cart state.

**Independent Test**:
1. Submit an order checkout specifying `PaymentMethod.VnPay`. Verify the request fails immediately with HTTP 400 `UNSUPPORTED_PAYMENT_METHOD` and message `"VNPay payment method is no longer supported. Please use Wallet or COD."`.
2. Submit an order checkout specifying `PaymentMethod.Wallet` with insufficient funds. Verify HTTP 400 `INSUFFICIENT_WALLET_BALANCE` is returned, cart items remain in the cart, and reserved stock is released.
3. Submit an order checkout specifying `PaymentMethod.Cod`. Verify stock is committed immediately, no wallet debit occurs, and the order is placed in `Confirmed` / `PendingPayment` ready for shop fulfillment.

**Acceptance Scenarios**:
1. **Given** a client attempting to use `PaymentMethod.VnPay`, **When** `CreateOrderAsync` processes the request, **Then** the system rejects with HTTP 400 `UNSUPPORTED_PAYMENT_METHOD` without creating database records or holding inventory.
2. **Given** an authenticated buyer with insufficient wallet balance, **When** they submit `CreateOrderAsync` with `PaymentMethod.Wallet`, **Then** the service fails fast, rolls back any inventory reservations, retains cart items, and returns HTTP 400 with error code `INSUFFICIENT_WALLET_BALANCE`.
3. **Given** a successful wallet payment, **When** the debit succeeds, **Then** the full `TotalAmount` is transferred into the System Escrow Wallet, `order.Status` is set to `Paid`, and stock is permanently committed via `IFulfillmentClient.CommitStockAsync`.

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
1. Cancel a sub-order with `PaymentMethod.Cod`. Verify that NO call to `_walletClient.RefundSubOrderPaymentAsync` is made, and no wallet refund transaction is generated.
2. Cancel a paid sub-order with `PaymentMethod.Wallet`. Verify that `_walletClient.RefundSubOrderPaymentAsync` is invoked for `so.TotalAmount`, `OrderRefund.Status` becomes `Completed`, and applied vouchers in `DiscountCodeUsages` are marked as `Released`.

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

- **EC-001 (Unauthorized Platform Fee Tampering)**: An unprivileged buyer or seller attempts to POST/PUT fee adjustments.  
  *Mitigation*: Controller decorates the route with `[RequireRole("staff", "admin")]` and throws HTTP 403 `FORBIDDEN`.
- **EC-002 (In-Flight Order During Fee Rate Modification)**: Staff updates the platform fee rate while a buyer has an active 15-minute checkout quote.  
  *Mitigation*: The quote snapshot stored in `CachedQuote` preserves the agreed fee rate until expiration or checkout. Newly requested quotes immediately pick up the updated rate.
- **EC-003 (Zero-Value Payout)**: A sub-order where shop discounts equal the entire items amount (`SellerReceiveAmount == 0`).  
  *Mitigation*: The settlement engine skips wallet credit if amount is zero, logs an informational event, and marks the sub-order as `Completed`.
- **EC-004 (Negative Platform Fee Exceeds System Escrow Reserves)**: Heavy system vouchers reduce `PlatformFeeAmount` to a negative number.  
  *Mitigation*: The platform marketing reserve in System Escrow guarantees funds. The invariant mathematically balances: the platform covers the difference when releasing `SellerReceiveAmount`.
- **EC-005 (Legacy Client Submitting VNPay Method)**: A mobile client with cached code submits `PaymentMethod: "VnPay"`.  
  *Mitigation*: API validation immediately returns HTTP 400 `UNSUPPORTED_PAYMENT_METHOD` with clear English instructions to select Wallet or COD.

---

## 4. Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every `SubOrder` MUST calculate and persist `ItemsAmount`, `ProductDiscountAmount`, `ShopDiscountAmount`, `PlatformMarkupAmount`, `SystemDiscountAmount`, `ShippingFeeAmount`, `SellerReceiveAmount`, `PlatformFeeAmount`, and `TotalAmount` satisfying:
  $$\text{TotalAmount} = \text{SellerReceiveAmount} + \text{PlatformFeeAmount} + \text{ShippingFeeAmount}$$
- **FR-002**: The platform markup rate (`PlatformMarkupPercent`) MUST be dynamically configurable and managed exclusively by Staff/Admin:
  - System exposes `GET /api/v1/admin/platform-fee` and `PUT /api/v1/admin/platform-fee` restricted to `staff` and `admin` roles.
  - Rate values must be validated in range $[0.0, 100.0]\%$.
  - Rate is persisted to runtime configuration/store with safe fallback default (0.0%).
- **FR-003**: The system MUST completely decommission VNPay:
  - Any checkout request with `PaymentMethod == PaymentMethod.VnPay` MUST be rejected with HTTP 400 `UNSUPPORTED_PAYMENT_METHOD`.
  - `VnPayPaymentPollingWorker` is removed from service registration and host execution.
  - Direct checkout methods in `IWalletClient` and `WalletClient` are removed or disabled.
- **FR-004**: `OrderService.CreateOrderAsync` MUST evaluate the buyer's wallet balance before clearing cart items:
  - For `PaymentMethod.Wallet`: Execute debit via `_walletClient.PayOrderAsync`. If balance is insufficient, fail with HTTP 400 `INSUFFICIENT_WALLET_BALANCE`, retain cart items, and release stock.
  - For `PaymentMethod.Cod`: Commit stock immediately without wallet debit.
- **FR-005**: `BuyerConfirmSubOrderReceiptAsync` and `CompleteOrderAsync` MUST disburse `so.SellerReceiveAmount` to the seller's wallet via `_walletClient.SettleSellerSubOrderAsync`, retaining `PlatformFeeAmount + ShippingFeeAmount` within the System Escrow Wallet.
- **FR-006**: `WalletClient.SettleSellerSubOrderAsync` MUST NOT treat HTTP 409 `SETTLEMENT_CONFLICT` as a successful response. It must return `false` to prevent masking parameter mismatches.
- **FR-007**: `CancelOrderAsync` and `SellerRejectSubOrderAsync` MUST verify the order's payment method:
  - For `PaymentMethod.Cod`: Do NOT invoke wallet refund; only release stock and mark cancelled.
  - For `PaymentMethod.Wallet`: Call `_walletClient.RefundSubOrderPaymentAsync` for `so.TotalAmount`, set `OrderRefund.Status = Completed`, and release applied vouchers.
- **FR-008**: When any order cancellation or expiration occurs, all associated `DiscountCodeUsages` records with `Status == Reserved` MUST be transitioned to `Status = Released`, and the corresponding `DiscountCode.ReservedCount` decremented.
- **FR-009**: The system MUST expose `IVoucherDiscountCalculator` abstraction supporting multi-tenant discount separation (`ShopDiscountAmount` vs `SystemDiscountAmount`), enabling drop-in implementation of the future Vouchers feature.

### Non-Functional Requirements

- **NFR-001 (Zero Repository Pattern)**: All database access MUST use `ICommerceDbContext` / `AppDbContext` directly in Application services.
- **NFR-002 (Inward-Only)**: Zero external references outside `CommerceService`.
- **NFR-003 (Error Standards)**: 100% English Sentence-case for all exception and error messages; `SCREAMING_SNAKE_CASE` for all error codes.
- **NFR-004 (Security & Role Enforcement)**: Staff-only endpoints MUST enforce role validation through `[RequireRole("staff", "admin")]` and reject unauthorized callers with HTTP 403 `FORBIDDEN`.
- **NFR-005 (Compilation & Test)**: Solution MUST compile with 0 errors and 0 warnings on `Release` configuration, and pass 100% of the unit test suite.

---

## 5. Verification Checklist

- [ ] `CalculateFinancials` domain method enforces invariant and handles both positive and negative `PlatformFeeAmount`.
- [ ] Admin endpoint `PUT /api/v1/admin/platform-fee` successfully updates rate for staff and returns 403 for buyer/seller.
- [ ] `CreateOrderAsync` rejects `PaymentMethod.VnPay` with HTTP 400 `UNSUPPORTED_PAYMENT_METHOD`.
- [ ] `CreateOrderAsync` fails fast on `INSUFFICIENT_WALLET_BALANCE` without deleting cart items.
- [ ] `VnPayPaymentPollingWorker` is removed from `Commerce.Api/Program.cs`.
- [ ] `BuyerConfirmSubOrderReceiptAsync` settles exact `SellerReceiveAmount` to seller.
- [ ] `WalletClient` returns `false` on HTTP 409 `SETTLEMENT_CONFLICT`.
- [ ] `CancelOrderAsync` and `SellerRejectSubOrderAsync` bypass wallet refund for COD orders.
- [ ] `DiscountCodeUsages` records are released when orders cancel or expire.
- [ ] `IVoucherDiscountCalculator` abstraction is defined and integrated into `QuoteCheckoutAsync`.
- [ ] `dotnet build Commerce.sln -c Release` compiles with 0 errors and 0 warnings.
- [ ] `dotnet test Commerce.sln -c Release --no-build` passes with 100% success rate.
