# Feature Specification: Fulfillment Route Alignment and Cart Batch Optimization

**Feature Branch**: `009-fulfillment-and-cart-optimization`  
**Created**: 2026-11-03  
**Status**: Completed  
**Input**: Phản hồi kỹ thuật từ FE/Reviewer: Cập nhật route của Fulfillment Service (`GET /api/v1/inventories/{skuId}` và `POST /api/v1/inventories/by-skus`), loại bỏ fallback giả lập che giấu lỗi, và tối ưu hóa song song (parallel Task.WhenAll) triệt tiêu N+1 trong `ValidateCartAsync`.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Fulfillment Inventory Real Route Alignment (Priority: P1) 🎯 MVP

As a buyer adding an item to my cart or viewing my cart, I want CommerceService to query the actual live inventory routes exposed by FulfillmentService (`GET /api/v1/inventories/{skuId}` and `POST /api/v1/inventories/by-skus`), so that real warehouse stock numbers are verified without receiving 404 Route Not Found errors.

**Why this priority**: Calling invalid routes (`api/v1/fulfillment/inventory/check`) results in 404s, triggering mock fallbacks that hide network/stock issues.

**Independent Test**:
Call `IFulfillmentClient.CheckStockAvailabilityAsync(skuId, quantity)` and `IFulfillmentClient.BatchCheckStockAsync(skuIds)`. Verify that requests are sent to `GET /api/v1/inventories/{skuId}` and `POST /api/v1/inventories/by-skus` respectively, correctly parsing available quantities.

**Acceptance Scenarios**:
1. **Given** a SKU exists in warehouse with stock = 5, **When** `CheckStockAvailabilityAsync` is called with quantity = 2, **Then** it requests `GET /api/v1/inventories/{skuId}` and returns `true`.
2. **Given** a list of SKU IDs, **When** `BatchCheckStockAsync` is called, **Then** it requests `POST /api/v1/inventories/by-skus` with `{ "skuIds": [...] }` and maps the returned stock quantities accurately.
3. **Given** Fulfillment Service is down and `FailOpen == false`, **When** check stock fails, **Then** a `CommerceDomainException("STOCK_CHECK_UNAVAILABLE", ...)` with HTTP 503 is thrown instead of masking it with fake stock.

---

### User Story 2 - Cart Validation Parallel Batch Optimization (Chống N+1) (Priority: P2)

As a buyer validating my selected cart items before checkout, I want CommerceService to validate SKU availability and warehouse stock concurrently using batch endpoints, so that cart validation responds in minimal time without sequential N+1 HTTP calls.

**Why this priority**: Eliminates sequential HTTP loops ($N$ Catalog requests after Fulfillment request), reducing pre-checkout latency by ~50%.

**Independent Test**:
Call `POST /api/v1/carts/me/validate` on a cart with 5 items. Verify that `_catalogClient.GetSkuBatchDetailsAsync` and `_fulfillmentClient.BatchCheckStockAsync` are executed concurrently via `Task.WhenAll`, producing an accurate validation report.

**Acceptance Scenarios**:
1. **Given** a cart with multiple items, **When** `ValidateCartAsync` is invoked, **Then** stock check and catalog batch lookups run in parallel via `Task.WhenAll`.
2. **Given** a product price changed in Catalog, **When** validated, **Then** `ValidateCartResponse` flags `PRICE_CHANGED` with updated price info.
3. **Given** a SKU is out of stock in Fulfillment, **When** validated, **Then** `ValidateCartResponse` flags `OUT_OF_STOCK` or `INSUFFICIENT_STOCK`.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `FulfillmentClient.CheckStockAvailabilityAsync` MUST query `GET api/v1/inventories/{skuId}` and verify if `availableStock >= quantity`.
- **FR-002**: `FulfillmentClient.BatchCheckStockAsync` MUST query `POST api/v1/inventories/by-skus` with body `{ "skuIds": skuIds }`.
- **FR-003**: `FulfillmentClient` MUST parse both camelCase and PascalCase variations of inventory response schemas (`availableStock`, `availableQuantity`, `quantity`, `reservedQuantity`, `isAvailable`).
- **FR-004**: When `StockPolicy:FailOpen` is `false`, `FulfillmentClient` MUST throw `CommerceDomainException("STOCK_CHECK_UNAVAILABLE", "Warehouse inventory service is temporarily unreachable.", 503)` upon network or 5xx failures.
- **FR-005**: `CartService.ValidateCartAsync` MUST invoke `_catalogClient.GetSkuBatchDetailsAsync` instead of looping with individual `GetSkuDetailsAsync` calls.
- **FR-006**: `CartService.ValidateCartAsync` MUST execute `BatchCheckStockAsync` and `GetSkuBatchDetailsAsync` concurrently using `Task.WhenAll`.
