# Implementation Plan: Order Financial Lifecycle, Staff-Managed Platform Fee & Voucher Extensibility (VNPay Decommissioned)

**Branch**: `023-order-financial-lifecycle-and-voucher-extensibility` | **Date**: 2026-11-16 | **Spec**: [specs/023-order-financial-lifecycle-and-voucher-extensibility/spec.md](spec.md)

**Input**: Feature specification from `/specs/023-order-financial-lifecycle-and-voucher-extensibility/spec.md`

---

## 1. Summary

This feature standardizes the financial lifecycle of `CommerceService` across 6 key axes:
1. **Staff-Governed Platform Fee Rate Management**: Introduces administrative endpoints `GET` and `PUT /api/v1/admin/platform-fee` protected strictly by `[RequireRole("staff", "admin")]`, storing runtime rates in distributed cache with fallback to configuration, and eliminating hardcoded zero rates.
2. **Decommissioning VNPay**: Fully eliminates `VnPayPaymentPollingWorker`, strips VNPay direct checkout methods from `IWalletClient`, and guards `CreateOrderAsync` to reject VNPay with HTTP 400 `UNSUPPORTED_PAYMENT_METHOD`.
3. **Multi-Tier Financial Invariant Enforcement**: Replaces manual assignments with `subOrder.CalculateFinancials(...)` guaranteeing $\text{TotalAmount} = \text{SellerReceiveAmount} + \text{PlatformFeeAmount} + \text{ShippingFeeAmount}$ with support for negative `PlatformFeeAmount`.
4. **Fail-Closed System Escrow Protection**: Evaluates wallet balance before cart purge, preventing dead `PendingPayment` orders and releasing stock upon insufficient funds.
5. **Method-Aware Sub-Order Refund & Strict Settlement**: Stops fraudulent COD wallet refunds, cleanses 409 conflict swallowing in `WalletClient`, and releases voucher usages upon cancellation.
6. **Voucher Extensibility**: Introduces `IVoucherDiscountCalculator` abstraction and clean separation of discount fields (`ProductDiscountAmount`, `ShopDiscountAmount`, `SystemDiscountAmount`) ready for implementation of feature `024`.

---

## 2. Technical Context

**Language/Version**: C# 12 / .NET 8.0 (`net8.0`)  
**Primary Dependencies**: ASP.NET Core, Microsoft.EntityFrameworkCore 8.0, Npgsql.EntityFrameworkCore.PostgreSQL 8.0, Microsoft.Extensions.Caching.Distributed  
**Storage**: PostgreSQL 16 (`commerce_db:5435`) via EF Core 8 (Zero Schema Drift, 0 migrations required)  
**Testing**: xUnit, FluentAssertions, Moq (`tests/Commerce.UnitTests`)  
**Target Platform**: Linux container (Docker) on Port `8003`, Local development profile on Ports `5105`/`7035`  
**Project Type**: Inward-only REST Microservice (`Commerce.sln`)  
**Performance Goals**: In-memory financial invariant calculations < 15ms per multi-shop order  
**Constraints**: Inward-only repository boundaries, No-Repository over EF Core (`ICommerceDbContext` directly), 100% English Sentence-case error messages, `SCREAMING_SNAKE_CASE` error codes  
**Scale/Scope**: Multi-vendor marketplace orders, 2 active payment methods (`Wallet`, `Cod`)  

---

## 3. Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Rule | Compliance Status | Justification / Architectural Evidence |
| :--- | :---: | :--- |
| **I. Clean Architecture & Direct EF Core** | ✅ PASS | Direct access via `ICommerceDbContext` / `AppDbContext`. Zero Repository or Unit-of-Work patterns. |
| **II. Synchronous Typed REST & Fail-Closed** | ✅ PASS | Typed `IWalletClient` with Polly resilience; fail-closed on insufficient balance; transactional stock release. |
| **III. Immutable Ports & RESTful Standard** | ✅ PASS | Port 8003, DB 5435. All routes plural (`/api/v1/admin/platform-fee`, `/api/v1/orders`). RFC 9110 Problem Details. |
| **IV. System-Wide Error Protocol** | ✅ PASS | 100% English Sentence-case messages (`"Access denied: Only staff members can adjust platform fee rates."`), `SCREAMING_SNAKE_CASE` codes (`FORBIDDEN`, `UNSUPPORTED_PAYMENT_METHOD`). |
| **V. Inward-Only & Self-Contained** | ✅ PASS | Zero references pointing outside `CommerceService`. No cross-repo paths. |
| **Zero Schema Drift Policy** | ✅ PASS | Uses existing entity columns (`PlatformMarkupAmount`, `PlatformFeeAmount`, `SellerReceiveAmount`). Zero database migrations required. |

---

## 4. Project Structure

### Documentation (`specs/023-order-financial-lifecycle-and-voucher-extensibility/`)
```text
specs/023-order-financial-lifecycle-and-voucher-extensibility/
├── spec.md              # Feature specification
├── plan.md              # Implementation plan (this file)
├── research.md          # Technical research & architectural decisions
├── data-model.md        # Entities, financial invariants, and state transitions
├── quickstart.md        # Verification walkthrough and testing commands
└── contracts/
    ├── admin-platform-fee-api.yaml      # OpenAPI contract for staff platform fee management
    └── order-financial-api.yaml         # OpenAPI contract for order financial calculation & checkout
```

### Source Code (`Commerce.sln`)
```text
Commerce.Domain/
├── Entities/
│   ├── SubOrder.cs                      # Existing CalculateFinancials method enforces invariant
│   └── Order.cs                         # Master order financial aggregate
└── Enums/
    └── PaymentMethod.cs                 # Wallet = 0, VnPay = 1 (Deprecated), Cod = 2

Commerce.Application/
├── Common/
│   └── Models/                          # DTOs: PlatformFeeResponse, UpdatePlatformFeeRequest
├── Interfaces/
│   ├── ICommerceDbContext.cs           # Direct EF Core context interface
│   ├── IWalletClient.cs                # Cleansed of dead VNPay direct checkout methods
│   └── IVoucherDiscountCalculator.cs   # Extensibility abstraction for voucher calculation
└── Services/
    ├── Order/
    │   ├── IOrderService.cs
    │   └── OrderService.cs              # Integrates CalculateFinancials, fails-fast, blocks VNPay, handles COD refunds
    └── Discount/
        └── DefaultVoucherDiscountCalculator.cs # Zero-discount pass-through stub for Feature 024

Commerce.Infrastructure/
├── Persistence/
│   └── AppDbContext.cs
└── Services/
    └── Wallet/
        └── WalletClient.cs              # Removes 409 conflict swallow, removes dead VNPay methods

Commerce.Api/
├── Controllers/
│   ├── Admin/
│   │   └── AdminPlatformFeeController.cs # Staff-only fee management [RequireRole("staff", "admin")]
│   └── Public/
│       └── OrdersController.cs          # Checkout quote and order creation endpoints
├── Program.cs                           # Unregisters VnPayPaymentPollingWorker
├── Workers/
│   └── PaymentTimeoutWorker.cs          # Simplified cancellation without VNPay polling
└── appsettings.json                     # Added "PlatformFee:DefaultPercent": 0.0

tests/Commerce.UnitTests/
├── Application/
│   ├── OrderCheckoutTests.cs            # Tests for fail-closed wallet, rejected VNPay
│   ├── OrderSettlementRefundTests.cs    # Tests for COD refund bypass, exact escrow settlement
│   └── AdminPlatformFeeTests.cs         # Tests for staff RBAC on fee adjustment
└── Domain/
    └── FinancialInvariantTests.cs       # Tests for positive and negative PlatformFeeAmount
```

---

## 5. Implementation Phases

### Phase 0: Research & Technical Decisions (Complete)
- Investigated and documented technical approach in `research.md`.
- Designed runtime cache persistence for staff platform fee (avoiding DB migrations).
- Verified impact of removing `VnPayPaymentPollingWorker` (confined to 7 files, zero external breakage).
- Defined mathematical boundaries for positive and negative platform fee margins.

### Phase 1: Design, Contracts & Data Model (Complete)
- Established data relationships and invariant equations in `data-model.md`.
- Generated OpenAPI contracts in `contracts/admin-platform-fee-api.yaml` and `contracts/order-financial-api.yaml`.
- Documented testing procedures and verification walkthrough in `quickstart.md`.

### Phase 2: Implementation Strategy (Ready for `/speckit-tasks`)
1. **Core Financial Engine & Configuration**:
   - Update `appsettings.json` with `PlatformFee:DefaultPercent: 0.0`.
   - Implement `AdminPlatformFeeController` with `[RequireRole("staff", "admin")]` and distributed cache backing.
   - Introduce `IVoucherDiscountCalculator` and `DefaultVoucherDiscountCalculator`.
2. **OrderService Hardening & VNPay Decommissioning**:
   - In `CreateOrderAsync`: Reject `PaymentMethod.VnPay` with 400 `UNSUPPORTED_PAYMENT_METHOD`.
   - In `CreateOrderAsync`: Fail fast on `INSUFFICIENT_BALANCE` without deleting cart items.
   - In `CreateOrderAsync`: Invoke `subOrder.CalculateFinancials` with dynamic fee percentage.
   - In `CancelOrderAsync` & `SellerRejectSubOrderAsync`: Skip wallet refund for `PaymentMethod.Cod`.
3. **Background Worker & Client Cleansing**:
   - Remove `VnPayPaymentPollingWorker` from `Program.cs` and delete files.
   - Cleanse `WalletClient.cs` of 409 conflict swallowing and dead VNPay methods.
4. **Verification & Regression Testing**:
   - Implement unit tests for `AdminPlatformFeeController`, fail-closed wallet, COD refund bypass, and rejected VNPay.
   - Compile `dotnet build Commerce.sln -c Release` (0 errors, 0 warnings).
   - Execute full test suite via `dotnet test Commerce.sln -c Release --no-build` (100% pass).
