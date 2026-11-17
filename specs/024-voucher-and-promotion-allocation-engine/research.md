# Technical Research & Architectural Decisions: Voucher & Promotion Allocation Engine

**Feature**: `024-voucher-and-promotion-allocation-engine` | **Date**: 2026-11-16 | **Status**: APPROVED

---

## 1. Decision 1: 3-Tier Stacking Hierarchy & Conflict Resolution

### Context
In multi-vendor e-commerce checkouts, buyers combine product-level promotions, seller-specific discounts, and platform-wide subsidies in a single order. Uncontrolled voucher stacking can lead to negative profit margins or arithmetic ambiguities if orders are evaluated out of order.

### Alternatives Considered
- **Alternative A (Single-voucher policy)**: Only allow one voucher per checkout session. *Rejected: Poor buyer experience, uncompetitive with modern e-commerce platforms.*
- **Alternative B (Arbitrary parallel stacking)**: Apply all vouchers simultaneously against initial gross amount. *Rejected: Causes severe over-discounting where total discounts exceed 100% of line-item prices.*
- **Alternative C (Strict 3-Tier Sequential Stacking - Selected)**:
  1. **Tier 1 (`Product`)**: Max 1 voucher per product/SKU line item.
  2. **Tier 2 (`Shop`)**: Max 1 voucher per shop/sub-order, applied against shop base **after Tier 1 deductions**.
  3. **Tier 3 (`System`)**: Max 1 voucher per master order, applied against eligible items **after Tier 1 and Tier 2 deductions**.

### Stacking Conflict Matrix
| Combination | Allowed? | Enforcement / Error |
| :--- | :---: | :--- |
| 2+ System Vouchers in one order | ❌ No | HTTP 400 `VOUCHER_STACKING_CONFLICT` |
| 2+ Shop Vouchers for same SellerId | ❌ No | HTTP 400 `VOUCHER_STACKING_CONFLICT` |
| 2+ Product Vouchers matching same SkuId | ❌ No | HTTP 400 `VOUCHER_STACKING_CONFLICT` |
| 1 Product + 1 Shop + 1 System Voucher | ✅ Yes | Evaluated in strict 1 $\rightarrow$ 2 $\rightarrow$ 3 order |

---

## 2. Decision 2: Pure Calculation Function & Integer VND Penny Rounding Lock

### Context
Vietnam Dong (VND) is a zero-decimal currency without fractional subdivisions (cents/xu). Pro-rata allocation of platform discounts across multiple shops often yields fractional values (e.g. $100,000 \times \frac{730,000}{1,330,000} = 54,887.218...$). Accumulating rounded integers can cause off-by-one errors (penny rounding drift) where the sum of shop discounts does not match the master voucher discount.

### Technical Decision
1. **Pure Function (`IVoucherCalculationEngine`)**: Thread-safe calculation executed entirely in memory without database queries. Given `EvaluateVouchersRequest(Items, AvailableVouchers, PlatformMarkupPercent)`, it produces deterministic `EvaluateVouchersResult`.
2. **Penny Rounding Lock**:
   - Sort eligible shops deterministically by `SellerId` ascending.
   - For shops $j = 1$ to $L - 1$:
     $$\text{discount}_{k_j} = \text{Math.Round}\left(D_{\text{total}} \times \frac{\text{Base}_{k_j}}{\text{Base}_{\text{total}}}, 0, \text{MidpointRounding.AwayFromZero}\right)$$
     $$\text{allocatedSum} += \text{discount}_{k_j}$$
   - For the final shop ($j = L$):
     $$\text{discount}_{k_L} = D_{\text{total}} - \text{allocatedSum}$$
3. **Shopee / TikTok Shop Commission & Escrow Protection**:
   - $\text{NetSeller} = \text{ItemsAmount} - \text{ProductDiscount} - \text{ShopDiscount}$.
   - Platform fee is deducted from Seller sales (default $r = 5\%$):
     $$\text{PlatformFeeAmount} = \text{Math.Round}\left(\text{NetSeller} \times \frac{r}{100m}, 0, \text{MidpointRounding.AwayFromZero}\right)$$
   - Seller payout after commission:
     $$\text{SellerReceiveAmount} = \text{NetSeller} - \text{PlatformFeeAmount}$$
   - Buyer pays exact catalog price minus all discounts + shipping fee (Zero Buyer markup):
     $$\text{TotalAmount} = \text{ItemsAmount} - (\text{ProductDiscount} + \text{ShopDiscount} + \text{SystemDiscount}) + \text{ShippingFee}$$
   - System vouchers are funded entirely by Platform marketing budget:
     $$\text{NetPlatformRetained} = \text{PlatformFeeAmount} - \text{SystemDiscountAmount}$$

---

## 3. Decision 3: Atomic High-Concurrency Reservation Locking

### Context
Flash sales feature limited-quantity vouchers (e.g., 100 total redemptions). When thousands of buyers click "Place Order" simultaneously, standard `SELECT ... UPDATE` queries suffer from race conditions, over-issuing vouchers beyond `MaxUsesTotal` or exceeding `MaxUsesPerUser`.

### Alternatives Considered
- **Alternative A (Redis Distributed Lock)**: Acquire Redis lock on voucher ID. *Rejected: Adds Redis failure modes, network latency, and requires two-phase commits between Redis and PostgreSQL.*
- **Alternative B (Optimistic Concurrency via RowVersion)**: Update with version check. *Rejected: In high-concurrency contention, 95%+ of requests fail with concurrency exceptions and require aggressive retries.*
- **Alternative C (PostgreSQL Advisory Transaction Lock - Selected)**:
  - Inside a short database transaction:
    ```sql
    SELECT pg_advisory_xact_lock(hashtext(concat(voucherId, ':', buyerId)));
    ```
  - Verify `(UsedCount + ReservedCount) < MaxUsesTotal`.
  - Verify buyer usage count `< MaxUsesPerUser`.
  - Execute atomic SQL increment:
    ```sql
    UPDATE "DiscountCodes"
    SET "ReservedCount" = "ReservedCount" + 1, "UpdatedAt" = NOW()
    WHERE "Id" = voucherId AND ("MaxUsesTotal" IS NULL OR "UsedCount" + "ReservedCount" < "MaxUsesTotal");
    ```
  - Insert temporary reservation record into `DiscountCodeUsages` with `Status = 0 (Reserved)`, `OrderId = NULL`.
  - Commit short transaction immediately (holding lock for < 5ms).

---

## 4. Decision 4: Idempotent Voucher Release & Orphan Sweeper

### Context
When orders are cancelled by the buyer, timeout after 15 minutes, or rejected by sellers, reserved vouchers must be returned immediately. Furthermore, if a server crashes after reserving a voucher but before creating an order, orphaned reservation records must be cleaned up automatically.

### Technical Decision
1. **Idempotent Release (`IVoucherLifecycleService`)**:
   - Atomic conditional update:
     ```sql
     UPDATE "DiscountCodeUsages"
     SET "Status" = 2, "ReleasedAt" = NOW(), "UpdatedAt" = NOW()
     WHERE "Id" = @usageId AND "Status" = @expectedStatus;
     ```
   - Only decrement `ReservedCount` or `UsedCount` on `DiscountCodes` when `rowsAffected == 1`.
2. **Reconciliation Sweeper in `PaymentTimeoutWorker`**:
   - Runs periodically (every 15 minutes) inside `PaymentTimeoutWorker`:
     ```sql
     UPDATE "DiscountCodeUsages"
     SET "Status" = 2, "ReleasedAt" = NOW(), "UpdatedAt" = NOW()
     WHERE "Status" = 0
       AND "ReservedAt" < (NOW() - INTERVAL '16 minutes')
       AND ("OrderId" IS NULL OR EXISTS (
           SELECT 1 FROM "Orders" o WHERE o."Id" = "DiscountCodeUsages"."OrderId" AND o."Status" = 6
       ));
     ```

---

## 5. Decision 5: Dynamic Platform Fee Integration

### Context
Feature 023 established `AdminPlatformFeeController` (`GET/PUT /api/v1/admin/platform-fee`), allowing Staff to adjust platform markup rates in real time. Static configurations (such as hardcoding 5% in `appsettings.json`) violate administrative autonomy.

### Technical Decision
- `OrderService` already implements `GetActivePlatformMarkupPercentAsync(ct)`, reading from distributed cache (`commerce:platform_fee_percent` / `commerce:platform_fee:metadata`) with 5.0% fallback.
- `EvaluateVouchersRequest.PlatformMarkupPercent` receives this dynamic rate directly from `GetActivePlatformMarkupPercentAsync(ct)` during quote generation and order creation.

---

## 6. Decision 6: Dynamic Seller Permission Check via `IUserClient`

### Context
Sellers subjected to penalties, account suspension, or low trust scores must be barred from creating or modifying promotion vouchers.

### Technical Decision
- In accordance with the user requirement to preserve dynamic permission checking via UserService:
- `IDiscountService` injects `IUserClient` and verifies:
  ```csharp
  var permission = await _userClient.CheckBuyerPermissionAsync(currentUserId, action: "manage_voucher", ct);
  if (!permission.IsAllowed)
  {
      throw new CommerceDomainException("SELLER_ACTION_RESTRICTED", "Your seller account is restricted from managing promotion vouchers.", 403);
  }
  ```
- If UserService is unreachable, fail-closed with HTTP 503 `USER_SERVICE_UNAVAILABLE`.

---

## 7. Decision 7: Facade Architecture (`IVoucherDiscountCalculator`)

### Context
Feature 023 introduced `IVoucherDiscountCalculator` in `Commerce.Application.Interfaces` and registered `DefaultVoucherDiscountCalculator` returning zero discounts. `OrderService.QuoteCheckoutAsync` already calls this interface.

### Technical Decision
- Feature 024 implements `VoucherDiscountCalculator : IVoucherDiscountCalculator` in `Commerce.Application/Services/Discount/`.
- Replaces `DefaultVoucherDiscountCalculator` in `Commerce.Application/DependencyInjection.cs`:
  ```csharp
  services.AddScoped<IVoucherDiscountCalculator, VoucherDiscountCalculator>();
  ```
- `VoucherDiscountCalculator` acts as the bridge:
  1. Resolves voucher entities from `ICommerceDbContext`.
  2. Runs soft validation and generates warnings.
  3. Invokes `IVoucherCalculationEngine.EvaluateAndApplyVouchers(...)`.
  4. Maps calculation results to `VoucherCalculationResult`.
- Zero changes required to `OrderService` calling signature, preserving 100% backward compatibility.
