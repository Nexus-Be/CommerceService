# Feature Specification: Stock Consistency & Single Source of Truth Alignment (FulfillmentService)

**Feature Branch**: `007-fix-stock-consistency`  
**Created**: 2026-11-02  
**Status**: Ready for Implementation  
**Input**: Triển khai speckit xử lý lỗi bất đồng bộ và thiếu hụt kiểm tra tồn kho tại Commerce Cart module.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Validate Cart with Real-time Warehouse Inventory (Priority: P1) 🎯 MVP

As a buyer reviewing my selected items before checkout, I want the system to check real-time available inventory against the physical warehouse (FulfillmentService) so that I am immediately notified of out-of-stock items before placing an order.

**Why this priority**: Prevents cart validation from passing with stale catalog metadata only to fail during the checkout order creation step.

**Independent Test**:
Call `POST /api/v1/carts/me/validate` with selected items. Mock FulfillmentService returning `availableStock: 0` for SKU A. Verify response has `isValid: false`, `hasStockIssues: true`, and an issue entry with `availableStock: 0`, `issueType: "OUT_OF_STOCK"`.

**Acceptance Scenarios**:
1. **Given** 4 variant SKUs in cart and SKU D has 0 stock in warehouse, **When** buyer validates cart, **Then** `ValidateCartAsync` reports `isValid: false` and lists SKU D as `OUT_OF_STOCK` with `suggestedQuantity: null`.
2. **Given** a variant SKU has quantity 5 in cart but warehouse only has 3, **When** buyer validates cart, **Then** `ValidateCartAsync` reports `INSUFFICIENT_STOCK` with `availableStock: 3`, `requestedQuantity: 5`, and `suggestedQuantity: 3`.
3. **Given** all selected items have sufficient warehouse stock, **When** buyer validates cart, **Then** `ValidateCartAsync` returns `isValid: true` and `issues: []`.

---

### User Story 2 - Accurate Cart Calculation Excluding Out-of-stock Items (Priority: P2)

As a buyer selecting a subset of items to purchase, I want `CalculateSelectedItemsAsync` to check inventory status and clearly distinguish items that are available from items that are out of stock, calculating the subtotal only on available items.

**Why this priority**: Avoids misleading price calculations when some selected items are out of stock.

**Independent Test**:
Call `POST /api/v1/carts/me/calculate` with 2 items (Item A = 100k, Item B = 50k out-of-stock). Verify Item B has `isAvailable: false`, and `subtotalAmount` equals 100k (only Item A).

**Acceptance Scenarios**:
1. **Given** 2 selected items where 1 is out of stock in warehouse, **When** calculate is requested, **Then** out-of-stock item has `isAvailable: false`, and `subtotalAmount` only sums available items.

---

### User Story 3 - Stock Validation on Variant Change & Guest Cart Merge (Priority: P3)

As a buyer changing a cart item's variant or logging in to merge guest cart items, I want the system to check warehouse stock availability so that out-of-stock variants cannot be chosen and out-of-stock guest items do not contaminate the active cart.

**Why this priority**: Enforces invariant validation on all cart modification entry points.

**Independent Test**:
Call `PUT /api/v1/carts/me/items/{id}/variant` for a variant with 0 warehouse stock. Verify system throws `INSUFFICIENT_STOCK` (HTTP 400).

**Acceptance Scenarios**:
1. **Given** a buyer requests to change SKU to an out-of-stock variant, **When** `ChangeItemVariantAsync` is invoked, **Then** system throws `CommerceDomainException("INSUFFICIENT_STOCK", ...)` with HTTP 400.
2. **Given** a guest cart contains an out-of-stock item, **When** `MergeGuestCartAsync` is invoked, **Then** the out-of-stock item is omitted from active cart.

---

### User Story 4 - Environment-aware Stock Fallback Policy (Priority: P4)

As a system operator, I want the FulfillmentClient to fail-closed in Production (refusing checkout/validation if inventory service is down) while allowing fail-open in Development mode for offline developer ergonomics.

**Why this priority**: Prevents catastrophic overselling in production when network hiccups occur.

**Independent Test**:
Configure `StockPolicy:FailOpen = false`. Cause connection failure to FulfillmentService. Verify exception `STOCK_CHECK_UNAVAILABLE` (HTTP 503) is raised instead of silently returning 100 available stock.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `ValidateCartAsync` MUST use `IFulfillmentClient.BatchCheckStockAsync` as the single source of truth for stock quantities and availability.
- **FR-002**: `ValidateCartAsync` MUST use `ICatalogClient.GetSkuDetailsAsync` solely for product metadata verification (purchasability, product name, seller ID, unit price drift).
- **FR-003**: `ValidateCartAsync` MUST include `suggestedQuantity` in `CartItemIssue` when partial stock is available.
- **FR-004**: `CalculateSelectedItemsAsync` MUST batch-check warehouse stock and mark `isAvailable: false` on items exceeding available stock, computing `subtotalAmount` solely from available items.
- **FR-005**: `ChangeItemVariantAsync` MUST verify warehouse stock availability with `IFulfillmentClient.CheckStockAvailabilityAsync` before applying variant replacement.
- **FR-006**: `MergeGuestCartAsync` MUST verify warehouse stock availability for each guest item before merging.
- **FR-007**: `FulfillmentClient` MUST respect `StockPolicy:FailOpen` configuration, failing closed in Production environments when FulfillmentService cannot be reached.

---

## Success Criteria *(mandatory)*

- **SC-001**: Cart validation matches 100% with physical warehouse stock in FulfillmentService.
- **SC-002**: Zero regression on existing cart and order unit tests.
- **SC-003**: Full build passes with 0 errors (`dotnet build Commerce.sln -c Release`).
