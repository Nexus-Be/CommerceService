# Tasks: Cart State Tracking Remediation & Concurrency Standardization

**Input**: Design documents from `/specs/018-cart-concurrency-and-state-remediation/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`  

## Format: `- [ ] [TaskID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., [US1], [US2], [US3])
- Include exact file paths in descriptions

---

## Phase 1: Setup (Baseline Verification)

**Purpose**: Verify solution baseline and ensure clean compilation before executing modifications.

- [x] T001 Verify baseline solution compilation in Release configuration via `dotnet build Commerce.sln -c Release --no-restore -m:1 -nr:false`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Infrastructure helper methods and shared error inspection utilities required across multiple user stories.

**⚠️ CRITICAL**: Must be in place before implementing defensive error handling in user stories.

- [x] T002 Add helper method `IsUniqueConstraintViolation(DbUpdateException ex)` to inspect PostgreSQL error code 23505 (`IX_CartItems_CartId_SkuId`) in `Commerce.Application/Services/Cart/CartService.cs`

**Checkpoint**: Foundation ready - user story implementation can begin.

---

## Phase 3: User Story 1 - Reliable Item Insertion into Existing Active Cart (Priority: P1) 🎯 MVP

**Goal**: Eliminate the 500 `DbUpdateConcurrencyException` when adding new items to an existing cart by removing `_dbContext.CartItems.Local.Any(...)` and explicitly invoking `_dbContext.CartItems.Add(cartItem)` for new items.

**Independent Test**: Call `AddItemAsync` on a buyer cart that already contains existing items. Verify that adding a new distinct SKU assigns `EntityState.Added`, generates an SQL `INSERT INTO "CartItems"`, and returns HTTP 200 OK without concurrency exceptions.

### Tests for User Story 1
- [x] T003 [P] [US1] Add unit test `AddItemAsync_ShouldMarkEntityAsAdded_WhenAddingNewSkuToExistingCart` in `tests/Commerce.UnitTests/Application/CartServiceTests.cs` verifying that adding an unfamiliar SKU to an active tracked cart persists cleanly without concurrency exceptions.

### Implementation for User Story 1
- [x] T004 [US1] Refactor `AddItemAsync` in `Commerce.Application/Services/Cart/CartService.cs`: check `var isExistingItem = cart.Items.Any(i => i.SkuId == skuInfo.SkuId)` upfront, call `cart.AddOrUpdateItem(...)`, delete `_dbContext.CartItems.Local.Any(...)`, and explicitly invoke `_dbContext.CartItems.Add(cartItem)` when `!isExistingItem`.

**Checkpoint**: User Story 1 is fully functional and testable independently (MVP complete).

---

## Phase 4: User Story 2 - Resilient Guest Cart Merge on Authentication (Priority: P1)

**Goal**: Eliminate the sister 500 error when merging guest items into an existing cart during user sign-in by checking `isExistingItem` and explicitly calling `_dbContext.CartItems.Add(cartItem)` for newly imported guest items.

**Independent Test**: Call `MergeGuestCartAsync` for a buyer with an active cart using a request containing at least 1 new guest SKU. Verify that the new SKU is inserted with `EntityState.Added` and returns HTTP 200 OK.

### Tests for User Story 2
- [x] T005 [P] [US2] Add unit test `MergeGuestCartAsync_ShouldMarkNewItemsAsAdded_WhenMergingIntoExistingCart` in `tests/Commerce.UnitTests/Application/CartServiceTests.cs` asserting that guest items with new SKUs are persisted cleanly without concurrency exceptions.

### Implementation for User Story 2
- [x] T006 [US2] Refactor `MergeGuestCartAsync` in `Commerce.Application/Services/Cart/CartService.cs`: evaluate `var isExistingItem = cart.Items.Any(i => i.SkuId == sku.SkuId)` for each guest item, and call `_dbContext.CartItems.Add(cartItem)` when `!isExistingItem`.

**Checkpoint**: User Story 2 is fully functional and testable independently.

---

## Phase 5: User Story 3 - RFC 9110 Compliant Optimistic Concurrency Error Mapping (Priority: P1)

**Goal**: Standardize optimistic concurrency handling in `GlobalExceptionHandlerMiddleware` by mapping `Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException` to HTTP 409 Conflict (`CONCURRENCY_CONFLICT`) per RFC 9110 Section 15.5.10.

**Independent Test**: Pass a synthetic or simulated `DbUpdateConcurrencyException` through `GlobalExceptionHandlerMiddleware`. Verify that the response status is 409, `errorCode` is `"CONCURRENCY_CONFLICT"`, and problem details match RFC 9110.

### Tests for User Story 3
- [x] T007 [P] [US3] Add unit test `GlobalExceptionHandler_ShouldReturn409Conflict_WhenDbUpdateConcurrencyExceptionThrown` in `tests/Commerce.UnitTests/Application/CartServiceTests.cs` (or equivalent test class) asserting that `DbUpdateConcurrencyException` translates to HTTP 409 Conflict with code `"CONCURRENCY_CONFLICT"`.

### Implementation for User Story 3
- [x] T008 [US3] Update `GlobalExceptionHandlerMiddleware.cs` in `Commerce.Api/Middleware/GlobalExceptionHandlerMiddleware.cs`: add pattern-matching branch for `DbUpdateConcurrencyException` returning `StatusCodes.Status409Conflict`, `errorCode: "CONCURRENCY_CONFLICT"`, title `"Data concurrency conflict"`, detail `"The resource was modified or deleted by another operation. Please refresh and try again."`, and RFC type `"https://tools.ietf.org/html/rfc9110#section-15.5.10"`.

**Checkpoint**: User Story 3 is fully functional and testable independently.

---

## Phase 6: User Story 4 - Double-Click & Rapid Ingestion Race Condition Hardening (Priority: P2)

**Goal**: Prevent HTTP 500 errors when rapid consecutive clicks for the same SKU cause a unique constraint conflict on `IX_CartItems_CartId_SkuId` by reloading the cart and accumulating quantity.

**Independent Test**: Simulate a `DbUpdateException` with unique constraint violation `IX_CartItems_CartId_SkuId` during `AddItemAsync`. Verify that the exception is caught, the cart is refreshed, item quantity is accumulated, and the operation completes successfully.

### Tests for User Story 4
- [x] T009 [P] [US4] Add unit test `AddItemAsync_ShouldRecoverGracefully_WhenUniqueConstraintViolatedByConcurrentInsert` in `tests/Commerce.UnitTests/Application/CartServiceTests.cs` verifying automatic retry and quantity accumulation upon unique index collision.

### Implementation for User Story 4
- [x] T010 [US4] Add defensive try-catch in `AddItemAsync` in `Commerce.Application/Services/Cart/CartService.cs` wrapping `SaveChangesAsync`: catch `DbUpdateException` matching `IsUniqueConstraintViolation`, clear change tracker, reload the cart, accumulate quantity via `cart.AddOrUpdateItem(...)`, and persist successfully.

**Checkpoint**: User Story 4 is fully functional and testable independently.

---

## Phase 7: User Story 5 - Cart RowVersion Integrity during Price Validation (Priority: P2)

**Goal**: Ensure optimistic concurrency token consistency by incrementing `cart.RowVersion++` in `ValidateCartAsync` whenever catalog prices drift and cart subtotal amounts are adjusted.

**Independent Test**: Call `ValidateCartAsync` for a cart whose items experience catalog price drift. Verify that `cart.RowVersion` is incremented in the database alongside `SubtotalAmount`.

### Tests for User Story 5
- [x] T011 [P] [US5] Add unit test `ValidateCartAsync_ShouldIncrementRowVersion_WhenPriceSnapshotsAreUpdated` in `tests/Commerce.UnitTests/Application/CartServiceTests.cs` verifying that `cart.RowVersion` increments when `hasDbUpdates` is true.

### Implementation for User Story 5
- [x] T012 [US5] In `ValidateCartAsync` in `Commerce.Application/Services/Cart/CartService.cs`, add `cart.RowVersion++;` inside the `if (hasDbUpdates)` block prior to `await _dbContext.SaveChangesAsync(cancellationToken);`.

**Checkpoint**: User Story 5 is fully functional and testable independently.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Solution-wide build verification, test suite execution, and documentation synchronization.

- [x] T013 [P] Verify 100% English sentence-case formatting for all error messages and `SCREAMING_SNAKE_CASE` error codes across touched files per Rule 5 in `Commerce.Application/Services/Cart/CartService.cs` and `Commerce.Api/Middleware/GlobalExceptionHandlerMiddleware.cs`
- [x] T014 Execute full unit test suite via `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore -m:1 -nr:false`
- [x] T015 Perform end-to-end scenario validation against `specs/018-cart-concurrency-and-state-remediation/quickstart.md` and verify final Release build via `dotnet build Commerce.sln -c Release --no-restore -m:1 -nr:false`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Can start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1; blocks Phase 6 (US4).
- **User Story 1 (Phase 3)**: Depends on Phase 1; independent of other stories. 🎯 MVP.
- **User Story 2 (Phase 4)**: Depends on Phase 1; can execute in parallel with or after US1.
- **User Story 3 (Phase 5)**: Independent of cart domain logic (modifies middleware); can run in parallel with US1/US2.
- **User Story 4 (Phase 6)**: Depends on Phase 2 (T002) and Phase 3 (T004).
- **User Story 5 (Phase 7)**: Depends on Phase 1; independent of other stories.
- **Polish (Phase 8)**: Depends on completion of all desired user stories.

### Parallel Opportunities

- Tests within each user story (`T003`, `T005`, `T007`, `T009`, `T011`) are marked `[P]` and can be authored concurrently.
- User Story 3 (`GlobalExceptionHandlerMiddleware.cs`) touches a different project (`Commerce.Api`) and can be implemented in parallel with User Story 1 (`Commerce.Application`).
- Polish task `T013` can run concurrently with test verification.

---

## Implementation Strategy

### MVP First (User Story 1 Only)
1. Complete Phase 1 (Baseline build).
2. Complete Phase 3 (User Story 1: `AddItemAsync` state tracking fix).
3. Validate: Run `AddItemAsync` unit tests $\rightarrow$ Verify 500 error is eliminated.

### Incremental Delivery
1. Foundation & US1 $\rightarrow$ Cart addition works without 500 error (MVP).
2. US2 $\rightarrow$ Guest cart merge works without 500 error.
3. US3 $\rightarrow$ Concurrency conflicts cleanly return HTTP 409 instead of 500.
4. US4 $\rightarrow$ Double clicks absorbed without unique key violations.
5. US5 $\rightarrow$ Concurrency token parity preserved during price validation.
6. Phase 8 $\rightarrow$ Final test suite and Release build verification.
