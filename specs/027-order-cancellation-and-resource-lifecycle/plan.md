# Implementation Plan: Comprehensive Order Cancellation & Resource Rollback Lifecycle

**Branch**: `027-order-cancellation-and-resource-lifecycle` | **Date**: 2026-11-17 | **Spec**: [specs/027-order-cancellation-and-resource-lifecycle/spec.md](file:///d:/CommerceService/specs/027-order-cancellation-and-resource-lifecycle/spec.md)

**Input**: Feature specification from `/specs/027-order-cancellation-and-resource-lifecycle/spec.md`

---

## Summary

Standardize the complete order cancellation lifecycle and automated resource rollbacks across `CommerceService`.
Key deliverables:
1. Enforce strict pre-shipping cancellation boundaries: once an order has progressed to `Shipping`, `Delivered`, or `Completed`, manual cancellation requests are strictly rejected with HTTP 400 `ORDER_CANNOT_BE_CANCELLED`.
2. Formalize the non-negotiable invariant that post-delivery sales are final (`Delivered`/`Completed`), strictly disallowing automated buyer refunds post-delivery.
3. Validate and optimize the high-throughput batch cancellation flow in `PaymentTimeoutWorker`: aggregate up to 50 expired orders, invoke single outbound `BatchReleaseStockAsync(orderIds)` to Fulfillment Service, batch release vouchers, and verify zero wallet interaction.
4. Guarantee 100% COD financial safety across all cancellation and return pathways (`Returned` status never invokes WalletService).

---

## Technical Context

**Language/Version**: C# / .NET 8.0  
**Primary Dependencies**: ASP.NET Core 8.0, Entity Framework Core 8.0, Microsoft.Extensions.Hosting (BackgroundService)  
**Storage**: PostgreSQL 16 (`commerce_db:5435`)  
**Testing**: xUnit, FluentAssertions, Moq (`dotnet test Commerce.sln -c Release`)  
**Target Platform**: Linux / Windows container runtime (port 8003)  
**Project Type**: RESTful Web API Microservice  
**Performance Goals**: Cancellation execution < 300ms p95; batch timeout sweep < 2s for 50 orders  
**Constraints**: Inward-only referencing, no Repository pattern over EF Core, RFC 9110 ProblemDetails, 100% English sentence-case error messages  
**Scale/Scope**: Multi-vendor marketplace orders, batch sweeps of up to 50 orders, carrier webhook return handling  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **I. Clean Architecture & Direct EF Core Access**: 4-layer structure maintained (`Api`, `Application`, `Domain`, `Infrastructure`). Direct access to `ICommerceDbContext` in Application services and Background workers.
- [x] **II. Synchronous Typed REST Orchestration**: Inter-service calls coordinated via typed `IFulfillmentClient`, `IWalletClient`, `IUserClient`. No Message Broker, Saga, or Outbox tables.
- [x] **III. Immutable Infrastructure & REST Ports**: Port 8003 runtime, Port 5435 PostgreSQL, RFC 9110 Problem Details responses.
- [x] **IV. Standardized System-Wide Error Protocol**: 100% English sentence-case messages (`"Order cannot be cancelled once shipped, delivered, or completed."`), SCREAMING_SNAKE_CASE codes (`ORDER_CANNOT_BE_CANCELLED`).
- [x] **V. Self-Contained Repository & Inward-Only Boundaries**: Zero references outside `CommerceService`.
- [x] **Quality Gates**: Clean Build, 0 errors, 100% test pass.

---

## Project Structure

### Documentation (this feature)

```text
specs/027-order-cancellation-and-resource-lifecycle/
├── spec.md              # Feature specification
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── order-cancellation-lifecycle-api.yaml
└── tasks.md             # Phase 2 output (/speckit-tasks command)
```

### Source Code Touches

```text
Commerce.Application/
└── Services/
    └── Order/
        └── OrderService.cs            # Verify pre-shipping guards, COD safety on PackageReturned, and voucher rollback

Commerce.Api/
└── Workers/
    └── PaymentTimeoutWorker.cs        # Verify batch release to fulfillment and zero wallet invocation

tests/
└── Commerce.UnitTests/
    └── Application/
        ├── OrderLifecycleTests.cs     # Tests for cancellation boundary guards (Shipping/Delivered block)
        ├── PaymentTimeoutWorkerTests.cs # Tests for batch expiration without wallet calls
        └── OrderSettlementRefundTests.cs # Tests for COD package return safety
```

**Structure Decision**: Standard 4-layer Clean Architecture without additions.

---

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None      | N/A        | Clean Architecture adhered to 100%  |
