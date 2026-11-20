# Tasks: Voucher & Promotion Allocation Engine (Feature 024)

**Input**: Design documents from `/specs/024-voucher-and-promotion-allocation-engine/`  
**Prerequisites**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/)  
**Target Solution**: `Commerce.sln`  
**Quality Target**: 0 Warnings, 0 Errors, 100% Tests Passed  

---

## Format: `[ID] [P?] [Story] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Target component or user story tag (`[CORE]`, `[CALC]`, `[LIFE]`, `[HOOK]`, `[SVC]`, `[API]`, `[TEST]`)

---

## Phase 1: Setup & Foundational Foundation (Completed / Verified)

**Purpose**: Ensure domain enums, entities, and database abstractions are clean, aligned, and compiling without schema drift.

- [x] T001 [CORE] Verify zero schema drift on PostgreSQL tables `DiscountCodes`, `DiscountCodeScopes`, `DiscountCodeUsages` in `Commerce.Infrastructure/Persistence/AppDbContext.cs`.
- [x] T002 [CORE] Clean up duplicate enum aliases in `Commerce.Domain/Enums/DiscountScopeType.cs` (`System = 0, Shop = 1, Product = 2`) and `Commerce.Domain/Enums/DiscountType.cs` (`Percentage = 0, FixedAmount = 1`).
- [x] T003 [CORE] Expose `DatabaseFacade Database { get; }` on `Commerce.Application/Interfaces/ICommerceDbContext.cs` for advisory lock transactions.
- [x] T004 [CORE] Update `Commerce.Infrastructure/Persistence/Seeds/CommerceSeedData.cs` to use canonical `DiscountType.FixedAmount`.

---

## Phase 2: Pure Calculation Engine (`IVoucherCalculationEngine`)

**Purpose**: Implement the thread-safe, pure in-memory calculation engine evaluating 3-tier discounts and VND integer pro-rata allocation with penny rounding lock.

- [x] T005 [P] [CALC] Define calculation inputs and models in `Commerce.Application/Services/Discount/CalculationModels.cs`:
  - `CalculationItemInput(Guid CartItemId, Guid ProductId, Guid SkuId, Guid SellerId, string ProductName, int Quantity, decimal UnitPrice)`
  - `EvaluateVouchersRequest(List<CalculationItemInput> Items, List<DiscountCode> AvailableVouchers, decimal PlatformMarkupPercent = 0.0m)`
  - `AllocatedVoucherUsage(Guid DiscountCodeId, string Code, DiscountScopeType ScopeType, Guid? SubOrderSellerId, decimal DiscountAmount)`
  - `ShopCalculationResult(...)` and `EvaluateVouchersResult(...)`
- [x] T006 [CALC] Implement interface `IVoucherCalculationEngine` and implementation `VoucherCalculationEngine` in `Commerce.Application/Services/Discount/`:
  - Implement Step 1: `CalculateRawDiscount(decimal baseAmount, DiscountCode voucher)` with integer VND rounding.
  - Implement Step 2: Tier 1 (`Product`) and Tier 2 (`Shop`) calculation per shop.
  - Implement Step 3: Tier 3 (`System`) calculation and pro-rata allocation with deterministic ascending `SellerId` order and penny rounding lock.
  - Implement Step 4: Financial invariant calculation according to Shopee / TikTok Shop model:
    - `NetSellerAmount = ItemsAmount - ProductDiscountAmount - ShopDiscountAmount`
    - `PlatformFeeAmount = Math.Round(NetSellerAmount * (platformMarkupPercent / 100m), 0, MidpointRounding.AwayFromZero)`
    - `SellerReceiveAmount = NetSellerAmount - PlatformFeeAmount` (Seller payout after 5% commission)
    - `TotalAmount = NetSellerAmount - SystemDiscountAmount + ShippingFeeAmount` (Buyer pays catalog price - discounts + ship)
  - Implement stacking conflict detector: rejecting duplicate `System` vouchers, duplicate `Shop` vouchers per seller, or duplicate `Product` vouchers per SKU.
- [x] T007 [P] [TEST] Implement 8 core mathematical unit tests in `tests/Commerce.UnitTests/Services/VoucherCalculationEngineTests.cs`:
  - Case 1: Stacking `Shop` + `System`, exact total integer VND sum match.
  - Case 2: Pro-rata 3 shops with odd remainder, penny rounding assigned to last shop.
  - Case 3: Percentage voucher capped at `MaxDiscountAmount`.
  - Case 4: `SellerReceiveAmount` unchanged when `System` voucher is applied (borne by platform).
  - Case 5: Block `VOUCHER_STACKING_CONFLICT` when 2 vouchers of same scope type are passed.
  - Case 6: Block core financial edits on used vouchers (`VOUCHER_ALREADY_IN_USE`).
  - Case 7: Block Seller attaching another seller's SKU (`VOUCHER_SELLER_SKU_FORBIDDEN`).
  - Case 8: Boundary check on `EndsAt` (`now < EndsAt` valid, `now >= EndsAt` expired).

---

## Phase 3: Voucher Lifecycle Service (`IVoucherLifecycleService`)

**Purpose**: Manage high-concurrency reservation locking, confirmation, idempotent release, and orphan record sweeping.

- [x] T008 [LIFE] Define interface `IVoucherLifecycleService` in `Commerce.Application/Services/Discount/IVoucherLifecycleService.cs`:
  - `ReserveVouchersAsync(Guid buyerId, List<AllocatedVoucherUsage> vouchers, CancellationToken ct)`
  - `CommitOrderVouchersAsync(Guid orderId, CancellationToken ct)`
  - `ReleaseOrderVouchersAsync(Guid orderId, CancellationToken ct)`
  - `ReleaseSubOrderVouchersAsync(Guid subOrderId, CancellationToken ct)`
  - `ReleaseReservationsAsync(List<Guid> usageIds, CancellationToken ct)`
  - `ReleaseExpiredReservationsAsync(CancellationToken ct)`
- [x] T009 [LIFE] Implement `VoucherLifecycleService` in `Commerce.Application/Services/Discount/VoucherLifecycleService.cs`:
  - Implement short transaction with `pg_advisory_xact_lock(hashtext(concat(voucherId, ':', buyerId)))`.
  - Check per-user limit `< MaxUsesPerUser` and global limit `< MaxUsesTotal`.
  - Increment `ReservedCount` and insert `DiscountCodeUsage` (`Status = Reserved`, `OrderId = null`).
  - Implement idempotent status transition (`WHERE Status = ExpectedStatus`) before adjusting counters.
- [x] T010 [P] [TEST] Implement concurrency unit test in `tests/Commerce.UnitTests/Services/VoucherLifecycleServiceTests.cs`:
  - Simulate 20 concurrent threads attempting to reserve a voucher with `MaxUsesTotal = 1`.
  - Verify exactly 1 thread succeeds and 19 receive `VOUCHER_USAGE_LIMIT_EXCEEDED`.

---

## Phase 4: OrderService & Worker Integration (`Order Hooks`)

**Purpose**: Connect `OrderService.QuoteCheckoutAsync`, `CreateOrderAsync`, `CancelOrderAsync`, and `PaymentTimeoutWorker` to the voucher engine without breaking changes.

- [x] T011 [HOOK] Update `Commerce.Application/Interfaces/IVoucherDiscountCalculator.cs`:
  - Add `decimal ProductDiscountAmount = 0m` to `ShopDiscountBreakdown`.
  - Add `IReadOnlyList<CalculationItemInput>? Items = null` to `VoucherCalculationRequest`.
  - Add `IReadOnlyList<string>? Warnings = null` to `VoucherCalculationResult`.
- [x] T012 [HOOK] Implement `VoucherDiscountCalculator : IVoucherDiscountCalculator` in `Commerce.Application/Services/Discount/VoucherDiscountCalculator.cs`:
  - Query active vouchers from `ICommerceDbContext` matching `request.VoucherCodes`.
  - Perform graceful validation generating warning strings for non-applicable/expired vouchers.
  - Call `IVoucherCalculationEngine.EvaluateAndApplyVouchers` to compute discounts.
  - Replace `DefaultVoucherDiscountCalculator` with `VoucherDiscountCalculator` in `Commerce.Application/DependencyInjection.cs`.
- [x] T013 [HOOK] Update Shopee / TikTok Shop financial model in `Commerce.Domain/Entities/SubOrder.cs` & `OrderService.QuoteCheckoutAsync`:
  - Update `SubOrder.CalculateFinancials` in `Commerce.Domain/Entities/SubOrder.cs` so platform fee is deducted from Seller (`SellerReceiveAmount = NetSeller - PlatformFeeAmount`) and Buyer pays catalog price minus discounts + shipping (`TotalAmount = NetSeller - SystemDiscountAmount + ShippingFeeAmount`).
  - Update `OrderService.QuoteCheckoutAsync` in `Commerce.Application/Services/Order/OrderService.cs` to map exact Shopee financial amounts and warnings in `CheckoutQuoteResponse.Warnings`.
  - Update `tests/Commerce.UnitTests/Domain/FinancialInvariantTests.cs` to validate the Shopee seller commission invariant.
- [x] T014 [HOOK] Update `OrderService.CreateOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs`:
  - Re-evaluate voucher calculation on fresh SKU prices.
  - Call `_voucherLifecycleService.ReserveVouchersAsync` before external service calls.
  - Link `usage.OrderId` and `usage.SubOrderId` within master order transaction.
  - Commit vouchers via `_voucherLifecycleService.CommitOrderVouchersAsync` for Wallet (`Paid`) and COD (`Confirmed`).
  - Add compensation rollback in catch block: `_voucherLifecycleService.ReleaseReservationsAsync`.
- [x] T015 [HOOK] Update cancellation & reject flows in `Commerce.Application/Services/Order/OrderService.cs`:
  - Delegate `CancelOrderAsync` to `_voucherLifecycleService.ReleaseOrderVouchersAsync`.
  - Delegate `SellerRejectSubOrderAsync` and failed delivery to `_voucherLifecycleService.ReleaseSubOrderVouchersAsync`.
- [x] T016 [HOOK] Extend `Commerce.Api/Workers/PaymentTimeoutWorker.cs`:
  - Integrate periodic sweep (every 15m) invoking `_voucherLifecycleService.ReleaseExpiredReservationsAsync`.

---

## Phase 5: Application Service & DTOs (`IDiscountService`)

**Purpose**: Implement CRUD, public validation, and auto-recommendation use cases.

- [x] T017 [P] [SVC] Implement DTO records in `Commerce.Application/Services/Discount/DiscountDtos.cs`:
  - `ValidateVoucherRequest`, `ValidateVoucherResponse`
  - `RecommendVouchersRequest`, `RecommendVouchersResponse`, `TierSavingsDto`
  - `CreateDiscountRequest`, `CreateDiscountScopeInput`, `UpdateDiscountRequest`, `UpdateDiscountStatusRequest`
  - `DiscountDetailResponse`, `DiscountSummaryDto`, `BuyerAvailableVoucherDto`, `DiscountScopeDto`
- [x] T018 [SVC] Define interface `IDiscountService` in `Commerce.Application/Services/Discount/IDiscountService.cs`:
  - `GetAvailableVouchersAsync(Guid buyerId, Guid? sellerId, List<Guid>? cartItemIds, int page, int pageSize, CancellationToken ct)`
  - `ValidateVoucherAsync(Guid buyerId, ValidateVoucherRequest request, CancellationToken ct)`
  - `RecommendVouchersAsync(Guid buyerId, RecommendVouchersRequest request, CancellationToken ct)`
  - `CreateDiscountAsync(Guid currentUserId, bool isAdmin, CreateDiscountRequest request, CancellationToken ct)`
  - `ListDiscountsAsync(Guid currentUserId, bool isAdmin, int page, int pageSize, DiscountScopeType? scopeType, DiscountStatus? status, string? search, CancellationToken ct)`
  - `GetDiscountByIdAsync(Guid currentUserId, bool isAdmin, Guid discountId, CancellationToken ct)`
  - `UpdateDiscountAsync(Guid currentUserId, bool isAdmin, Guid discountId, UpdateDiscountRequest request, CancellationToken ct)`
  - `UpdateDiscountStatusAsync(Guid currentUserId, bool isAdmin, Guid discountId, UpdateDiscountStatusRequest request, CancellationToken ct)`
  - `DeleteDiscountAsync(Guid currentUserId, bool isAdmin, Guid discountId, CancellationToken ct)`
- [x] T019 [SVC] Implement `DiscountService` in `Commerce.Application/Services/Discount/DiscountService.cs`:
  - Integrate dynamic permission gate: `_userClient.CheckBuyerPermissionAsync(currentUserId, "manage_voucher", ct)` for Seller write operations (`UC-04`, `UC-07`, `UC-08`, `UC-09`).
  - Integrate Catalog SKU verification via `_catalogClient.GetSkuBatchDetailsAsync` (`UC-04`).
  - Enforce ownership guard (`Seller can only view/edit owned vouchers`).
  - Enforce financial freeze guard: prohibit core financial modifications when `UsedCount + ReservedCount > 0`.
  - Register `IDiscountService` in `Commerce.Application/DependencyInjection.cs`.

---

## Phase 6: Web API Layer (`DiscountsController`)

**Purpose**: Expose thin controller endpoints following CommerceService conventions.

- [x] T020 [API] Implement `Commerce.Api/Controllers/Public/DiscountsController.cs`:
  - Route: `[Route("api/v1/discounts")]`
  - `GET /available` (`[RequireRole("buyer")]`, `UC-01`)
  - `POST /validate` (`[RequireRole("buyer")]`, `UC-02`)
  - `POST /recommend` (`[RequireRole("buyer")]`, `UC-03`)
  - `POST /` (`[RequireRole("admin", "seller")]`, `UC-04`)
  - `GET /` (`[RequireRole("admin", "seller", "staff")]`, `UC-05`)
  - `GET /{discountId}` (`[RequireRole("admin", "seller", "staff")]`, `UC-06`)
  - `PUT /{discountId}` (`[RequireRole("admin", "seller")]`, `UC-07`)
  - `PATCH /{discountId}/status` (`[RequireRole("admin", "seller")]`, `UC-08`)
  - `DELETE /{discountId}` (`[RequireRole("admin", "seller")]`, `UC-09`)
  - Extract user ID via `this.TryGetUserId(out var userId, out var authError)`.
  - Return Pure DTOs directly with appropriate HTTP status codes (200, 201).

---

## Phase 7: Verification & Quality Assurance

**Purpose**: Execute end-to-end testing, verify architectural compliance, and confirm 100% build health.

- [x] T021 [TEST] Implement controller contract tests in `tests/Commerce.UnitTests/Controllers/DiscountsControllerTests.cs`.
- [x] T022 [TEST] Implement end-to-end integration test verifying checkout quote with 3-tier stacking in `tests/Commerce.UnitTests/Application/EndToEndFlowTests.cs`.
- [x] T023 [TEST] Run `dotnet build Commerce.sln -c Release -m:1 /p:UseSharedCompilation=false` verifying **0 Errors, 0 Warnings**.
- [x] T024 [TEST] Run `dotnet test Commerce.sln` verifying all unit tests pass 100%.
- [x] T025 [P] [TEST] Run `speckit-analyze` to verify non-destructive cross-artifact consistency across `spec.md`, `plan.md`, `tasks.md`, `data-model.md`, and `contracts/`.

---

## Phase 8: Shopee Alignment & Unified Shop-Level Voucher Stacking (Feature Update)

**Purpose**: Transition voucher stacking from 3 arbitrary tiers to the Shopee 2-tier model: Shop-Level Exclusivity (single voucher per shop, eliminating double-discounting margin erosion) + Platform Voucher Stacking, with Catalog Service batch endpoint alignment and 5% platform fee financial invariant.

- [x] T026 [CALC] Update `VoucherCalculationEngine.cs` in `Commerce.Application/Services/Discount/VoucherCalculationEngine.cs`:
  - Enforce Shop-Level Exclusivity Guard: Group input vouchers by `OwnerSellerId` for `Product` and `Shop` scopes. If a shop has $\ge 2$ vouchers, throw `CommerceDomainException("SHOP_MULTIPLE_VOUCHERS_NOT_ALLOWED", "Only one voucher per shop can be applied.", 400)`.
  - For each shop, calculate discount from the single selected voucher (either product-level eligible items or shop-wide total).
  - Compute `NetSellerAmount = ItemsAmount - ShopDiscountAmount`.
- [x] T027 [CALC] Update `RecommendVouchersAsync` in `DiscountService.cs`:
  - When auto-recommending vouchers for a shop, evaluate both product and shop vouchers, and select the single voucher with $\max(\text{ProductDiscount}, \text{ShopDiscount})$.
- [x] T028 [DTO] Update `TierSavingsDto` in `Commerce.Application/Services/Discount/DiscountDtos.cs`:
  - Streamline to 2-tier savings: `decimal ShopDiscount`, `decimal SystemDiscount`, `decimal TotalSavings`.
- [x] T029 [HOOK] Update `OrderService.cs` & `VoucherDiscountCalculator.cs` in `Commerce.Application/Services/`:
  - Ensure `OrderService.CreateOrderAsync` and `QuoteCheckoutAsync` enforce Shop-Level Exclusivity.
  - Assert the Shopee financial invariant: $ShopNetEarnings = ShopGrossRevenue - ShopDiscounts - PlatformFeeAmount + PlatformVoucherRebate$.
- [x] T030 [TEST] Add & update unit tests in `tests/Commerce.UnitTests/Services/`:
  - Test 1: Verify `SHOP_MULTIPLE_VOUCHERS_NOT_ALLOWED` thrown when 2 vouchers of the same shop are passed.
  - Test 2: Verify `RecommendVouchersAsync` picks the single best voucher for a shop.
  - Test 3: Multi-shop order with 1 voucher per shop + 1 platform voucher.
  - Test 4: Financial invariant exact match without penny rounding discrepancy.
- [x] T031 [TEST] Run `dotnet build Commerce.sln -c Release -m:1 /p:UseSharedCompilation=false` verifying **0 Warnings, 0 Errors**.
