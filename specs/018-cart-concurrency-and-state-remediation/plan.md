# Implementation Plan: Cart State Tracking Remediation & Concurrency Standardization

**Branch**: `018-cart-concurrency-and-state-remediation` | **Date**: 2026-11-10 | **Spec**: [specs/018-cart-concurrency-and-state-remediation/spec.md](spec.md)

**Input**: Feature specification from `/specs/018-cart-concurrency-and-state-remediation/spec.md`

---

## Summary

This feature resolves the root cause of the 500 Internal Server Error when adding products to an existing shopping cart (`POST /api/v1/carts/me/items`) and standardizes concurrency protocols across CommerceService:
1. **Explicit EntityState.Added Enforcement**: Eliminates premature `DetectChanges()` triggered by `_dbContext.CartItems.Local.Any(...)`. Checks `cart.Items.Any(i => i.SkuId == skuInfo.SkuId)` upfront, and calls `_dbContext.CartItems.Add(cartItem)` for new items to force `EntityState.Added` instead of `EntityState.Modified`.
2. **Guest Cart Merge Synchronization**: Implements the identical `isExistingItem` check and explicit `CartItems.Add` in `MergeGuestCartAsync` to eliminate 500 errors during account sign-in.
3. **Double-Click Race Condition Mitigation**: Catches `DbUpdateException` resulting from unique index `IX_CartItems_CartId_SkuId` collisions during rapid duplicate clicks, reloads the cart, and accumulates quantity safely.
4. **RFC 9110 Concurrency Conflict Mapping**: Maps `Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException` explicitly to HTTP 409 Conflict (`CONCURRENCY_CONFLICT`) in `GlobalExceptionHandlerMiddleware`.
5. **RowVersion Integrity Alignment**: Increments `cart.RowVersion++` in `ValidateCartAsync` whenever catalog prices drift and cart subtotal amounts are adjusted.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (`Commerce.sln` targeting net8.0)  
**Primary Dependencies**:
- `Microsoft.EntityFrameworkCore` & `Npgsql.EntityFrameworkCore.PostgreSQL` 8.x
- `Microsoft.AspNetCore.Mvc` (Thin controller architecture)
- `xUnit`, `Moq`, `FluentAssertions` (Test framework)  
**Storage**: PostgreSQL `5435` (`commerce_db`), table `Carts`, `CartItems`  
**Testing**: `dotnet test Commerce.sln -c Release`  
**Target Platform**: Linux Container (Port `8003`) & Local Windows Development  
**Project Type**: ASP.NET Core RESTful Web API Service  
**Performance Goals**: Sub-50ms cart item addition, 0 concurrency deadlocks, 100% absorption of rapid double clicks  
**Constraints**:
- Strict Clean Architecture with direct `ICommerceDbContext` interaction (zero Repository/UoW wrappers).
- 100% English sentence-case error messages and `SCREAMING_SNAKE_CASE` error codes.
- Problem Details RFC 9110 conformity across all error branches.
- Inward-only referencing: self-contained within `d:\CommerceService`.  
**Scale/Scope**: 2 modified application & middleware files (`CartService.cs`, `GlobalExceptionHandlerMiddleware.cs`), 1 test file updated (`CartServiceTests.cs`).

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-evaluated post-design.*

| Principle / Rule | Compliance Status | Analysis & Verification |
| :--- | :---: | :--- |
| **I. Clean Architecture & Direct EF Core Access** | **PASS** | `CartService` interacts directly with `ICommerceDbContext` and `DbSet<CartItem>`. No Repository or Unit of Work layer introduced. |
| **II. Synchronous Typed REST Orchestration** | **PASS** | Inter-service dependencies remain purely typed HTTP (`ICatalogClient`, `IFulfillmentClient`). No message brokers or sagas. |
| **III. Immutable Infrastructure & REST Standards** | **PASS** | Port `8003`, DB `5435`. All routes follow plural conventions (`/api/v1/carts/me/items`). Concurrency mapping aligned with RFC 9110 Section 15.5.10. |
| **IV. Universal Error Protocol** | **PASS** | Error codes are `SCREAMING_SNAKE_CASE` (`CONCURRENCY_CONFLICT`, `VAL_INVALID_INPUT`). Messages are 100% English Sentence-case. |
| **V. Inward-Only Boundaries** | **PASS** | Self-contained within `CommerceService`. No outward root or sibling directory referencing. |
| **DoD Verification Gate** | **PASS** | Solution builds with 0 errors in Release mode (`dotnet build Commerce.sln -c Release`). |

---

## Project Structure

### Documentation & SpecKit Artifacts (this feature)

```text
specs/018-cart-concurrency-and-state-remediation/
├── spec.md              # Feature specification with 5 prioritized User Stories (P1/P2)
├── plan.md              # This implementation plan file
├── research.md          # Phase 0: Technical decisions on ChangeTracker heuristics and concurrency mapping
├── data-model.md        # Phase 1: Entity attributes, ChangeTracker state machine, and DTO definitions
├── quickstart.md        # Phase 1: Verification guide and manual scenario execution
└── contracts/           # Phase 1: API request/response specifications and error matrices
    └── cart-api-endpoints.md
```

### Concrete Source Code Layout & Blast Radius

```text
Commerce.Application/
└── Services/
    └── Cart/
        └── CartService.cs                         # Update AddItemAsync, MergeGuestCartAsync, and ValidateCartAsync

Commerce.Api/
└── Middleware/
    └── GlobalExceptionHandlerMiddleware.cs       # Map DbUpdateConcurrencyException to HTTP 409 CONCURRENCY_CONFLICT

tests/Commerce.UnitTests/
└── Application/
    └── CartServiceTests.cs                       # Add tests for existing cart additions, merge flows, and concurrency
```

---

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
| :--- | :--- | :--- |
| *None* | *All modifications strictly conform to Clean Architecture, EF Core conventions, and Constitution.* | *N/A* |
