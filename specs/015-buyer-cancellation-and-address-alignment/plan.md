# Implementation Plan: Buyer Order Cancellation with Automated Wallet Refund & 2-Tier Administrative Address Normalization

**Branch/Directory**: `specs/015-buyer-cancellation-and-address-alignment` | **Date**: 2026-11-09  
**Spec Reference**: [specs/015-buyer-cancellation-and-address-alignment/spec.md](spec.md)  
**Input**: Feature specification from `spec.md`  

---

## 1. Summary

This feature resolves two critical production issues in `CommerceService`:
1. **Buyer Order Cancellation for Paid Orders & Automated Refund**:
   - Expands `CancelOrderAsync` to permit cancellations when `order.Status == OrderStatus.Paid` (as well as `PendingPayment`), as long as no sub-order is in `Shipping`, `Delivered`, or `Completed`.
   - Executes automated wallet refund via `_walletClient.RefundSubOrderPaymentAsync(...)` with idempotency key `$"refund-suborder-cancel-{so.Id}"`.
   - Records `OrderCancellation` (`WasPaid = true`) and `OrderRefund` (updating to `RefundStatus.Completed` on success).
   - Releases reserved stock via `_fulfillmentClient.ReleaseStockAsync(order.Id)`.
2. **2-Tier Administrative Address Normalization**:
   - Updates `ShippingAddressInput` record to make `District` nullable with a default value (`string? District = null`), avoiding breaking positional callers in tests.
   - Adjusts `QuoteCheckoutAsync` to validate only 5 mandatory fields (`ReceiverName`, `ContactPhone`, `Street`, `Province`, `Ward`), removing the mandatory check for `District`.
   - Updates `ShippingAddressSnapshot` to accept `string? district` and safely concatenate `FullAddressText` using non-empty parts, avoiding double-comma artifacts `, ,`.
   - Expands `OrdersController.CancelOrder` with `[HttpPut("{orderId:guid}/cancel")]` and optional request body defaulting to `BuyerChangedMind`.

---

## 2. Technical Context

- **Language / Framework**: C# 12 / .NET 8 LTS (`Commerce.sln` targeting net8.0)
- **Primary Dependencies**:
  - `Microsoft.EntityFrameworkCore` & `Npgsql.EntityFrameworkCore.PostgreSQL` 8.x
  - `Microsoft.AspNetCore.Mvc` & `System.Text.Json`
  - `FluentAssertions`, `Moq`, `xunit` (test projects)
- **Database / Cache**: PostgreSQL `5435` (`commerce_db`), Redis Distributed Cache
- **Database Migration**: **NONE REQUIRED (Zero Migration)**. `ShippingAddressSnapshot` is an EF Core 8 owned JSON entity (`ToJson()`).
- **Target Microservice**: `Commerce.Api` (Port `8003`)
- **Blast Radius**:
  - `Commerce.Domain/ValueObjects/ShippingAddressSnapshot.cs`
  - `Commerce.Application/Services/Order/OrderRequests.cs`
  - `Commerce.Application/Services/Order/OrderService.cs`
  - `Commerce.Api/Controllers/Public/OrdersController.cs`
  - `tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs`

---

## 3. Constitution Check

*GATE: Must pass before execution.*

| Principle / Rule | Compliance Status | Analysis & Verification |
| :--- | :---: | :--- |
| **I. Clean Architecture & Direct EF Core Access** | **PASS** | Domain value object remains pure. Application logic co-located in `Commerce.Application/Services/Order/`. Direct access via `ICommerceDbContext`. Zero repository pattern. |
| **II. Synchronous Typed REST Orchestration** | **PASS** | Outbound calls to Wallet (`IWalletClient.RefundSubOrderPaymentAsync`) and Fulfillment (`IFulfillmentClient.ReleaseStockAsync`) use typed HTTP clients. Zero message brokers. |
| **III. Immutable Infrastructure & REST Standards** | **PASS** | Runs on port `8003`, connects to `commerce_db:5435`. Expands route ergonomics (`POST` and `PUT` on `/api/v1/orders/{orderId}/cancel`). Mandatory idempotency key on refund. |
| **IV. Universal Error Protocol** | **PASS** | 100% English Sentence-case error messages (`"Order cannot be cancelled once shipped, delivered, or completed."`). Error codes in `SCREAMING_SNAKE_CASE` (`ORDER_CANNOT_BE_CANCELLED`, `FORBIDDEN`). |
| **V. Inward-Only Boundaries** | **PASS** | 100% self-contained within `d:\CommerceService`. No cross-repo paths. |
| **DoD Verification Gate** | **PASS** | Solution must build cleanly (`dotnet build Commerce.sln -c Release`) with 0 warnings, 0 errors, and pass all unit tests. |

---

## 4. Technical Decisions & Architectural Rationale

### Decision 1: Cancellation Eligibility Condition
- **Decision**: Permit cancellation when `(order.Status == OrderStatus.PendingPayment || order.Status == OrderStatus.Paid)` AND no sub-order has `so.Status == SubOrderStatus.Shipping || so.Status == SubOrderStatus.Delivered || so.Status == SubOrderStatus.Completed`.
- **Rationale**: In Nexus E-commerce, Nexus Wallet orders transition to `Paid` immediately upon creation. Blocking cancellation on `Paid` orders traps buyer funds and creates extreme customer support friction when goods haven't even been picked or packed.

### Decision 2: Automated Idempotent Wallet Refund
- **Decision**: Loop over paid sub-orders (`so.PaidAt.HasValue || so.Status == SubOrderStatus.Paid || so.Status == SubOrderStatus.Confirmed`), create `OrderRefund` with `Status = RefundStatus.Requested`, invoke `_walletClient.RefundSubOrderPaymentAsync` with `idempotencyKey: $"refund-suborder-cancel-{so.Id}"`, and update `refund.Status = RefundStatus.Completed` on success. Wrap in try/catch to ensure database persistence and stock release still proceed even if wallet network temporarily blips.
- **Rationale**: Protects buyer balances automatically while providing resilient audit logging for financial reconciliation.

### Decision 3: Backward-Compatible `ShippingAddressInput` Constructor
- **Decision**: Declare `string? District = null, string Province = ""` in `ShippingAddressInput` record.
- **Rationale**: Positional unit test constructor invocations with 8 arguments (where `"District"` is argument 5 and `"Province"` is argument 6) remain 100% compatible and compile without code changes, while JSON deserializers can safely omit `district`.

### Decision 4: Smart Address Concatenation
- **Decision**: In `ShippingAddressSnapshot`, use:
  ```csharp
  var parts = new[] { street, ward, district, province }.Where(p => !string.IsNullOrWhiteSpace(p));
  FullAddressText = string.Join(", ", parts);
  ```
- **Rationale**: Safely handles cases where `district` is null, whitespace, or empty, preventing double commas (e.g. `123 Le Loi, Phường Bến Nghé, , TP. Hồ Chí Minh` $\rightarrow$ `123 Le Loi, Phường Bến Nghé, TP. Hồ Chí Minh`).

---

## 5. Verification Checklist

1. `dotnet build Commerce.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false` must compile with 0 errors and 0 warnings.
2. `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore --no-build` passes with 100% success.
3. Test case `CancelOrderAsync_ShouldCancelOrderAndRefund_WhenOrderIsPaidAndNotShipped` verifies wallet refund invocation.
4. Test case `CancelOrderAsync_ShouldThrowException_WhenSubOrderIsAlreadyShipped` verifies strict guard against cancelling in-flight packages.
