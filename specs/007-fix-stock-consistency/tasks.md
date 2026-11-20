# Tasks: Stock Consistency & Single Source of Truth Alignment

**Input**: Design documents from `/specs/007-fix-stock-consistency/`  
**Prerequisites**: `spec.md`, `plan.md`  

---

## Phase 1: Contract & DTO Enhancement
- [x] T001 Update `CartItemIssue` and `CalculatedCartItem` in `Commerce.Application/Services/Cart/CartResponses.cs` with stock validation fields.

## Phase 2: Application Implementation
- [x] T002 [US1] Update `ValidateCartAsync` in `Commerce.Application/Services/Cart/CartService.cs` to batch check stock with `_fulfillmentClient.BatchCheckStockAsync`.
- [x] T003 [US2] Update `CalculateSelectedItemsAsync` in `Commerce.Application/Services/Cart/CartService.cs` to check stock availability and exclude out-of-stock items from `SubtotalAmount`.
- [x] T004 [US3] Update `ChangeItemVariantAsync` and `MergeGuestCartAsync` in `Commerce.Application/Services/Cart/CartService.cs` to check stock with `_fulfillmentClient.CheckStockAvailabilityAsync`.

## Phase 3: Infrastructure Configuration
- [x] T005 [US4] Update `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs` and configuration files with `StockPolicy:FailOpen`.

## Phase 4: Verification & Tests
- [x] T006 Add unit tests for `ValidateCartAsync`, `CalculateSelectedItemsAsync`, and `ChangeItemVariantAsync` in `tests/Commerce.UnitTests/Application/CartServiceTests.cs`.
- [x] T007 Run `dotnet build Commerce.sln -c Release` and `dotnet test Commerce.sln -c Release`.
