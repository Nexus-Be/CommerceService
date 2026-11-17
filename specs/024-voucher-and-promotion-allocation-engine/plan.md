# Implementation Plan: Voucher & Promotion Allocation Engine (Feature 024)

**Branch**: `024-voucher-and-promotion-allocation-engine` | **Date**: 2026-11-16 | **Spec**: [specs/024-voucher-and-promotion-allocation-engine/spec.md](spec.md)

**Input**: Feature specification from `/specs/024-voucher-and-promotion-allocation-engine/spec.md`

---

## 1. Summary

This feature implements the complete **Voucher & Promotion Allocation Engine** for `CommerceService` (`Commerce.sln`), delivering an enterprise-grade multi-tier discount calculation, reservation lifecycle, and promotion administration subsystem:
1. **3-Tier Voucher Stacking & Pro-Rata Allocation (Shopee / TikTok Shop Model)**: Sequential discount evaluation across Tier 1 (`Product`), Tier 2 (`Shop`), and Tier 3 (`System`). Buyer pays exact catalog price minus discounts + shipping fee without extra buyer markup ($\text{TotalAmount} = \text{ItemsAmount} - \text{DiscountAmount} + \text{ShippingFeeAmount}$). Platform commission ($5\%$) is deducted directly from Seller sales ($\text{SellerReceiveAmount} = \text{NetSeller} - \text{PlatformFeeAmount}$), and System vouchers are absorbed by the platform ($\text{NetPlatformRetained} = \text{PlatformFeeAmount} - \text{SystemDiscountAmount}$), with 100% integer VND rounding and penny rounding lock.
2. **Pure In-Memory Calculation Engine (`IVoucherCalculationEngine`)**: Thread-safe pure function evaluating multi-shop baskets without database roundtrips, calculating exact line-item discounts and warnings.
3. **Seamless Facade Integration with Feature 023 (`IVoucherDiscountCalculator`)**: Implements `VoucherDiscountCalculator` replacing `DefaultVoucherDiscountCalculator` in DI, linking directly to `OrderService.QuoteCheckoutAsync` with soft validation (graceful warnings) and zero breaking changes.
4. **Atomic High-Concurrency Reservation & Idempotent Release (`IVoucherLifecycleService`)**: PostgreSQL advisory transaction locking (`pg_advisory_xact_lock`) ensuring strict per-user and per-voucher limits under race conditions, coupled with idempotent release on order cancellation/rejection and reconciliation sweeping in `PaymentTimeoutWorker`.
5. **Administrative & Buyer Public REST APIs (`DiscountsController`)**: 9 RESTful endpoints covering available voucher discovery, manual code validation, AI recommendation, creation, update, status toggling, and soft deletion.
6. **Dynamic Seller Governance**: Enforces dynamic seller privilege checks via `IUserClient.CheckBuyerPermissionAsync(currentUserId, "manage_voucher", ct)` before allowing voucher administration.

---

## 2. Technical Context

**Language/Version**: C# 12 / .NET 8.0 (`net8.0`)  
**Primary Dependencies**: ASP.NET Core, Microsoft.EntityFrameworkCore 8.0, Npgsql.EntityFrameworkCore.PostgreSQL 8.0, Microsoft.Extensions.Caching.Distributed  
**Storage**: PostgreSQL 16 (`commerce_db:5435`) via EF Core 8 (Zero Schema Drift: using existing `DiscountCodes`, `DiscountCodeScopes`, `DiscountCodeUsages`)  
**Testing**: xUnit, FluentAssertions, Moq (`tests/Commerce.UnitTests`)  
**Target Platform**: Linux container (Docker) on Port `8003`, Local development profile on Ports `5105`/`7035`  
**Project Type**: Inward-only REST Microservice (`Commerce.sln`)  
**Performance Goals**: Pure voucher allocation calculation < 10ms for 50-item multi-shop cart; concurrency test under 20 parallel requests for last voucher  
**Constraints**: Inward-only repository boundaries, No-Repository over EF Core (`ICommerceDbContext` directly), 100% English Sentence-case error messages, `SCREAMING_SNAKE_CASE` error codes, Integer VND rounding (`Math.Round(x, 0, MidpointRounding.AwayFromZero)`)  
**Scale/Scope**: Multi-vendor marketplace orders, 3 discount tiers, 9 controller endpoints, 4 internal hooks  

---

## 3. Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Rule | Compliance Status | Justification / Architectural Evidence |
| :--- | :---: | :--- |
| **I. Clean Architecture & Direct EF Core** | ✅ PASS | Application layer accesses `ICommerceDbContext` directly. Zero Repository/Unit-of-Work patterns. `ICommerceDbContext.Database` exposes `DatabaseFacade` for advisory lock transactions. |
| **II. Synchronous Typed REST & Fail-Closed** | ✅ PASS | Typed `IUserClient` and `ICatalogClient` with Polly resilience; fail-closed on service unavailability; transactional compensation on payment/persistence failure. |
| **III. Immutable Ports & RESTful Standard** | ✅ PASS | Port 8003, DB 5435. All routes plural (`/api/v1/discounts`, `/api/v1/orders`). Pure DTO responses (no `ApiResponse<T>` wrapper). RFC 9110 Problem Details for errors. |
| **IV. System-Wide Error Protocol** | ✅ PASS | 100% English Sentence-case messages (e.g. `"Voucher code was not found."`), `SCREAMING_SNAKE_CASE` codes (`VOUCHER_NOT_FOUND`, `VOUCHER_STACKING_CONFLICT`). |
| **V. Inward-Only & Self-Contained** | ✅ PASS | Zero references pointing outside `CommerceService`. All paths inward-only. |
| **Zero Schema Drift Policy** | ✅ PASS | 100% table and column alignment with existing `DiscountCodes`, `DiscountCodeScopes`, `DiscountCodeUsages`. 0 migrations required. |

---

## 4. Project Structure

### Documentation (`specs/024-voucher-and-promotion-allocation-engine/`)
```text
specs/024-voucher-and-promotion-allocation-engine/
├── spec.md              # Feature specification
├── plan.md              # Implementation plan (this file)
├── research.md          # Technical research & architectural decisions
├── data-model.md        # Entities, financial invariants, and state transitions
├── quickstart.md        # Verification walkthrough and testing commands
├── tasks.md             # Dependency-ordered execution checklist
└── contracts/
    ├── discounts-api.yaml          # OpenAPI contract for DiscountsController (9 endpoints)
    └── checkout-voucher-api.yaml   # OpenAPI contract for quote and order checkout integration
```

### Source Code (`Commerce.sln`)
```text
Commerce.Domain/
├── Entities/
│   ├── DiscountCode.cs                 # Existing entity with Code, MinOrderAmount, MaxUsesTotal
│   ├── DiscountCodeScope.cs            # Existing entity with ScopeRefType, ScopeRefId
│   ├── DiscountCodeUsage.cs            # Existing entity with UserId, OrderId, SubOrderId, Status
│   ├── SubOrder.cs                     # CalculateFinancials with Product, Shop, System discounts
│   └── Order.cs                        # Master order entity
└── Enums/
    ├── DiscountScopeType.cs            # System = 0, Shop = 1, Product = 2
    ├── DiscountType.cs                 # Percentage = 0, FixedAmount = 1
    ├── DiscountScopeRefType.cs         # Shop = 0, Product = 1, Sku = 2, Category = 3, All = 4
    ├── DiscountStatus.cs               # Draft = 0, Active = 1, Inactive = 2, Expired = 3
    └── DiscountUsageStatus.cs          # Reserved = 0, Used = 1, Released = 2

Commerce.Application/
├── Interfaces/
│   ├── ICommerceDbContext.cs           # EF Core interface with DatabaseFacade Database
│   ├── IVoucherDiscountCalculator.cs   # Facade abstraction for OrderService checkout integration
│   ├── IUserClient.cs                  # User permission check client
│   └── ICatalogClient.cs               # Catalog SKU batch validation client
└── Services/
    └── Discount/
        ├── IVoucherCalculationEngine.cs # Pure RAM calculation engine interface
        ├── VoucherCalculationEngine.cs  # 3-tier sequential math & pro-rata penny rounding lock
        ├── IVoucherLifecycleService.cs  # Atomic reservation & idempotent release interface
        ├── VoucherLifecycleService.cs   # pg_advisory_xact_lock, commit, release, orphan sweeper
        ├── VoucherDiscountCalculator.cs # IVoucherDiscountCalculator implementation (replaces Default)
        ├── IDiscountService.cs          # Application CRUD & recommendation service interface
        ├── DiscountService.cs           # Application CRUD, validation, and recommendation logic
        ├── DiscountRequests.cs          # Pure Request DTOs
        └── DiscountResponses.cs         # Pure Response DTOs

Commerce.Api/
├── Controllers/
│   └── Public/
│       └── DiscountsController.cs      # Thin controller with 9 endpoints (/api/v1/discounts)
└── Workers/
    └── PaymentTimeoutWorker.cs         # Extended with orphan reservation sweeper (> 16 minutes)

tests/Commerce.UnitTests/
├── Services/
│   ├── VoucherCalculationEngineTests.cs # 8 core mathematical test cases for pure engine
│   ├── VoucherLifecycleServiceTests.cs  # Concurrency test (20 parallel threads) & lifecycle tests
│   └── DiscountServiceTests.cs          # CRUD, validation, and RBAC tests
└── Controllers/
    └── DiscountsControllerTests.cs      # Controller contract tests (HTTP 200, 201, 400, 403, 404)
```

---

## 5. Verification Strategy

1. **Compilation Gate**: `dotnet build Commerce.sln -c Release -m:1 /p:UseSharedCompilation=false` must succeed with **0 Warning, 0 Error**.
2. **Pure Engine Test Gate**: 8 unit tests validating exact integer VND calculations, penny rounding locks, stacking guards, and time boundaries.
3. **Concurrency Test Gate**: 20 concurrent tasks competing for 1 remaining voucher slot; exactly 1 succeeds with `Reserved`, 19 receive `400 VOUCHER_USAGE_LIMIT_EXCEEDED`.
4. **Integration Gate**: Complete checkout quote with 3-tier stacking; total amount invariant matches $\sum (\text{Items} - \text{Discounts} + \text{Shipping} + \text{PlatformMarkup})$.
5. **Full Suite**: `dotnet test Commerce.sln` passes 100% of test cases.
