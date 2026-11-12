<!--
Sync Impact Report
- Version change: 1.3.0 -> 1.4.0
- List of modified principles:
  - II. Synchronous Typed HTTP REST Orchestration & Transactional Compensation: Formally repealed MassTransit/RabbitMQ Sagas and Outbox tables. Ratified synchronous Typed HTTP REST Orchestration (IWalletClient, ICatalogClient, IFulfillmentClient), fail-closed error handling (no mock fallbacks), and transactional compensation.
  - III. Immutable Infrastructure & RESTful API Architecture: Removed RabbitMQ from messaging infrastructure. Reaffirmed container port 8003 and PostgreSQL port 5435.
- Added sections: None
- Removed sections: CloudEvents Specification Standard, MassTransit Transactional Outbox
- Follow-up TODOs: None
-->

# CommerceService Constitution

## Core Principles

### I. Clean Architecture & Direct EF Core Access (No Repository Anti-Pattern)
The codebase MUST strictly adhere to the 4-layer Clean Architecture (`Commerce.Domain`, `Commerce.Application`, `Commerce.Infrastructure`, `Commerce.Api` targeting `.NET 8.0`):
- **Dependency Inversion**: Outer layers depend inward (`Api` → `Application` + `Infrastructure`; `Infrastructure` → `Application` + `Domain`; `Application` → `Domain`; `Domain` has zero external dependencies).
- **Direct EF Core Access**: Developers and AI agents MUST NOT wrap Entity Framework Core inside Repository or Unit-of-Work abstractions.
- **DbContext Mapping**: The Application layer interacts directly with `ICommerceDbContext` and its `DbSet<T>` properties. The Infrastructure layer implements persistence via `AppDbContext`.
- **Entity & DB Mapping Alignment**:
  - `Order` represents the Checkout Group (1 checkout = N orders, deadline, payment attempts).
  - `SubOrder` represents the Shop/Seller Order (an order per seller, snapshots, amounts, status).
  - `SubOrderItem` represents the Order Line Item (line items under SubOrder).
  - `Cart` & `CartItem` represent active buyer carts.

### II. Synchronous Typed HTTP REST Orchestration & Transactional Compensation
CommerceService coordinates order placement, inventory reservations, payments, and shipments via direct, synchronous Typed HTTP REST Clients:
- **Bounded Context Boundaries**:
  - CommerceService DOES NOT own inventory stock; stock reservations, deductions, and releases are coordinated via `IFulfillmentClient` (`api/v1/internal/inventories/reservations`).
  - CommerceService DOES NOT own user wallet funds; balance deductions and payments are coordinated via `IWalletClient` (`api/v1/internal/wallets`).
  - CommerceService DOES NOT own product catalog items; catalog validations and price verifications are coordinated via `ICatalogClient` (`api/v1/products/batch`).
  - CommerceService receives auction outcomes via REST endpoints to manage `AuctionOrder`.
- **Typed HTTP REST Clients & Fail-Closed Governance**:
  - All inter-service communications MUST use typed HTTP clients (`IWalletClient`, `ICatalogClient`, `IFulfillmentClient`) injected into feature services.
  - gRPC, RabbitMQ, MassTransit, Saga State Machines, and Transactional Outbox tables are STRICTLY FORBIDDEN and REPEALED.
  - Clients MUST operate fail-closed: never fabricate dummy shipping fees (e.g. 30,000 VND), fake carrier tracking codes (e.g. `GHN-...`), or swallow critical errors (`_failOpen = false`). Outbound service failures MUST surface as standard RFC 9110 HTTP 503 `CommerceDomainException`.
- **Transactional Compensation**:
  - When multi-step operations fail during checkout or payment (e.g., payment failure after stock reservation), CommerceService MUST execute explicit compensating calls (e.g. releasing reserved inventory via `ReleaseStockAsync` or `BatchReleaseStockAsync`).

### III. Immutable Infrastructure & RESTful API Architecture
All deployment, network, and communication configurations are immutable project standards:
- **Runtime Ports**:
  - **Docker/Container Port**: Port `8003` (`EXPOSE 8003`, `ASPNETCORE_URLS=http://+:8003`).
  - **Local Development**: Port `5105` (HTTP) and `7035` (HTTPS) defined in `launchSettings.json`.
- **Data Persistence**:
  - **PostgreSQL**: Port `5435`, database name `commerce_db`, user `postgres`.
- **RESTful API Standards**:
  - Endpoints follow `docs/api_catalog.md` (e.g., `GET /api/v1/carts/me`, `POST /api/v1/carts/me/items`, `PATCH /api/v1/carts/me/items/{cartItemId}`, `POST /api/v1/orders`, `POST /api/v1/sub-orders/{subOrderId}/delivery-status`).
  - APIs MUST maintain 100% OpenAPI / Swagger specification parity with the API Gateway.
  - **Idempotency Protection**: State-mutating requests (such as Checkout and Order Creation) MUST require an `Idempotency-Key` (UUID) header with 24-hour deduplication stored in the idempotency store.
  - **Response Payload**: Error responses MUST return `application/problem+json` (RFC 9110) formatted with: `type`, `title`, `status`, `errorCode`, `detail`, `traceId`.

### IV. Standardized System-Wide Error Protocol
All error messages, exceptions, problem details, and API validation responses MUST adhere strictly to the universal 3-part standard:
- **100% English**: All human-facing messages MUST be written in clear English (never Vietnamese).
- **Sentence Case**: Descriptions MUST capitalize only the first letter (e.g., `"Cart item not found in the user cart."`, `"Order cannot be cancelled in its current status."`).
- **SCREAMING_SNAKE_CASE Codes**: Technical error codes MUST adhere to `docs/gaps_decisions.md` and the standardized error codes (e.g., `VALIDATION_ERROR`, `VAL_IDEMPOTENCY_KEY_REQUIRED`, `CART_NOT_FOUND`, `CHECKOUT_ALREADY_OPEN`, `ORDER_INVALID_STATUS`, `FULFILLMENT_SERVICE_UNAVAILABLE`).

### V. Self-Contained Repository & Inward-Only Boundaries
CommerceService is an independent, self-contained Git repository (`Commerce.sln`):
- All path references, imports, and specifications MUST be inward-only.
- Path references pointing outside the repository root (`../../../docs/...`) or referencing sibling microservices directly (`../../Catalog/...`) are STRICTLY FORBIDDEN.

## Architecture & Persistence Constraints

1. **Solution Structure**: The solution file is `Commerce.sln` comprising 4 projects targeting `.NET 8.0`:
   - `Commerce.Domain`, `Commerce.Application`, `Commerce.Infrastructure`, `Commerce.Api`.

2. **Core Database Entities & Schema Invariants (`DB_Commerce_v2`)**:
   - `orders.total_amount = seller_receive_amount + platform_fee_amount + shipping_fee_amount` (platform_fee_amount can be negative due to system voucher subsidies).
   - For Auction Orders: `shipping_fee_amount` and `total_amount` are either both NULL (awaiting shipping quote) or `total_amount = winning_amount + shipping_fee_amount`.
   - Cross-service references (`buyer_id`, `seller_id`, `product_id`, `sku_id`, `wallet_transaction_id`) are stored as raw `Guid` values without foreign keys.

3. **Canonical Enum Naming Policy**:
   All entity configurations store enum values as strings via `HasConversion<string>()` or PostgreSQL integer enums as configured. Code MUST use canonical enum names:
   - `SubOrderStatus`: Use `Confirmed` (do not use `SellerAccepted`); use `Shipped` (do not use `Shipping`).
   - `DiscountType`: Use `Percentage` and `FixedAmount`.
   - `DiscountScopeType`: Use `System` (do not use `Global`).

4. **Concurrency & Soft-Delete**:
   - Optimistic concurrency is enforced on `Cart`, `Order`, `SubOrder`, `OrderRefund`, and `AuctionOrder` using `RowVersion` (`long`) with `.IsConcurrencyToken()`.
   - Entities inherit soft-delete capabilities (`IsDeleted`, `DeletedAt`) with global query filters (`HasQueryFilter(x => !x.IsDeleted)`).

## Quality Gates & Definition of Done

No technical task, code modification, or pull request is complete unless:
1. **Plan-First Compliance**: A comprehensive plan outlining blast radius, file touches, and verification steps has been documented prior to code changes.
2. **Clean Build**: `dotnet build Commerce.sln -c Release` compiles with 0 errors.
3. **Automated Verification**: `dotnet test Commerce.sln -c Release` passes 100% of all unit and integration test suites.
4. **Git Hygiene**: Clean working tree. No build artifacts (`bin/`, `obj/`), editor settings (`.vs/`, `.idea/`), or untracked environment secrets (`.env`) are committed.

## Governance

- The Constitution is the supreme technical authority for CommerceService; it supersedes informal conventions and ad-hoc patterns.
- Any amendment MUST be versioned using Semantic Versioning (SemVer), documented with clear rationale, and accompanied by necessary schema/code migrations.
- Runtime development rules defined in `AGENTS.md`, `specs/`, and `docs/` serve as active compliance references during implementation.

**Version**: 1.4.0 | **Ratified**: 2026-10-24 | **Last Amended**: 2026-11-04
