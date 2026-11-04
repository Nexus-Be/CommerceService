# Tasks: Fulfillment Route Alignment and Cart Batch Optimization

**Feature Directory**: `specs/009-fulfillment-and-cart-optimization`  
**Prerequisites**: `spec.md`, `plan.md`  

---

## Phase 1: Infrastructure Fulfillment Client Alignment
- [x] T001 [FR-001, FR-002, FR-003, FR-004] Update `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs`:
  - Refactor `CheckStockAvailabilityAsync` to query `GET api/v1/inventories/{skuId}` and verify `availableStock >= quantity`.
  - Refactor `BatchCheckStockAsync` to query `POST api/v1/inventories/by-skus` with `{ "skuIds": skuIds }`.
  - Parse inventory responses flexibly (`availableStock`, `availableQuantity`, `quantity`, `reservedQuantity`, `isAvailable`).
  - When `_failOpen == false`, throw `CommerceDomainException("STOCK_CHECK_UNAVAILABLE", ...)` with HTTP 503 instead of masking errors.

## Phase 2: Application CartService Concurrent Batch Optimization
- [x] T002 [FR-005, FR-006] Update `ValidateCartAsync` in `Commerce.Application/Services/Cart/CartService.cs`:
  - Extract all distinct `skuIds`.
  - Launch `stockTask = _fulfillmentClient.BatchCheckStockAsync(skuIds, cancellationToken)` and `catalogTask = _catalogClient.GetSkuBatchDetailsAsync(skuIds, cancellationToken)`.
  - Await both concurrently with `await Task.WhenAll(stockTask, catalogTask)`.
  - Replace the sequential `foreach` with in-memory lookup from `catalogTask.Result` (with fallback to `GetSkuDetailsAsync` only if missing).

## Phase 3: Test Suite Verification & Synchronization
- [x] T003 Synchronize and verify unit tests in `tests/Commerce.UnitTests/Application/CartServiceTests.cs` to ensure `GetSkuBatchDetailsAsync` is mocked and parallel execution produces expected validation issues.
- [x] T004 Run `dotnet build Commerce.sln -c Release` and verify 0 warnings, 0 errors, and all 69 unit tests passing.
