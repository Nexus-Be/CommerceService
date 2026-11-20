# Research & Technical Decisions: Order Financial Lifecycle, Staff-Managed Platform Fee & Voucher Extensibility

**Feature Branch**: `023-order-financial-lifecycle-and-voucher-extensibility`  
**Date**: 2026-11-16  
**Status**: Completed  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  

---

## 1. Staff-Governed Platform Fee Rate Management

### Problem Statement
Currently, `OrderService.cs` lines 379–384 hardcode `PlatformMarkupAmount = 0m` and `PlatformFeeAmount = 0m`. Furthermore, business policy mandates that platform fees must be dynamic and customizable, but **EXCLUSIVELY MANAGED BY STAFF** (`staff` or `admin` roles). Regular users (`buyer`, `seller`) must never be permitted to alter monetization rates.

### Decision
1. **Administrative Endpoint**:
   - Create `AdminPlatformFeeController` located at `Commerce.Api/Controllers/Public/AdminPlatformFeeController.cs` (or under `Controllers/Admin/`).
   - Decorated with `[Authorize]` and `[RequireRole("staff", "admin")]`.
   - Endpoints:
     - `GET /api/v1/admin/platform-fee`: Returns current fee percentage and last updated timestamp.
     - `PUT /api/v1/admin/platform-fee`: Updates fee percentage. Validates rate in range $[0.0, 100.0]\%$.
2. **Zero Schema Drift Persistence**:
   - To avoid modifying database migrations, store the runtime rate in `IDistributedCache` under key `platform_fee:default_percent`.
   - Fallback default is read from `IConfiguration["PlatformFee:DefaultPercent"]` (configured as `0.0` in `appsettings.json`).
3. **Usage in Checkout Pipeline**:
   - `OrderService` queries the active fee rate from a helper service or cache/config when calculating quotes and creating orders, passing it to `subOrder.CalculateFinancials(..., platformMarkupPercent)`.

### Rationale
- Completely avoids database schema migration (Zero Schema Drift).
- Guarantees instant runtime effect across distributed instances via cache.
- Enforces strict role-based access control (RBAC).

---

## 2. Total Decommissioning of VNPay

### Problem Statement
The project has discontinued VNPay integration in favor of a 100% digital-wallet and cash-on-delivery (COD) model. Continuing to execute `VnPayPaymentPollingWorker` consumes redundant background threads, polls a dead provider, and causes critical race conditions where orders paid near deadline could be cancelled by `PaymentTimeoutWorker`.

### Decision
1. **Host De-registration**:
   - Remove `builder.Services.AddHostedService<VnPayPaymentPollingWorker>();` from `Commerce.Api/Program.cs`.
   - Delete `Commerce.Api/Workers/VnPayPaymentPollingWorker.cs` and `tests/Commerce.UnitTests/Application/VnPayPaymentPollingWorkerTests.cs`.
2. **API Gate Rejection**:
   - In `OrderService.CreateOrderAsync`: Check if `request.PaymentMethod == PaymentMethod.VnPay`. If so, throw `CommerceDomainException("UNSUPPORTED_PAYMENT_METHOD", "VNPay payment method is no longer supported. Please use Wallet or COD.", 400)`.
3. **Client Cleanup**:
   - Remove or deprecate `InitiateDirectCheckoutAsync` and `GetDirectCheckoutStatusAsync` in `IWalletClient` and `WalletClient.cs`.
4. **Timeout Worker Simplification**:
   - `PaymentTimeoutWorker` cancels expired `PendingPayment` orders directly without querying external VNPay endpoints.

### Rationale
- Removes 200+ lines of dead code and background polling overhead.
- Completely eliminates race condition between `PaymentTimeoutWorker` and VNPay polling.
- Focuses service verification on core payment channels: `Wallet` and `Cod`.

---

## 3. Financial Invariant Engine & Zero-Sum Math

### Problem Statement
In `OrderService.cs`, `SubOrder` properties were manually assigned without running invariant validation, leading to situations where `TotalAmount` could diverge from the sum of seller receive amount, platform fee, and shipping fee.

### Decision
- Replace manual field mapping with `subOrder.CalculateFinancials(shippingFee, shopDiscount, systemDiscount, platformMarkupPercent)`.
- Enforce the invariant:
  $$\text{TotalAmount} = \text{SellerReceiveAmount} + \text{PlatformFeeAmount} + \text{ShippingFeeAmount}$$
- Formally support `PlatformFeeAmount < 0`:
  When system promotional vouchers (`SystemDiscountAmount`) exceed platform markup (`PlatformMarkupAmount`), `PlatformFeeAmount` calculates as negative. This subsidizes the buyer while guaranteeing the seller receives 100% of their net earnings.

### Rationale
- Leverages existing validated domain logic in `Commerce.Domain/Entities/SubOrder.cs`.
- Complies with Constitution Rule II and IV.

---

## 4. System Escrow Protection & Safe Multi-Vendor Checkout

### Problem Statement
When paying via `PaymentMethod.Wallet`, if `_walletClient.PayOrderAsync` returned `INSUFFICIENT_BALANCE`, `OrderService` logged a warning, proceeded to clear the customer's cart items, and saved an unpaid `PendingPayment` order in the database. Because CommerceService has no re-pay endpoint, customer carts were wiped with no recovery path.

### Decision
- **Fail-Closed Wallet Debit**:
  - In `OrderService.CreateOrderAsync`, if `paymentResult.IsSuccess == false` and `paymentResult.ErrorCode == "INSUFFICIENT_BALANCE"`:
    - Release reserved stock via `_fulfillmentClient.ReleaseStockAsync`.
    - Do NOT remove items from cart.
    - Throw `CommerceDomainException("INSUFFICIENT_WALLET_BALANCE", "Your wallet balance is insufficient to complete this order.", 400)`.
- **System Escrow Inflow**:
  - When payment succeeds, 100% of `TotalAmount` is held in the System Escrow Wallet until delivery confirmation.

### Rationale
- Protects buyer cart state and prevents phantom dead orders from jamming warehouse inventory for 15 minutes.

---

## 5. Method-Aware Sub-Order Refund & Idempotent Settlement

### Problem Statement
1. In `CancelOrderAsync` and `SellerRejectSubOrderAsync`, `_walletClient.RefundSubOrderPaymentAsync` was called whenever `wasPaid` was true. For COD orders marked as paid or updated by mistake, this credited unearned platform funds to the buyer's wallet.
2. In `WalletClient.SettleSellerSubOrderAsync`, HTTP 409 `SETTLEMENT_CONFLICT` was caught and treated as `true` (success), swallowing genuine conflict errors when settling different amounts or sellers.

### Decision
1. **Method-Aware Refund**:
   - In `CancelOrderAsync` and `SellerRejectSubOrderAsync`:
     - If `so.PaymentMethod == PaymentMethod.Cod`: strictly skip wallet refund. Release stock and record cancellation.
     - If `so.PaymentMethod == PaymentMethod.Wallet && wasPaid`: call `_walletClient.RefundSubOrderPaymentAsync`.
2. **Strict Settlement Response Handling**:
   - Remove the `response.StatusCode == HttpStatusCode.Conflict` catch block returning `true`.
   - Return `false` on any non-2xx status, logging warning/error for audit reconciliation.

### Rationale
- Plugs financial leakage on COD cancellations.
- Adheres to integration contract Section 3.2 where 409 indicates mismatched payload conflict.

---

## 6. Voucher Extensibility Architecture (Preparation for Feature 024)

### Problem Statement
`OrderService.QuoteCheckoutAsync` currently hardcodes all discount variables to `0m`. We must define a decoupled abstraction that computes discounts and allocations without modifying existing database schemas.

### Decision
1. Define interface `IVoucherDiscountCalculator`:
   ```csharp
   public interface IVoucherDiscountCalculator
   {
       Task<VoucherCalculationResult> CalculateDiscountsAsync(
           Guid buyerId,
           List<string>? voucherCodes,
           List<ShopOrderQuote> shopQuotes,
           decimal platformMarkupPercent,
           CancellationToken ct = default);
   }
   ```
2. Provide default pass-through implementation `DefaultVoucherDiscountCalculator` returning zero discounts, ready for replacement by the full allocation engine in `specs/024`.
3. Support automatic release of reserved vouchers:
   - When orders expire or cancel, update `DiscountCodeUsages.Status = Released` and decrement `DiscountCode.ReservedCount`.
