# Feature Specification: Cart State Tracking Remediation & Concurrency Standardization

**Feature Branch**: `018-cart-concurrency-and-state-remediation`  
**Created**: 2026-11-10  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  
**Input**: User description: "Triển khai khắc phục triệt để lỗi 500 khi thêm giỏ hàng (POST /api/v1/carts/me/items) do EF Core Change Tracker nhận nhầm EntityState.Modified thay vì EntityState.Added; đồng bộ xử lý thêm mới đối với MergeGuestCartAsync; bọc chống lỗi 500 khi double-click trùng index IX_CartItems_CartId_SkuId; đồng bộ RowVersion trong ValidateCartAsync; và chuẩn hóa ngoại lệ DbUpdateConcurrencyException trong GlobalExceptionHandlerMiddleware trả về HTTP 409 Conflict (CONCURRENCY_CONFLICT) theo chuẩn RFC 9110."

---

## 1. User Scenarios & Testing *(mandatory)*

### User Story 1 - Reliable Item Insertion into Existing Active Cart (Priority: P1)

As an authenticated Buyer who already has an active shopping cart in the database, I want to add new products or SKUs to my cart seamlessly via `POST /api/v1/carts/me/items`, so that items are immediately persisted and reflected in my cart summary without experiencing unexpected 500 Internal Server Errors.

**Why this priority**:
Adding items to the cart is the critical top-of-funnel conversion action in CommerceService. When buyers who already have an active cart attempt to add a new item, EF Core ChangeTracker currently discovers the newly instantiated `CartItem` (which has a pre-assigned non-empty GUID) upon evaluating `.Local`, misinterprets it as an existing detached entity, and marks it with `EntityState.Modified`. At `SaveChangesAsync`, EF Core executes an SQL `UPDATE` against non-existent rows, throwing `DbUpdateConcurrencyException` and resulting in an HTTP 500 failure that completely blocks purchasing.

**Independent Test**:
1. Seed or retrieve an existing buyer with an active cart in PostgreSQL.
2. Submit a `POST /api/v1/carts/me/items` request with a valid `skuId` and `quantity >= 1` that is not yet present in the cart.
3. Verify that the response returns HTTP 200 OK with the updated `CartDto` containing the new item.
4. Verify directly in the database that a new record is inserted into the `CartItems` table.

**Acceptance Scenarios**:
1. **Given** an authenticated buyer with an existing active cart, **When** they add a new product SKU to their cart, **Then** the system detects that the SKU does not exist in `cart.Items`, calls `_dbContext.CartItems.Add(cartItem)` to explicitly enforce `EntityState.Added`, and persists the item via SQL `INSERT` without throwing concurrency exceptions.
2. **Given** an authenticated buyer with an existing active cart containing SKU A (quantity 2), **When** they add SKU A again (quantity 3), **Then** the system detects that the SKU already exists, updates the quantity to 5, recalculates totals, and persists the update via SQL `UPDATE` cleanly.
3. **Given** an authenticated buyer without an active cart, **When** they add their first item, **Then** the system initializes a new `Cart`, adds the item, and persists both entities in `EntityState.Added` with HTTP 200 OK.

---

### User Story 2 - Resilient Guest Cart Merge on Authentication (Priority: P1)

As a Buyer logging in after browsing the marketplace as a guest, I want the items stored in my temporary browser local storage to merge into my server-side shopping cart via `POST /api/v1/carts/me/merge`, so that my selected products are preserved without incurring 500 Internal Server Errors during account sign-in.

**Why this priority**:
When a guest buyer registers or signs in, the Frontend automatically calls the cart merge endpoint. If newly imported guest items are added to `cart.Items` without explicit `_dbContext.CartItems.Add(cartItem)` registration, EF Core replicates the same concurrency defect, failing the entire sign-in synchronization flow.

**Independent Test**:
1. Create a buyer account that already possesses an active cart with 1 item.
2. Send a `POST /api/v1/carts/me/merge` request containing a list of `GuestItems` with at least 1 new SKU and 1 overlapping SKU.
3. Verify that HTTP 200 OK is returned, overlapping SKUs accumulate quantities (capped at 99), and brand new SKUs are inserted with `EntityState.Added`.

**Acceptance Scenarios**:
1. **Given** a buyer with an active cart, **When** merging a guest cart containing an unfamiliar SKU, **Then** the system validates catalog purchasability and warehouse inventory, checks `cart.Items.Any(i => i.SkuId == sku.SkuId)`, creates the item, and explicitly invokes `_dbContext.CartItems.Add(cartItem)` before saving.
2. **Given** a guest item whose SKU is already in the buyer's server-side cart, **When** merging, **Then** the system increments the existing item's quantity without calling `CartItems.Add`, respecting the 99-item single-SKU cap.
3. **Given** a guest item with zero or negative quantity or an unpurchasable SKU, **When** merging, **Then** the system skips that item gracefully without interrupting the remaining valid items.

---

### User Story 3 - RFC 9110 Compliant Optimistic Concurrency Error Mapping (Priority: P1)

As a Frontend Application consuming CommerceService APIs, I want database concurrency conflicts (`DbUpdateConcurrencyException`) to be mapped to HTTP 409 Conflict with code `CONCURRENCY_CONFLICT`, so that the client UI can distinguish normal multi-tab/concurrent editing collisions from fatal 500 server crashes and offer automatic refresh options.

**Why this priority**:
Currently, any `DbUpdateConcurrencyException` uncaught in application services cascades to `GlobalExceptionHandlerMiddleware` and falls into the fallback pattern `_ => (500, "INTERNAL_SERVER_ERROR")`. This corrupts operational metrics, triggers false-positive system crash alerts, and prevents the Frontend from executing graceful client-side retry or refresh strategies.

**Independent Test**:
1. Simulate a concurrent update on the same cart entity using an outdated `RowVersion` or trigger a synthetic `DbUpdateConcurrencyException`.
2. Inspect the HTTP response status and problem details envelope.
3. Verify that the response returns HTTP 409 Conflict, `errorCode` is `"CONCURRENCY_CONFLICT"`, and `detail` provides a clear, actionable sentence-case message.

**Acceptance Scenarios**:
1. **Given** two concurrent requests attempting to mutate the same cart with optimistic locking tokens (`RowVersion`), **When** one request commits first and the second encounters a concurrency mismatch, **Then** the middleware catches `DbUpdateConcurrencyException` and responds with HTTP 409 Conflict and problem type `https://tools.ietf.org/html/rfc9110#section-15.5.10`.
2. **Given** an HTTP 409 response, **Then** the JSON body adheres strictly to:
   - `status`: 409
   - `errorCode`: `"CONCURRENCY_CONFLICT"`
   - `title`: `"Data concurrency conflict"`
   - `detail`: `"The resource was modified or deleted by another operation. Please refresh and try again."`

---

### User Story 4 - Double-Click & Rapid Ingestion Race Condition Hardening (Priority: P2)

As a Buyer rapidly clicking the "Add to Cart" button or initiating concurrent requests across multiple browser tabs for the same SKU, I want the system to handle the concurrent write gracefully, so that my cart reliably reflects the intended purchase instead of crashing with a unique constraint violation error.

**Why this priority**:
PostgreSQL enforces a unique composite index `IX_CartItems_CartId_SkuId` on `(CartId, SkuId)`. When two rapid requests arrive simultaneously for a newly added SKU, both can evaluate `isExistingItem == false` before either transaction commits. Without duplicate key handling, the second request throws a `DbUpdateException` (Postgres code 23505), resulting in a 500 error for the user.

**Independent Test**:
1. Fire two concurrent `POST /api/v1/carts/me/items` requests with identical `buyerId` and `skuId`.
2. Verify that both requests complete successfully or the racing request recovers cleanly by reloading the active cart and accumulating quantity, ensuring no 500 error is returned to the client.

**Acceptance Scenarios**:
1. **Given** two simultaneous add requests for the same SKU on an active cart, **When** a database unique constraint conflict occurs on `IX_CartItems_CartId_SkuId`, **Then** the service catches `DbUpdateException`, reloads the cart entity with latest tracked items, accumulates the quantity onto the newly inserted item, and saves successfully.

---

### User Story 5 - Cart RowVersion Integrity during Price Validation (Priority: P2)

As the Commerce System maintaining data consistency prior to checkout, I want the cart's optimistic concurrency token (`RowVersion`) to increment whenever snapshot prices are refreshed in `POST /api/v1/carts/me/validate`, so that subsequent checkout operations are validated against a synchronized entity version.

**Why this priority**:
`ValidateCartAsync` updates item price snapshots and cart subtotal amounts when catalog prices change. Omitting `RowVersion++` creates a discrepancy between domain entity state tracking and database concurrency token evolution.

**Independent Test**:
1. Seed a cart with an item whose catalog price has changed.
2. Call `POST /api/v1/carts/me/validate`.
3. Verify that `cart.RowVersion` is incremented in database alongside `SubtotalAmount`.

**Acceptance Scenarios**:
1. **Given** a cart requiring price snapshot updates during validation, **When** `ValidateCartAsync` persists price adjustments, **Then** it updates `cart.SubtotalAmount`, updates `cart.LastActivityAt`, increments `cart.RowVersion++`, and executes `SaveChangesAsync`.

---

## 2. Edge Cases & Boundary Conditions

- **EC-001 (Zero or Negative Quantity)**: Frontend sends `quantity <= 0` in add item request.  
  *Mitigation*: Guarded at the controller/service boundary; throws `CommerceDomainException("VAL_INVALID_INPUT", "Quantity must be greater than zero.", 400)`.
- **EC-002 (Cumulative Item Quantity Exceeding 99)**: Existing item has quantity 90, request adds 15.  
  *Mitigation*: Guarded in `Cart.AddOrUpdateItem`; throws `CommerceDomainException("CART_ITEM_LIMIT_EXCEEDED", "Total quantity for a single product cannot exceed 99.", 400)`.
- **EC-003 (Distinct Item Limit of 50 Exceeded)**: Cart currently contains 50 distinct items, buyer adds 51st SKU.  
  *Mitigation*: Guarded in `Cart.AddOrUpdateItem`; throws `CommerceDomainException("CART_LIMIT_EXCEEDED", "Cart cannot exceed maximum limit of 50 different items.", 400)`.
- **EC-004 (Concurrent Cart Creation Race)**: Buyer has no cart in DB, fires 2 concurrent add requests.  
  *Mitigation*: Guarded by existing `DbUpdateException` fallback query mechanism in `GetOrCreateActiveCartAsync` or equivalent retry logic in `AddItemAsync`.
- **EC-005 (Simultaneous Variant Switch and Removal)**: One request modifies variant while another removes the item.  
  *Mitigation*: Entity optimistic locking catches the conflict, mapped to HTTP 409 `CONCURRENCY_CONFLICT`.

---

## 3. Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST check whether a SKU exists in `cart.Items` prior to calling `cart.AddOrUpdateItem(...)` in `AddItemAsync`.
- **FR-002**: If the SKU does not exist in `cart.Items`, the system MUST explicitly register the returned `CartItem` via `_dbContext.CartItems.Add(cartItem)` to guarantee `EntityState.Added`.
- **FR-003**: The system MUST NOT invoke `_dbContext.CartItems.Local.Any(...)` prior to `_dbContext.CartItems.Add(...)`, avoiding premature invocation of `ChangeTracker.DetectChanges()`.
- **FR-004**: The system MUST apply the identical `isExistingItem` check and explicit `_dbContext.CartItems.Add(cartItem)` call within `MergeGuestCartAsync` for all newly imported guest cart items.
- **FR-005**: The system MUST handle `DbUpdateException` arising from unique index constraint violations (`IX_CartItems_CartId_SkuId`) in `AddItemAsync` by reloading the active cart and accumulating item quantity safely.
- **FR-006**: In `GlobalExceptionHandlerMiddleware`, the system MUST register an explicit pattern match for `Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException`.
- **FR-007**: When `DbUpdateConcurrencyException` is caught, the middleware MUST return HTTP 409 Conflict with `errorCode: "CONCURRENCY_CONFLICT"`, title `"Data concurrency conflict"`, and detail `"The resource was modified or deleted by another operation. Please refresh and try again."`.
- **FR-008**: In `ValidateCartAsync`, whenever `hasDbUpdates` is true, the system MUST increment `cart.RowVersion++` before saving changes to PostgreSQL.
- **FR-009**: All error messages across modified flows MUST strictly adhere to 100% English Sentence-case descriptions and `SCREAMING_SNAKE_CASE` error codes.
- **FR-010**: All modified services and controllers MUST pass all unit and integration tests and build cleanly with `dotnet build Commerce.sln -c Release` (0 warnings, 0 errors).

### Key Entities

- **Cart**: Aggregate root representing the buyer's active shopping cart. Key attributes: `Id`, `BuyerId`, `Status`, `ItemCount`, `SubtotalAmount`, `RowVersion`, `LastActivityAt`.
- **CartItem**: Line item belonging to a Cart. Key attributes: `Id`, `CartId`, `ProductId`, `SkuId`, `SellerId`, `Quantity`, `UnitPriceSnapshot`, `ProductNameSnapshot`.

---

## 4. Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of `POST /api/v1/carts/me/items` calls for existing carts insert items cleanly with HTTP 200 OK without triggering `DbUpdateConcurrencyException`.
- **SC-002**: 100% of `POST /api/v1/carts/me/merge` calls with guest items merge cleanly into existing buyer carts without 500 errors.
- **SC-003**: Zero occurrences of uncaught `DbUpdateConcurrencyException` cascading to HTTP 500; 100% of optimistic concurrency collisions return HTTP 409 `CONCURRENCY_CONFLICT`.
- **SC-004**: Rapid consecutive clicks on "Add to Cart" for the same SKU succeed without throwing unique constraint 500 errors.
- **SC-005**: Solution builds cleanly in Release configuration (`dotnet build Commerce.sln -c Release`) with 0 compilation errors.

---

## 5. Assumptions

- **A-001**: The database is PostgreSQL 16+ running on port 5435 (`commerce_db`).
- **A-002**: The `CartItem` entity uses client-side GUID generation (`Id = Guid.NewGuid()`) defined in `BaseEntity<Guid>`.
- **A-003**: In-flight HTTP requests have passed through API Gateway or local middleware verifying the caller's JWT and populating `x-user-id` in the `HttpContext`.
