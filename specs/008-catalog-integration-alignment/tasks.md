# Tasks: Catalog Service Alignment & Batch Variant Optimization

**Feature Directory**: `specs/008-catalog-integration-alignment`  
**Prerequisites**: `spec.md`, `plan.md`  

---

## Phase 1: Contract & Interface Enhancement
- [x] T001 [FR-001, FR-002, FR-008] Update `Commerce.Application/Interfaces/ICatalogClient.cs`:
  - Define `CatalogVariantSwitchResult`.
  - Add `GetSkuBatchDetailsAsync` and `ValidateVariantSwitchAsync`.
  - Remove deprecated `ReserveStockAsync`.

## Phase 2: Infrastructure Implementation
- [x] T002 [FR-003, FR-004, FR-005] Update `Commerce.Infrastructure/Services/Catalog/CatalogClient.cs`:
  - Implement `GetSkuBatchDetailsAsync` calling `POST api/v1/internal/variants` with chunking (max 100).
  - Calculate `IsPurchasable` from `status == "Active" && productStatus == "Active" && price > 0`.
  - Re-implement `GetSkuDetailsAsync` to call `GetSkuBatchDetailsAsync` with single item.
  - Implement `ValidateVariantSwitchAsync` calling `POST api/v1/internal/catalog/skus/validate-variant-switch`.
  - Remove `ReserveStockAsync`.

## Phase 3: Application Services Integration
- [x] T003 [FR-006] Update `ChangeItemVariantAsync` in `Commerce.Application/Services/Cart/CartService.cs` to invoke `_catalogClient.ValidateVariantSwitchAsync`.
- [x] T004 [FR-007] Update `CreateOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` to fetch fresh SKU prices via `_catalogClient.GetSkuBatchDetailsAsync` in a single batch call.

## Phase 4: Test Suite Synchronization & Verification
- [x] T005 Update unit test mocks in `CartServiceTests.cs`, `OrderServiceTests.cs`, `OrderCheckoutTests.cs`, `EndToEndFlowTests.cs`, and `ExternalClientFaultTests.cs`.
- [x] T006 Add unit tests for `GetSkuBatchDetailsAsync` and `ValidateVariantSwitchAsync`.
- [x] T007 Run `dotnet build Commerce.sln -c Release` and `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release` ensuring 100% pass rate.
