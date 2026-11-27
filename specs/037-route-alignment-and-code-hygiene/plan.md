# Implementation Plan: Route Alignment, Gateway Parity & Compiler Warning Hygiene

**Branch**: `037-route-alignment-and-code-hygiene` | **Date**: 2026-11-26 | **Spec**: [spec.md](file:///d:/CommerceService/specs/037-route-alignment-and-code-hygiene/spec.md)

**Input**: Feature specification from `specs/037-route-alignment-and-code-hygiene/spec.md`

## Summary

This plan executes the remediation of 4 audited issues in CommerceService:
1. **Shipment Route Alignment**: Migrate master order shipment tracking from `GET /api/v1/shipments/order/{orderId}` to hierarchical RESTful route `GET /api/v1/orders/{orderId}/shipments` on `OrdersController.cs`, resolving Gateway prefix conflicts with `FulfillmentService` while preserving a backward-compatible alias.
2. **Compiler Warning Hygiene**: Eliminate all 6 compiler warnings (CS8604, CS8601, CS0108) across `AuctionOrderService`, `OrderService`, and `AdminRefundFundCasesController` using safe null-coalescing (`?? string.Empty`) and the `new` keyword modifier, achieving 0 errors and 0 warnings on Release build.
3. **Gateway Route Parity**: Deliver an exportable YARP routing contract mapping all 7 Commerce sub-routes to port 8003.
4. **Documentation Sync**: Update all 5 API documentation files in `docs/` to reflect canonical endpoint standards.

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (`net8.0`)  
**Primary Dependencies**: ASP.NET Core 8, EF Core 8 (`Npgsql.EntityFrameworkCore.PostgreSQL`), FluentValidation, Polly, Moq, FluentAssertions, xUnit  
**Storage**: PostgreSQL 16 (port `5435`, database `commerce_db`)  
**Testing**: xUnit (`tests/Commerce.UnitTests/Commerce.UnitTests.csproj`)  
**Target Platform**: Linux Docker Container (port `8003`) & Local Development (ports `5105`/`7035`)  
**Project Type**: ASP.NET Core Web API / Clean Architecture Microservice  
**Performance Goals**: < 50ms p95 response time for tracking lookup; 0 compiler warnings on Release build  
**Constraints**: Inward-Only repository boundary, fail-closed external calls, no Repository pattern, no Message Brokers / Saga  
**Scale/Scope**: 3 source files modified (`OrdersController.cs`, `OrderService.cs`, `AuctionOrderService.cs`, `AdminRefundFundCasesController.cs`), 5 documentation files updated, 0 breaking changes  

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Compliance Status | Analysis & Verification |
| :--- | :---: | :--- |
| **I. Clean Architecture & Direct EF Core** | ✅ PASS | No Repository or UnitOfWork pattern introduced. `OrderService` continues direct `AppDbContext` usage. |
| **II. Synchronous Typed REST & Fail-Closed** | ✅ PASS | Inter-service calls continue through `IFulfillmentClient` fail-closed. No Saga, Outbox, or Message Broker used. |
| **III. Immutable Infrastructure & REST Standards** | ✅ PASS | Ports `8003` and `5435` remain immutable. Adopts canonical plural route hierarchy (`/api/v1/orders/{orderId}/shipments`). |
| **IV. Standardized System-Wide Error Protocol** | ✅ PASS | Error codes adhere to `FORBIDDEN`, `ORDER_NOT_FOUND` in SCREAMING_SNAKE_CASE with 100% English sentence-case detail. |
| **V. Inward-Only Boundaries** | ✅ PASS | All code and documentation changes are 100% self-contained. Gateway configuration provided as a standalone spec artifact. |

*Gate Evaluation: PASSED. Zero constitution violations.*

## Project Structure

### Documentation (this feature)

```text
specs/037-route-alignment-and-code-hygiene/
├── plan.md              # This implementation plan
├── research.md          # Technical decisions, alternatives & rationale
├── data-model.md        # Value object & DTO nullability invariants
├── quickstart.md        # Run & verification instructions
├── gateway-routes-spec.json # YARP routing specification for Gateway
├── checklists/
│   └── requirements.md  # Specification quality checklist
└── contracts/
    └── orders-shipments-api.yaml # OpenAPI 3.0 contract for shipment tracking
```

### Source Code (repository root)

```text
Commerce.sln
├── Commerce.Domain/
│   └── ValueObjects/
│       └── ShippingAddressSnapshot.cs  # Snapshot entity with non-null address strings
├── Commerce.Application/
│   ├── DTOs/
│   │   └── ShippingAddressDto.cs       # Contract DTO with non-null address parameters
│   └── Services/
│       ├── AuctionOrder/
│       │   └── AuctionOrderService.cs  # Resolved CS8604 null argument warning
│       └── Order/
│           └── OrderService.cs         # Resolved CS8601 null assignment warnings
├── Commerce.Infrastructure/
│   └── Services/Fulfillment/
│       └── FulfillmentClient.cs        # Verified internal route cancellation
├── Commerce.Api/
│   └── Controllers/
│       ├── Public/
│       │   └── OrdersController.cs     # Canonical [HttpGet("{orderId:guid}/shipments")] + alias
│       └── Admin/
│           └── AdminRefundFundCasesController.cs # Resolved CS0108 member hiding warning
└── tests/
    └── Commerce.UnitTests/
        ├── Application/
        │   └── OrderLifecycleTests.cs  # Unit tests for order shipment tracking
        └── Infrastructure/
            └── FulfillmentClientCancelTests.cs # Unit tests for carrier cancellation
```

**Structure Decision**: Standard 4-layer Clean Architecture with Feature Co-location matching Commerce Constitution.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
| :---: | :---: | :---: |
| *None* | *N/A* | *N/A* |
