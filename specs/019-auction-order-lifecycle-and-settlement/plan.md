# Implementation Plan: Auction Order Lifecycle, Settlement & Automated Reversal

**Branch**: `019-auction-order-lifecycle-and-settlement` | **Date**: 2026-11-11 | **Spec**: [specs/019-auction-order-lifecycle-and-settlement/spec.md](spec.md)

**Input**: Feature specification from `specs/019-auction-order-lifecycle-and-settlement/spec.md`

---

## 1. Summary

Implement the complete, end-to-end lifecycle for Auction Orders (`AuctionOrder`) in `CommerceService` based on alignment contract v1.1.0 with Auction Service:
1. Ingest winning auction settlements via `POST /api/v1/auction-orders/settlement` with composite pair `(AuctionId, WinnerId)` idempotency, supporting runner-up bidders when the original winner defaults.
2. Enable winner delivery address submission and carrier fee calculation via `IFulfillmentClient` (`PUT /api/v1/auction-orders/{id}/shipping`).
3. Process wallet payments (`POST /api/v1/auction-orders/{id}/pay`), activate seller 24h respond deadline, and immediately dispatch callback `status: "Paid"` with `paidAt`.
4. Allow seller to accept within 24h (`POST /api/v1/auction-orders/{id}/accept`), transition to `Confirmed`, trigger shipment, and dispatch callback `status: "SellerAccepted"`.
5. Allow seller to reject (`POST /api/v1/auction-orders/{id}/reject`), transition to `Cancelled`, execute 100% automated wallet refund to the buyer, and dispatch callback `status: "SellerRejected"`.
6. Fix latent Foreign Key defect in `PaymentTimeoutWorker` (`SubOrderId` $\rightarrow$ `AuctionOrderId`), scan and auto-cancel 24h unpaid orders with callback `status: "PaymentTimedOut"`.
7. Add scanning loop in `PaymentTimeoutWorker` to auto-cancel orders where the seller failed to accept within 24h, refunding 100% wallet balance and dispatching callback `status: "SellerExpired"`.
8. Support carrier delivery webhook synchronization and buyer delivery confirmation with seller escrow release.
9. Implement outbound typed REST client `IAuctionClient` / `AuctionClient` configured with `AUCTION_SERVICE_URL:8005` and `X-Internal-Service-Key`.

---

## 2. Technical Context

- **Language/Version**: C# 12 / .NET 8.0 LTS
- **Primary Dependencies**: Microsoft.EntityFrameworkCore 8.0.x, Npgsql.EntityFrameworkCore.PostgreSQL 8.0.x, FluentAssertions, Moq, xUnit
- **Storage**: PostgreSQL 16 (database `commerce_db`, port `5435`)
- **Testing**: xUnit, Moq, FluentAssertions (`tests/Commerce.UnitTests/`)
- **Target Platform**: Linux Docker Container / Windows local development (ASPNETCORE_URLS `http://+:8003`)
- **Project Type**: Web API RESTful microservice
- **Performance Goals**: Sub-second settlement ingestion (<500ms p95), non-blocking background timeout worker (60s tick interval)
- **Constraints**: 100% English Sentence-case error messages, SCREAMING_SNAKE_CASE error codes, zero MassTransit/RabbitMQ, fail-closed external client integrations, strict Inward-Only reference boundary

---

## 3. Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Rule | Compliance Analysis | Status |
|---|---|---|
| **I. Clean Architecture & Direct EF Core Access** | Uses 4-layer structure inward. Uses `ICommerceDbContext` / `AppDbContext.AuctionOrders` directly. Zero repository abstractions. Feature Co-location applied in `Commerce.Application/Services/AuctionOrder/`. | **PASS** |
| **II. Synchronous Typed REST Orchestration** | Integrates `IAuctionClient` with typed `HttpClient`. Reuses `IWalletClient` and `IFulfillmentClient`. Zero message brokers or Saga state machines. Financial fail-safe applied. | **PASS** |
| **III. Immutable Infrastructure & REST Standards** | Operates on port `8003` (DB 5435). Auction Service targeted at `8005`. REST plural routes used. ProblemDetails returned on errors. | **PASS** |
| **IV. Standardized System-Wide Error Protocol** | 100% English Sentence-case messages (`"Auction payment deadline has expired."`). Error codes formatted in `SCREAMING_SNAKE_CASE` (`PAYMENT_DEADLINE_EXPIRED`, `SELLER_RESPOND_TIMEOUT`). | **PASS** |
| **V. Self-Contained Repository & Inward-Only** | All paths and references strictly relative to `Commerce.sln`. No external cross-service folder references. | **PASS** |

---

## 4. Project Structure

### Documentation (this feature)

```text
specs/019-auction-order-lifecycle-and-settlement/
├── spec.md              # Feature specification & requirements
├── plan.md              # Implementation plan (this file)
├── research.md          # Phase 0 technical decisions & research findings
├── data-model.md        # Phase 1 data model & state transition rules
├── quickstart.md        # Phase 1 end-to-end verification guide
├── contracts/           # Phase 1 OpenAPI API contracts
│   └── auction-order-api.yaml
└── checklists/
    └── requirements.md  # Spec quality checklist (100% passed)
```

### Source Code Touches & Additions

```text
Commerce.Application/
├── Interfaces/
│   └── IAuctionClient.cs                                       # [NEW] Outbound contract to Auction Service
└── Services/
    ├── AuctionOrder/                                           # [NEW] Feature Co-location package
    │   ├── IAuctionOrderService.cs                             # [NEW] Feature service interface
    │   ├── AuctionOrderService.cs                              # [NEW] Core business logic & state transitions
    │   ├── AuctionOrderRequests.cs                             # [NEW] Settlement, shipping, pay, respond DTOs
    │   └── AuctionOrderResponses.cs                            # [NEW] Order detail, summary, response DTOs
    └── Order/
        └── OrderService.cs                                     # [MODIFY] Webhook delivery status fallback to AuctionOrders

Commerce.Infrastructure/
├── DependencyInjection.cs                                      # [MODIFY] Register IAuctionClient typed HttpClient
└── Services/
    └── Auction/
        └── AuctionClient.cs                                    # [NEW] Typed REST client implementation

Commerce.Api/
├── Controllers/
│   └── Public/
│       └── AuctionOrdersController.cs                          # [NEW] 1-to-1 Thin controller for Auction Orders
├── Workers/
│   └── PaymentTimeoutWorker.cs                                 # [MODIFY] Fix SubOrderId FK bug & add 24h seller respond loop
└── appsettings.json                                            # [MODIFY] Add AUCTION_SERVICE_URL:8005

docker-compose.yml                                              # [MODIFY] Add AUCTION_SERVICE_URL environment variable

tests/Commerce.UnitTests/
└── Application/
    ├── AuctionOrderServiceTests.cs                             # [NEW] Comprehensive unit test suite
    └── PaymentTimeoutWorkerTests.cs                            # [MODIFY] Test FK fix and seller response timeout
```

**Structure Decision**: Selected clean feature co-location under `Commerce.Application/Services/AuctionOrder/` paired 1-to-1 with `AuctionOrdersController.cs` in `Commerce.Api/Controllers/Public/`.

---

## 5. Complexity Tracking

> **Constitution Check: 0 Violations.** All principles satisfied without requiring complexity exemptions.
