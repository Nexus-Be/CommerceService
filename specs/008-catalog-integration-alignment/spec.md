# Feature Specification: Catalog Service REST API Alignment & Batch Variant Optimization

**Feature Branch**: `008-catalog-integration-alignment`  
**Created**: 2026-11-03  
**Status**: Ready for Implementation  
**Input**: Phản hồi kỹ thuật chính thức từ Catalog Service (Port 8002): Triển khai Batch API `POST /api/v1/internal/variants`, Xác thực đổi Variant `POST /api/v1/internal/catalog/skus/validate-variant-switch`, map các trường `sellerId`, `productId`, `productName`, `primaryImageUrl`, logic xác định `isPurchasable` tạm thời từ `status` & `productStatus`, và bãi bỏ triệt để giữ kho tại Catalog.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Batch SKU Details Lookup (Chống N+1 Query) (Priority: P1) 🎯 MVP

As a buyer reviewing my cart or placing an order containing items from multiple sellers, I want CommerceService to fetch real-time catalog prices and metadata for all cart items in a single batch HTTP request, so that network latency is minimized and checkout remains fast and responsive.

**Why this priority**: Calling Catalog individually for every cart item creates an N+1 network latency bottleneck during cart view, quote calculation, and checkout order creation.

**Independent Test**:
Call `ICatalogClient.GetSkuBatchDetailsAsync` with a list of 5 SKU IDs. Verify that a single `POST /api/v1/internal/variants` request with payload `{"variantIds": [...]}` is dispatched to CatalogService, returning all matching variants mapped into `CatalogProductCheckResult` with `IsPurchasable` calculated correctly.

**Acceptance Scenarios**:
1. **Given** a list of valid SKU IDs, **When** CommerceService requests batch details, **Then** CatalogClient calls `POST /api/v1/internal/variants` and returns items with `SkuId`, `ProductId`, `SellerId`, `ProductName`, `UnitPrice`, and `IsPurchasable`.
2. **Given** some SKU IDs in the request do not exist in Catalog, **When** Catalog returns `notFoundIds`, **Then** CommerceService marks them as not found and does not crash or fail the entire batch.
3. **Given** a variant has `status == "Active"`, `productStatus == "Active"`, and `price > 0`, **When** evaluated, **Then** `IsPurchasable` is evaluated to `true`. Otherwise, `IsPurchasable` is evaluated to `false`.

---

### User Story 2 - Server-Side Variant Switch Validation (Priority: P2)

As a buyer choosing a different size or color for an item directly in my cart, I want CommerceService to validate with CatalogService that the new variant belongs to the exact same parent product and seller, so that I cannot fraudulently switch an inexpensive item to an entirely different expensive product.

**Why this priority**: Prevents cart tampering and guarantees price snapshot accuracy on variant switches.

**Independent Test**:
Call `POST /api/v1/carts/me/items/{cartItemId}/variant` with `newSkuId`. Verify that CommerceService calls Catalog's `POST /api/v1/internal/catalog/skus/validate-variant-switch`. If the new SKU does not belong to the same parent product, Catalog returns HTTP 400 `CATALOG_VARIANT_MISMATCH`, and Commerce propagates the domain exception to the client.

**Acceptance Scenarios**:
1. **Given** a buyer switches from "Shirt Black M" to "Shirt White L" of the same product, **When** `ChangeItemVariantAsync` is called, **Then** Catalog returns `isValid: true`, and the cart item is updated with the new SKU and fresh `UnitPriceSnapshot`.
2. **Given** a buyer attempts to switch from "Shirt" to "Laptop" (different parent product), **When** `ChangeItemVariantAsync` is called, **Then** Catalog returns HTTP 400 `CATALOG_VARIANT_MISMATCH`, and Commerce throws `CommerceDomainException("CATALOG_VARIANT_MISMATCH", ...)`.

---

### User Story 3 - Single SKU Lookup via Batch Endpoint (Priority: P3)

As CommerceService needing to fetch details for a single SKU (e.g. during `AddItemAsync`), I want `GetSkuDetailsAsync` to call `POST /api/v1/internal/variants` with a 1-element array, so that I immediately receive `sellerId`, `productId`, `productName`, and image URL without waiting for Catalog to update their legacy single-item endpoint.

**Why this priority**: Guarantees `sellerId` and `productId` are always populated for any SKU lookup in Commerce.

**Independent Test**:
Call `GetSkuDetailsAsync(skuId)`. Verify that it requests `{"variantIds": [skuId]}` and extracts the first matching item into `CatalogProductCheckResult`.

---

### User Story 4 - Deprecation of Catalog Stock Reservation (Priority: P4)

As a system architect, I want all stock reservation and inventory commitments to be solely handled by `FulfillmentService` (`:8005`), and any legacy stock reservation methods on `ICatalogClient` to be removed, so that Catalog remains strictly a Master Data service.

**Why this priority**: Eliminates dead code, enforces Clean Architecture boundary, and aligns with Catalog's official retirement of `POST /api/v1/products/reserve`.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `ICatalogClient` MUST expose `Task<IReadOnlyDictionary<Guid, CatalogProductCheckResult>> GetSkuBatchDetailsAsync(IEnumerable<Guid> skuIds, CancellationToken cancellationToken = default)`.
- **FR-002**: `ICatalogClient` MUST expose `Task<CatalogVariantSwitchResult> ValidateVariantSwitchAsync(Guid currentSkuId, Guid newSkuId, CancellationToken cancellationToken = default)`.
- **FR-003**: `CatalogClient` MUST invoke `POST /api/v1/internal/variants` with payload `{"variantIds": [...]}` (chunking into batches of at most 100 IDs if count exceeds 100).
- **FR-004**: `CatalogClient` MUST calculate `IsPurchasable = (item.Status == "Active" && item.ProductStatus == "Active" && item.Price > 0)`.
- **FR-005**: `CatalogClient.GetSkuDetailsAsync` MUST reuse `GetSkuBatchDetailsAsync` with a single ID to ensure `SellerId`, `ProductId`, and `ProductName` are always present.
- **FR-006**: `CartService.ChangeItemVariantAsync` MUST call `_catalogClient.ValidateVariantSwitchAsync` to enforce parent product consistency.
- **FR-007**: `OrderService.CreateOrderAsync` MUST fetch fresh prices for all order items in a single batch call via `_catalogClient.GetSkuBatchDetailsAsync`.
- **FR-008**: `ReserveStockAsync` MUST be removed from `ICatalogClient` and `CatalogClient`.
