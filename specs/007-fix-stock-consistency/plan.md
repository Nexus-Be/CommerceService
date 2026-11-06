# Implementation Plan: Stock Consistency & Single Source of Truth Alignment

**Feature Branch**: `007-fix-stock-consistency`  
**Created**: 2026-11-02  
**Status**: In Progress  

---

## Technical Context

- **Service**: CommerceService (Cart & Order Module)
- **Framework**: .NET 8, C# 12, ASP.NET Core Web API
- **Architecture**: Clean Architecture, Inward-Only, No Repository over EF Core
- **Dependencies**:
  - `ICatalogClient`: Product metadata lookup (Sku, Seller, Price, Purchasability)
  - `IFulfillmentClient`: Physical inventory check, batch check, reservation, commit, release
- **Single Source of Truth Decision**:
  - `FulfillmentService` (`IFulfillmentClient` via `http://localhost:8005/`) is the authoritative source for inventory.
  - `CatalogService` (`ICatalogClient` via `http://localhost:8002/`) is the source for static catalog data and price snapshots.

---

## Architecture Alignment & Constitution Checks

1. **Clean Architecture**: Changes are confined to `Commerce.Application` (Services & DTOs) and `Commerce.Infrastructure` (Client resilience).
2. **REST API Contract**: Responses are backward-compatible with default/optional parameters.
3. **No Repository Pattern**: Direct usage of `ICommerceDbContext` and clients.
4. **Error Handling**: 100% English, Sentence case messages, SCREAMING_SNAKE_CASE error codes (`INSUFFICIENT_STOCK`, `OUT_OF_STOCK`, `STOCK_CHECK_UNAVAILABLE`).

---

## Implementation Details

### Phase 1: Contract & DTO Updates
- In `Commerce.Application/Services/Cart/CartResponses.cs`:
  - Update `CartItemIssue` record to include `int? SuggestedQuantity = null`.
  - Update `CalculatedCartItem` record to include `bool IsAvailable = true, int? AvailableStock = null, string? StockWarning = null`.

### Phase 2: Application Service Modifications
- In `Commerce.Application/Services/Cart/CartService.cs`:
  - `ValidateCartAsync`: Add `_fulfillmentClient.BatchCheckStockAsync(skuIds)` call. Compare items against warehouse stock instead of catalog `StockQuantity`. Compute `suggestedQuantity`.
  - `CalculateSelectedItemsAsync`: Add `CheckStockForCartItemsAsync` call. Flag unavailable items and exclude from `SubtotalAmount`.
  - `ChangeItemVariantAsync`: Call `_fulfillmentClient.CheckStockAvailabilityAsync(newSkuId, quantity)`.
  - `MergeGuestCartAsync`: Check `_fulfillmentClient.CheckStockAvailabilityAsync(skuId, quantity)`.

### Phase 3: Infrastructure Resilience
- In `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs`:
  - Introduce `StockPolicyOptions` / `IConfiguration` reading `StockPolicy:FailOpen`.
  - Fail-closed in Production when configured (`FailOpen: false`).
- Update `appsettings.json` and `appsettings.Development.json`.

### Phase 4: Unit Testing & Verification
- Add unit tests in `tests/Commerce.UnitTests/Application/CartServiceTests.cs`:
  - `ValidateCartAsync_ShouldReturnFulfillmentStockIssue_WhenWarehouseStockInsufficient`
  - `CalculateSelectedItemsAsync_ShouldExcludeOutOfStockItemsFromSubtotal`
  - `ChangeItemVariantAsync_ShouldThrowException_WhenWarehouseStockInsufficient`
- Verify with `dotnet test Commerce.sln -c Release`.
