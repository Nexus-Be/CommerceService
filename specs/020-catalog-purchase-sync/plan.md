# Implementation Plan: Purchase Record Synchronization

**Branch**: `020-catalog-purchase-sync` | **Date**: 2026-11-11 | **Spec**: [specs/020-catalog-purchase-sync/spec.md](spec.md)

**Input**: Feature specification from `specs/020-catalog-purchase-sync/spec.md`

---

## 1. Summary

Implement outbound purchase record synchronization from `CommerceService` to `CatalogService` (`POST /api/v1/internal/purchases`) based on `COMMERCE_PURCHASE_SYNC_GUIDE.md`:
1. Define outbound contract `CatalogPurchaseSyncRequest` and client abstraction in `ICatalogClient.cs`.
2. Implement resilient, non-blocking `SyncPurchaseRecordAsync` and batch helper `SyncPurchasesBatchAsync` (`Task.WhenAll`) in `CatalogClient.cs`.
3. Fix EF Core navigation queries to eagerly include `Items` (`.Include(s => s.Items)` in `BuyerConfirmSubOrderReceiptAsync`, and `.Include(o => o.SubOrders).ThenInclude(so => so.Items)` in `CompleteOrderAsync`).
4. Trigger distinct `ProductId` synchronization upon `SubOrder` delivery receipt confirmation (`POST /api/v1/sub-orders/{subOrderId}/confirm-receipt`) and master order completion (`POST /api/v1/orders/{orderId}/complete`).
5. Trigger `ProductId` synchronization upon won auction order delivery receipt confirmation (`POST /api/v1/auction-orders/{id}/confirm-receipt`) in `AuctionOrderService.cs`.
6. Apply non-blocking fail-safe semantics (catch all network/HTTP failures and log warnings without rolling back order completion or escrow settlement).
7. Implement comprehensive unit tests across `CatalogClientTests`, `OrderLifecycleTests`, and `AuctionOrderServiceTests`.

---

## 2. Technical Context

- **Language/Version**: C# 12 / .NET 8.0 LTS
- **Primary Dependencies**: Microsoft.EntityFrameworkCore 8.0.x, Npgsql.EntityFrameworkCore.PostgreSQL 8.0.x, FluentAssertions, Moq, xUnit
- **Storage**: PostgreSQL 16 (database `commerce_db`, port `5435`) — Zero schema alterations required
- **Testing**: xUnit, Moq, FluentAssertions (`tests/Commerce.UnitTests/`)
- **Target Platform**: Linux Docker Container / Windows local development (ASPNETCORE_URLS `http://+:8003`)
- **Downstream Target**: `CatalogService` (Port `8002`, `http://catalog-service:8002/` / `http://localhost:8002/`)
- **Project Type**: Web API RESTful microservice
- **Performance Goals**: Sub-300ms concurrent sync overhead (`Task.WhenAll`), zero latency impact on failed downstream calls
- **Constraints**: 100% English Sentence-case error messages, SCREAMING_SNAKE_CASE error codes, zero MassTransit/RabbitMQ, non-blocking auxiliary side-effects, strict Inward-Only reference boundary

---

## 3. Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Rule | Compliance Analysis | Status |
|---|---|---|
| **I. Clean Architecture & Direct EF Core Access** | Respects 4-layer structure inward. Uses `ICommerceDbContext` / `AppDbContext` directly with `.Include(s => s.Items)`. Zero repository abstractions. Feature Co-location preserved. | **PASS** |
| **II. Synchronous Typed REST Orchestration** | Integrates via existing `ICatalogClient` and `CatalogClient` typed HTTP client. Zero message brokers or Saga state machines. Resilient non-blocking side-effect pattern. | **PASS** |
| **III. Immutable Infrastructure & REST Standards** | Operates on port `8003` (DB 5435). Catalog Service targeted at `8002`. Standard RESTful internal routes used (`api/v1/internal/purchases`). | **PASS** |
| **IV. Standardized System-Wide Error Protocol** | 100% English Sentence-case messages and logs. Error codes formatted in `SCREAMING_SNAKE_CASE` (e.g. `CATALOG_SERVICE_UNAVAILABLE`). | **PASS** |
| **V. Self-Contained Repository & Inward-Only** | All paths, references, and mock tests strictly relative to `Commerce.sln`. Zero external sibling directory references. | **PASS** |

---

## 4. Project Structure

### Documentation (this feature)

```text
specs/020-catalog-purchase-sync/
├── spec.md              # Feature specification & requirements
├── plan.md              # Implementation plan (this file)
├── research.md          # Technical research & architecture decisions
├── data-model.md        # Data model, contracts, and lifecycle sequence
├── quickstart.md        # Verification guide & cURL testing commands
├── contracts/           # OpenAPI API contract
│   └── catalog-purchase-sync-api.yaml
└── checklists/
    └── requirements.md  # Spec quality checklist (100% passed)
```

### Source Code Touches & Additions

```text
Commerce.Application/
├── Interfaces/
│   └── ICatalogClient.cs                                       # [MODIFY] Add CatalogPurchaseSyncRequest & SyncPurchaseRecordAsync methods
└── Services/
    ├── Order/
    │   └── OrderService.cs                                     # [MODIFY] Eagerly include Items & trigger purchase sync on receipt/complete
    └── AuctionOrder/
        └── AuctionOrderService.cs                              # [MODIFY] Trigger purchase sync for won auction product on receipt

Commerce.Infrastructure/
└── Services/
    └── Catalog/
        └── CatalogClient.cs                                    # [MODIFY] Implement SyncPurchaseRecordAsync targeting POST api/v1/internal/purchases

tests/Commerce.UnitTests/
├── Infrastructure/
│   └── CatalogClientTests.cs                                   # [MODIFY] Add tests for 201 Created, 200 OK, 400, 404, 500, network timeout
└── Application/
    ├── OrderLifecycleTests.cs                                  # [MODIFY] Verify SubOrder & Order complete triggers purchase sync
    └── AuctionOrderServiceTests.cs                             # [MODIFY] Verify AuctionOrder delivery receipt triggers purchase sync
```

---

## 5. Implementation Phases & Milestones

### Phase 0: Technical Research & Resolution
- [x] Resolve endpoint path, port, and headers (`POST api/v1/internal/purchases`, port 8002).
- [x] Establish in-memory deduplication strategy (`Select(i => i.ProductId).Distinct()`).
- [x] Confirm non-blocking fail-safe error handling policy.
- [x] Output: `research.md`.

### Phase 1: Contract, Data Model & Verification Design
- [x] Define `CatalogPurchaseSyncRequest` contract in `data-model.md`.
- [x] Document OpenAPI schema in `contracts/catalog-purchase-sync-api.yaml`.
- [x] Establish test matrix and verification plan in `quickstart.md`.
- [x] Output: `data-model.md`, `contracts/catalog-purchase-sync-api.yaml`, `quickstart.md`.

### Phase 2: Implementation (Scheduled via `/speckit-tasks`)
- Task 1: Update `ICatalogClient.cs` in `Commerce.Application/Interfaces/`.
- Task 2: Implement `SyncPurchaseRecordAsync` and `SyncPurchasesBatchAsync` in `CatalogClient.cs` (`Commerce.Infrastructure`).
- Task 3: Integrate purchase sync into `BuyerConfirmSubOrderReceiptAsync` and `CompleteOrderAsync` with `.Include(s => s.Items)` in `OrderService.cs`.
- Task 4: Integrate purchase sync into `BuyerConfirmAuctionDeliveryAsync` in `AuctionOrderService.cs`.
- Task 5: Add unit tests in `CatalogClientTests.cs`, `OrderLifecycleTests.cs`, and `AuctionOrderServiceTests.cs`.
- Task 6: Full verification build (`dotnet build Commerce.sln -c Release`) and test execution.
