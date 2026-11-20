# Research & Technical Decisions: Cart State Tracking Remediation & Concurrency Standardization

**Feature Branch**: `018-cart-concurrency-and-state-remediation`  
**Date**: 2026-11-10  
**Status**: Completed (Phase 0)  

---

## 1. Executive Summary

This document resolves all technical decisions and unknowns related to fixing the 500 Internal Server Error in `POST /api/v1/carts/me/items`, synchronizing `MergeGuestCartAsync`, mitigating rapid double-click race conditions, and standardizing optimistic concurrency exception handling in `GlobalExceptionHandlerMiddleware` to HTTP 409 Conflict per RFC 9110.

---

## 2. Research Findings & Technical Decisions

### Decision 1: EF Core ChangeTracker Navigation Entity State Mechanics

* **Problem Context**:
  When a Buyer has an existing active cart tracked by EF Core in `EntityState.Unchanged`, calling `cart.AddOrUpdateItem(...)` instantiates a new `CartItem` with `Id = Guid.NewGuid()` and adds it to `cart.Items`.
  The previous code evaluated:
  ```csharp
  if (!_dbContext.CartItems.Local.Any(ci => ci.Id == cartItem.Id))
  {
      _dbContext.CartItems.Add(cartItem);
  }
  ```
  In Entity Framework Core 8, accessing the `DbSet<T>.Local` property implicitly triggers `ChangeTracker.DetectChanges()`. During change detection, EF Core crawls navigation collections. When it discovers `cartItem` with a non-empty GUID (`Id != Guid.Empty`) that has not been explicitly registered via `DbSet.Add`, EF Core infers that this entity already exists in the database and was attached from a disconnected state. Consequently, EF Core assigns it `EntityState.Modified`.
  Because `cartItem` is now tracked in `.Local`, `_dbContext.CartItems.Local.Any(...)` evaluates to `true`, causing the negation `!Local.Any(...)` to evaluate to `false`. The call to `_dbContext.CartItems.Add(cartItem)` is completely bypassed.
  When `SaveChangesAsync` is invoked, EF Core emits an SQL `UPDATE "CartItems" SET ... WHERE "Id" = @id`. Since the record does not exist in PostgreSQL, 0 rows are affected, throwing `DbUpdateConcurrencyException` and triggering an HTTP 500 response.

* **Selected Approach**:
  Evaluate whether the SKU is already in the cart **before** invoking `cart.AddOrUpdateItem(...)`:
  ```csharp
  var isExistingItem = cart.Items.Any(i => i.SkuId == skuInfo.SkuId);
  var cartItem = cart.AddOrUpdateItem(
      productId: request.ProductId ?? skuInfo.ProductId,
      skuId: skuInfo.SkuId,
      sellerId: skuInfo.SellerId,
      productName: skuInfo.ProductName,
      quantity: request.Quantity,
      unitPrice: skuInfo.UnitPrice);

  if (!isExistingItem)
  {
      _dbContext.CartItems.Add(cartItem);
  }
  ```
* **Rationale**:
  1. Invoking `DbSet<CartItem>.Add(cartItem)` explicitly forces the entity state to `EntityState.Added` in the ChangeTracker, overriding any default heuristics.
  2. Bypasses touching `_dbContext.CartItems.Local`, eliminating the unintended premature execution of `DetectChanges()`.
  3. Guarantees that PostgreSQL executes `INSERT INTO "CartItems" (...) VALUES (...)`, persisting the new item with 100% reliability.

* **Alternatives Considered**:
  - *Setting `_dbContext.Entry(cartItem).State = EntityState.Added`*: Functionally equivalent, but `_dbContext.CartItems.Add(cartItem)` is more idiomatic in EF Core and avoids reflection overhead.
  - *Omitting `Id = Guid.NewGuid()` and letting database generate UUIDs*: Rejected because domain entities in this architecture use client-side GUID generation (`BaseEntity<Guid>`) to maintain snapshot purity and avoid identity retrieval round-trips.

---

### Decision 2: Synchronizing Guest Cart Merge (`MergeGuestCartAsync`)

* **Problem Context**:
  In `MergeGuestCartAsync` ([CartService.cs](file:///d:/CommerceService/Commerce.Application/Services/Cart/CartService.cs#L401-L408)), when a buyer logs in, items from guest storage are merged:
  ```csharp
  foreach (var guestItem in request.GuestItems)
  {
      // ...
      cart.AddOrUpdateItem(
          productId: guestItem.ProductId ?? sku.ProductId,
          skuId: sku.SkuId,
          sellerId: sku.SellerId,
          productName: sku.ProductName,
          quantity: guestItem.Quantity,
          unitPrice: sku.UnitPrice);
  }
  await _dbContext.SaveChangesAsync(cancellationToken);
  ```
  If any guest item represents a new SKU for the buyer's existing cart, `cart.AddOrUpdateItem` adds it to `cart.Items`. When `_dbContext.SaveChangesAsync` is called, `DetectChanges()` runs and encounters the untracked `CartItem` with a pre-set GUID, assigning `EntityState.Modified` and causing the exact same `DbUpdateConcurrencyException` (500) during checkout sign-in.

* **Selected Approach**:
  Apply the identical `isExistingItem` check inside the loop:
  ```csharp
  var isExistingItem = cart.Items.Any(i => i.SkuId == sku.SkuId);
  var cartItem = cart.AddOrUpdateItem(
      productId: guestItem.ProductId ?? sku.ProductId,
      skuId: sku.SkuId,
      sellerId: sku.SellerId,
      productName: sku.ProductName,
      quantity: guestItem.Quantity,
      unitPrice: sku.UnitPrice);

  if (!isExistingItem)
  {
      _dbContext.CartItems.Add(cartItem);
  }
  ```

* **Rationale**:
  Maintains complete architectural parity across all mutation entry points that insert line items into existing carts.

---

### Decision 3: Mitigating Double-Click Race Conditions (`IX_CartItems_CartId_SkuId`)

* **Problem Context**:
  PostgreSQL enforces a unique composite constraint `IX_CartItems_CartId_SkuId` on `(CartId, SkuId)`.
  If a buyer rapidly double-clicks "Add to Cart", two concurrent HTTP requests arrive at nearly the exact same millisecond:
  - Both read the cart and find `isExistingItem == false`.
  - Both call `cart.AddOrUpdateItem(...)` and `_dbContext.CartItems.Add(cartItem)`.
  - Thread 1 commits `INSERT INTO "CartItems"` $\rightarrow$ succeeds.
  - Thread 2 attempts `INSERT INTO "CartItems"` $\rightarrow$ PostgreSQL rejects with `23505: duplicate key value violates unique constraint "IX_CartItems_CartId_SkuId"`.
  - Without exception handling, Thread 2 throws `DbUpdateException` $\rightarrow$ 500 Internal Server Error returned to the Frontend.

* **Selected Approach**:
  Wrap `SaveChangesAsync` in `AddItemAsync` with defensive catch-and-retry logic:
  ```csharp
  try
  {
      await _dbContext.SaveChangesAsync(cancellationToken);
  }
  catch (DbUpdateException dbEx) when (IsUniqueConstraintViolation(dbEx))
  {
      // Detach failed entries to clear tainted tracker state
      _dbContext.ChangeTracker.Clear();

      // Reload fresh cart from database
      var refreshedCart = await GetActiveCartEntityAsync(buyerId, asNoTracking: false, cancellationToken)
          ?? throw new CommerceDomainException("CART_NOT_FOUND", "Active cart not found for the user.", 404);

      // Accumulate quantity onto the existing item
      refreshedCart.AddOrUpdateItem(
          productId: request.ProductId ?? skuInfo.ProductId,
          skuId: skuInfo.SkuId,
          sellerId: skuInfo.SellerId,
          productName: skuInfo.ProductName,
          quantity: request.Quantity,
          unitPrice: skuInfo.UnitPrice);

      await _dbContext.SaveChangesAsync(cancellationToken);
      return MapToDto(refreshedCart);
  }
  ```

* **Rationale**:
  Completely absorbs user double-clicks and concurrent tab additions. The racing request self-heals by turning the attempted duplicate insert into an update, delivering a flawless user experience without returning 500.

---

### Decision 4: RFC 9110 Concurrency Conflict Mapping in Global Middleware

* **Problem Context**:
  When an actual optimistic concurrency collision occurs (e.g., two tabs modifying the cart simultaneously with competing `RowVersion` values), EF Core legitimately throws `DbUpdateConcurrencyException`.
  In [GlobalExceptionHandlerMiddleware.cs](file:///d:/CommerceService/Commerce.Api/Middleware/GlobalExceptionHandlerMiddleware.cs#L36-L63), there is no pattern match for `DbUpdateConcurrencyException`. It falls into `_ => StatusCodes.Status500InternalServerError`, broadcasting false alarms in monitoring tools and confusing clients.

* **Selected Approach**:
  Add an explicit branch in `HandleExceptionAsync`:
  ```csharp
  DbUpdateConcurrencyException => (
      StatusCodes.Status409Conflict,
      "CONCURRENCY_CONFLICT",
      GetStatusTitle(StatusCodes.Status409Conflict, "Data concurrency conflict"),
      "The resource was modified or deleted by another operation. Please refresh and try again."),
  ```
  And map HTTP 409 in `GetRfcType`:
  ```csharp
  StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  ```

* **Rationale**:
  1. Conforms 100% to RFC 9110 Section 15.5.10 (HTTP 409 Conflict: Target resource state conflict).
  2. Complies with Constitution Rule IV: 100% English sentence-case message, `SCREAMING_SNAKE_CASE` code (`CONCURRENCY_CONFLICT`).
  3. Enables Frontend interceptors to recognize concurrent editing collisions and trigger auto-refresh flows.

---

### Decision 5: RowVersion Integrity in Pre-Checkout Validation

* **Problem Context**:
  In `ValidateCartAsync` ([CartService.cs](file:///d:/CommerceService/Commerce.Application/Services/Cart/CartService.cs#L361-L366)), when catalog prices change:
  ```csharp
  if (hasDbUpdates)
  {
      cart.SubtotalAmount = cart.Items.Sum(i => i.Quantity * i.UnitPriceSnapshot);
      cart.LastActivityAt = DateTimeOffset.UtcNow;
      await _dbContext.SaveChangesAsync(cancellationToken);
  }
  ```
  `RowVersion` is not incremented, whereas domain methods increment it via `RecalculateTotals()`.

* **Selected Approach**:
  Increment `cart.RowVersion++` inside `if (hasDbUpdates)` prior to `SaveChangesAsync`.

* **Rationale**:
  Guarantees that any price mutation increments the aggregate root's concurrency token, preserving optimistic locking invariants across the system.
