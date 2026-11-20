# Implementation Plan: Fulfillment Service Internal REST Endpoint Alignment

**Branch**: `013-fulfillment-endpoint-alignment` | **Date**: 2026-11-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/013-fulfillment-endpoint-alignment/spec.md`

---

## Summary

Harmonize all outbound inter-service HTTP endpoints in `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs` with the finalized Fulfillment Service contract (Tasks F01–F08):
1. Route partial stock release for rejected sub-orders to `POST /api/v1/internal/inventories/reservations/{orderId}/release-items` with body `{ items, reason }`.
2. Route dynamic carrier shipping quotes to `POST /api/v1/internal/shipping/quote`.
3. Route carrier shipment dispatch to `POST /api/v1/internal/shipments`.
4. Update unit test mock assertions in `tests/Commerce.UnitTests/Infrastructure/FulfillmentClientTests.cs` and local OpenAPI specifications.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8.0  
**Primary Dependencies**: ASP.NET Core 8.0, `System.Net.Http.Json`, `Microsoft.Extensions.Http`, `System.Text.Json`  
**Storage**: PostgreSQL 16 (`commerce_db:5435`) accessed directly via EF Core (`ICommerceDbContext`), no repository layer  
**Testing**: xUnit, Moq, FluentAssertions  
**Target Platform**: Linux / Windows container listening on Port `8003`  
**Project Type**: RESTful Microservice (Clean Architecture)  
**Performance Goals**: < 100ms client serialization and dispatch latency  
**Constraints**: 100% Inward-Only boundary, strictly zero RabbitMQ/MassTransit/gRPC, RFC 9110 Fail-Closed 503 error governance  
**Scale/Scope**: 3 client endpoints in `FulfillmentClient.cs`, 3 unit test fixtures in `FulfillmentClientTests.cs`, 1 OpenAPI contract file  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Requirement | Status | Notes |
| :--- | :--- | :---: | :--- |
| **I. Clean Architecture** | 4-layer separation, no repository pattern over EF Core. | ✅ PASS | Interface remains in `Commerce.Application`, implementation in `Commerce.Infrastructure`. |
| **II. Synchronous REST Orchestration** | 100% Typed HTTP REST Clients, Fail-Closed policy, zero mock fallbacks. | ✅ PASS | Targets internal REST routes with fail-closed HTTP 503 error handling. |
| **III. Immutable Infrastructure** | Container port 8003, database port 5435, Fulfillment port 8006. | ✅ PASS | Fallback BaseUrl configured to `http://localhost:8006/`. |
| **IV. Error Protocol** | 100% English, Sentence case messages, `SCREAMING_SNAKE_CASE` codes. | ✅ PASS | Errors use `FULFILLMENT_SERVICE_UNAVAILABLE` (503) and `SHIPPING_QUOTE_FAILED` (400). |
| **V. Inward-Only Boundaries** | No links or references pointing outside `CommerceService`. | ✅ PASS | All path references are strictly inward-only. |

---

## Project Structure

### Documentation (this feature)

```text
specs/013-fulfillment-endpoint-alignment/
├── spec.md              # Feature specification
├── plan.md              # Implementation plan (this file)
├── research.md          # Phase 0 architectural decisions
├── data-model.md        # Phase 1 DTO schemas & sequences
├── quickstart.md        # Phase 1 verification scenarios
├── checklists/
│   └── requirements.md  # Specification quality checklist
├── contracts/
│   └── fulfillment-contracts.json # OpenAPI 3.0 contract definition
└── tasks.md             # Phase 2 task execution breakdown (generated via /speckit-tasks)
```

### Source Code (CommerceService Clean Architecture)

```text
Commerce.Domain/
└── Enums/
    └── CancellationReasonCode.cs

Commerce.Application/
└── Interfaces/
    └── IFulfillmentClient.cs

Commerce.Infrastructure/
├── DependencyInjection.cs
└── Services/
    └── Fulfillment/
        └── FulfillmentClient.cs

tests/Commerce.UnitTests/
└── Infrastructure/
    └── FulfillmentClientTests.cs
```

**Structure Decision**: The modifications are localized to `FulfillmentClient.cs` in `Commerce.Infrastructure` and verified by unit test mocks in `FulfillmentClientTests.cs`, preserving the Clean Architecture inward dependency flow.

---

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
| :--- | :--- | :--- |
| *None* | Zero constitutional violations | Standard typed REST client alignment |
