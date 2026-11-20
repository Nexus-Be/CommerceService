# Research & Technical Decisions: Order Settlement, Automated Refund, and Seed Data Alignment

**Feature Directory**: `specs/014-order-settlement-and-refund-alignment`  
**Phase**: Phase 0 (Research & Technical Decisions)  
**Status**: Completed  

---

## 1. Research Tasks & Unknowns Resolved

### Research 1: EF Core Entity Tracking for Newly Added CartItem
* **Context**: In `CartService.AddItemAsync`, `cart.AddOrUpdateItem(...)` instantiates a new `CartItem` and adds it to `cart.Items`. In some EF Core configurations, when `cart` is loaded with `.Include(c => c.Items)`, a new entity added to the collection is not reliably marked with `EntityState.Added` prior to `SaveChangesAsync`, causing the new item to be ignored during SQL generation.
* **Decision**: 
  In `CartService.AddItemAsync`, capture the result of `cart.AddOrUpdateItem(...)`. Check if the item's `Id` is already tracked in `_dbContext.CartItems.Local`. If not tracked (i.e. it is a newly created item), explicitly invoke `_dbContext.CartItems.Add(newItem)`.
* **Rationale**: 
  Explicitly calling `_dbContext.CartItems.Add(item)` sets the entity's state unconditionally to `EntityState.Added`, guaranteeing that EF Core generates an `INSERT INTO "CartItems"` statement upon `SaveChangesAsync`.
* **Alternatives Considered**: 
  - *Rely on `ChangeTracker.DetectChanges()`*: Unreliable when navigation collection modifications occur on detached or partially attached graphs.
  - *Use raw SQL insert*: Violates Clean Architecture and EF Core direct mapping principles.

---

### Research 2: SubOrder JSON Seed Conflict & Model Snapshot Synchronization
* **Context**: `SubOrderConfiguration.cs` configures `ShippingAddress` as an owned entity using PostgreSQL JSONB: `builder.OwnsOne(x => x.ShippingAddress, nav => nav.ToJson());`. In `CommerceSeedData.cs`, `SeedOrders` uses `modelBuilder.Entity<SubOrder>().HasData(...)` with concrete `ShippingAddressSnapshot` objects. EF Core 8/9 throws an `InvalidOperationException` because `HasData` cannot seed complex JSON-mapped owned entities.
* **Decision**:
  1. Remove `SeedOrders(modelBuilder)` call from `CommerceSeedData.SeedAll(modelBuilder)`.
  2. Create a clean migration `20261104150739_FixSubOrderJsonSeed.cs` that drops the hardcoded seed records from the model builder metadata without dropping production runtime tables.
  3. Synchronize `AppDbContextModelSnapshot.cs` to reflect the updated entity metadata without order seed entries.
* **Rationale**:
  Production data does not rely on model seed data for transactional orders. Orders and SubOrders are runtime transactional aggregates, not static dictionary/lookup seed data (like SystemSettings or DiscountCodes).
* **Alternatives Considered**:
  - *Keep seed data and map ShippingAddress as relational columns*: Violates architecture schema `DB_Commerce_v2` which mandates JSONB for shipping snapshots.
  - *Insert seed data via custom SQL in Migration*: Unnecessary complexity for transactional entities that are generated dynamically during tests.

---

### Research 3: Outbound Wallet Client Integration for Refund & Escrow Settlement
* **Context**: WalletService introduced 2 new MediatR commands in `Wallet.Api/Controllers/Public/WalletsController.cs`:
  - `RefundOrderPaymentCommand` (RefundCredit transaction, idempotent on order/suborder ID).
  - `SettleSellerOrderCommand` (EscrowRelease transaction, idempotent on suborder ID).
  Meanwhile, `Commerce.Infrastructure/Services/Wallet/WalletClient.cs` currently has stubbed implementations returning `Task.FromResult(false)`.
* **Decision**:
  Implement concrete HTTP calls in `WalletClient.cs`:
  1. **Refund**: `POST api/v1/wallets/orders/{orderId}/refund` with body `{ "amount": amount, "reason": reason }` and header `Idempotency-Key: refund-{subOrderId}`.
  2. **Settlement**: `POST api/v1/wallets/sub-orders/{subOrderId}/settle` with body `{ "sellerId": sellerId, "amount": amount }` and header `Idempotency-Key: settle-{subOrderId}`.
  3. Ensure `IdentityPropagationHandler` propagates the caller's JWT Bearer token on all calls.
* **Rationale**:
  Guarantees deterministic, idempotent financial operations. Even in case of retry or network latency, the WalletService idempotency store prevents double refund or duplicate seller payout.
* **Alternatives Considered**:
  - *RabbitMQ / Outbox events*: Strictly forbidden by Constitution Principle II (No Message Brokers / No Sagas). Synchronous Typed REST is mandatory.

---

### Research 4: Buyer Goods Receipt Confirmation & State Invariants
* **Context**: Multi-shop master order (`Order`) coordinates multiple shop sub-orders (`SubOrder`).
  - `Order` only tracks payment lifecycle (`PendingPayment`, `Paid`, `Cancelled`). It does NOT have a `Completed` status in its domain enum `OrderStatus`.
  - `SubOrder` tracks fulfillment and payout lifecycle (`PendingPayment`, `Paid`, `Confirmed`, `Shipped`, `Delivered`, `Completed`, `Cancelled`).
* **Decision**:
  1. Expose `POST /api/v1/sub-orders/{subOrderId:guid}/confirm-receipt` in `OrdersController.cs`.
  2. Require authentication: `currentUserId` must match `subOrder.BuyerId`.
  3. Enforce valid previous state: `subOrder.Status == SubOrderStatus.Delivered` (or `Shipped` if self-pickup/manual confirmation allowed).
  4. Transition `subOrder.Status = SubOrderStatus.Completed` and set `subOrder.CompletedAt = DateTimeOffset.UtcNow`.
  5. Trigger synchronous settlement via `_walletClient.SettleSellerSubOrderAsync`.
* **Rationale**:
  Maintains clean domain boundary. Each shop's sub-order completes independently upon buyer receipt, enabling partial payouts for multi-vendor carts without blocking other shops.

---

### Research 5: Seller Sub-Orders Query & Database Pagination
* **Context**: Sellers need to view orders placed at their shop. In-memory filtering or loading all orders is an anti-pattern.
* **Decision**:
  1. Expose `GET /api/v1/sub-orders` (with optional `status`, `page`, `pageSize`) in `OrdersController.cs`.
  2. In `OrderService.ListSellerSubOrdersAsync`:
     - Apply query predicate `s => s.SellerId == sellerId && !s.IsDeleted`.
     - Apply status filter if provided: `s.Status == status.Value`.
     - Order by `s.CreatedAt DESC`.
     - Count via `await query.CountAsync(ct)`.
     - Paginate via `await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)`.
     - Wrap in `PagedData<SubOrderSummaryResponse>`.
* **Rationale**:
  Complies with Constitution Principle III & local rules: 100% database-level pagination, preventing high-memory overhead.

---

## 2. Technology Stack & Verification Matrix

| Component | Standard / Library | Version | Role |
| :--- | :--- | :--- | :--- |
| **Runtime** | .NET Core | `8.0` | Target framework across all 4 projects |
| **ORM** | Entity Framework Core | `8.0` (Npgsql.EntityFrameworkCore.PostgreSQL) | Direct DbContext access (No Repository) |
| **Database** | PostgreSQL | `5435` | Target database `commerce_db` |
| **Web API** | ASP.NET Core Web API | `8.0` | RESTful thin controllers (Port `8003`) |
| **Inter-Service** | Typed `HttpClient` | Injected | Direct REST calls to Wallet, Fulfillment, Catalog |
| **Testing** | xUnit + FluentAssertions + Moq | Latest | Unit & E2E integration test suite |
