# Implementation Plan: Fulfillment Delivery Synchronization and Sub-Order Lifecycle Governance

**Branch**: `025-fulfillment-delivery-and-suborder-lifecycle` | **Date**: 2026-11-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/025-fulfillment-delivery-and-suborder-lifecycle/spec.md`

---

## Summary

Harmonize the delivery and lifecycle boundaries between `CommerceService` and `FulfillmentService` by:
1. Expanding delivery status webhook contracts to ingest carrier Proof-of-Delivery (POD) photos and courier metadata.
2. Gating buyer receipt confirmation strictly to `Delivered` status, eliminating premature escrow fund releases.
3. Introducing an independent sub-order cancellation endpoint (`POST /api/v1/sub-orders/{subOrderId}/cancel`) for buyers in multi-vendor orders, coordinated with atomic stock release and carrier shipment cancellation.
4. Implementing an automated background lifecycle worker to disburse seller escrow payouts for delivered orders past a configurable inspection window (default 3 days, configurable to 3 minutes for evaluation).
5. Enforcing the Cash on Delivery (COD) invariant to bypass digital wallet escrow deductions during receipt confirmation.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8.0  
**Primary Dependencies**: ASP.NET Core 8.0, Microsoft.EntityFrameworkCore 8.0.10, Npgsql.EntityFrameworkCore.PostgreSQL 8.0.10  
**Storage**: PostgreSQL (port 5435, database `commerce_db`) accessed directly via EF Core (`ICommerceDbContext` / `AppDbContext`)  
**Testing**: xUnit, FluentAssertions, Moq (`tests/Commerce.UnitTests`)  
**Target Platform**: Linux Docker / Windows Server, Service container port 8003  
**Project Type**: Clean Architecture Web API (4 layers: `Commerce.Api`, `Commerce.Application`, `Commerce.Domain`, `Commerce.Infrastructure`)  
**Performance Goals**: Sub-50ms webhook response time; deduplication window of 2 seconds for carrier retries; background scan batch size 50  
**Constraints**: Inward-only scope; Clean Architecture; No repository pattern over EF Core; Zero message brokers (100% typed REST clients); Sentence-case English error messages; PascalCase JSON enum serialization  
**Scale/Scope**: Multi-vendor marketplace orders with $N$ sub-orders per checkout  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle / Rule | Compliance Status | Analysis & Verification |
| :--- | :---: | :--- |
| **I. Clean Architecture & Direct EF Core** | ✅ PASS | Controllers in `Commerce.Api` delegate directly to `IOrderService` in `Commerce.Application`. Data access occurs directly via `ICommerceDbContext` with no repository wrappers. |
| **II. Synchronous Typed REST Orchestration** | ✅ PASS | Inter-service coordination uses `IFulfillmentClient` (`FulfillmentClient.cs`) and `IWalletClient`. Zero message brokers or saga state machines. |
| **III. Immutable Infrastructure & REST** | ✅ PASS | Port 8003 and PostgreSQL 5435 maintained. All routes adhere to plural noun conventions (`/api/v1/sub-orders/{subOrderId}/cancel`). |
| **IV. Standardized System Error Protocol** | ✅ PASS | 100% English sentence-case error messages (`"Sub-order cannot be confirmed until it is delivered."`). Error codes in SCREAMING_SNAKE_CASE (`INVALID_STATUS`, `SUB_ORDER_CANNOT_BE_CANCELLED`). |
| **V. Inward-Only Boundaries** | ✅ PASS | 100% self-contained within `CommerceService`. No relative paths pointing outside repository root. |

---

## Project Structure

### Documentation (this feature)

```text
specs/025-fulfillment-delivery-and-suborder-lifecycle/
├── plan.md                                                  # Implementation plan
├── research.md                                              # Technical decisions and research findings
├── data-model.md                                            # Entity schema, DTOs, and state machine invariants
├── quickstart.md                                            # Verification and execution scenarios
├── checklists/
│   └── requirements.md                                      # Quality validation checklist
└── contracts/
    ├── fulfillment-delivery-webhook-api.yaml                # Delivery status callback OpenAPI spec
    ├── suborder-cancellation-api.yaml                       # Sub-order cancellation OpenAPI spec
    └── fulfillment-outbound-cancellation-api.yaml           # Outbound stock release & shipment cancel spec
```

### Source Code (repository layout)

```text
Commerce.Api/
├── Controllers/
│   └── Public/
│       └── OrdersController.cs                              # Add POST /api/v1/sub-orders/{subOrderId}/cancel
│                                                            # Update POST /api/v1/sub-orders/{subOrderId}/delivery-status
└── Workers/
    └── PaymentTimeoutWorker.cs                              # Add Auto-Complete scan for delivered sub-orders

Commerce.Application/
├── Interfaces/
│   ├── IFulfillmentClient.cs                                # Add CancelShipmentAsync signature
│   └── IOrderService.cs                                     # Add CancelSubOrderAsync signature
└── Services/
    └── Order/
        ├── OrderRequests.cs                                 # Expand UpdateDeliveryStatusRequest; add CancelSubOrderRequest
        ├── OrderResponses.cs                                # Add CancelSubOrderResponse
        └── OrderService.cs                                  # Implement CancelSubOrderAsync, update confirm/complete guards

Commerce.Infrastructure/
└── Services/
    └── Fulfillment/
        └── FulfillmentClient.cs                             # Implement CancelShipmentAsync calling /api/v1/shipments/{id}/cancel

tests/Commerce.UnitTests/
└── Application/
    ├── OrderLifecycleTests.cs                               # Unit tests for guarded confirmation, sub-order cancel, auto-complete
    └── Controllers/
        └── OrdersControllerDeliveryCallbackTests.cs         # Webhook callback tests with POD & courier info
```

**Structure Decision**: Fully adheres to the existing 4-layer Clean Architecture. All feature modifications reside in their co-located `Order` feature folders across `Api`, `Application`, `Infrastructure`, and `UnitTests`.

---

## Complexity Tracking

> **Constitution Check has 0 violations. All changes follow standard architectural conventions.**

| Violation | Why Needed | Simpler Alternative Rejected Because |
| :---: | :---: | :---: |
| *None* | *None* | *None* |
