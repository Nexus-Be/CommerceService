# Implementation Plan: Real-World Carrier Logistics and Shipment Tracking

**Branch**: `021-real-world-shipping-and-tracking` | **Date**: 2026-11-12 | **Spec**: [specs/021-real-world-shipping-and-tracking/spec.md](spec.md)

**Input**: Feature specification from `specs/021-real-world-shipping-and-tracking/spec.md`

---

## Summary

Modernize and elevate CommerceService's logistics subsystem to authentic e-commerce standards (Shopee / Lazada / GHN integration):
1. **Persistent Carrier Credentials**: Extend `SubOrder` and `AuctionOrder` schemas to persist real carrier metadata (`CarrierCode`, `TrackingCode`, `LabelPrintUrl`, `EstimatedDeliveryAt`, `ShipmentPendingCreation`) returned by `FulfillmentService.CreateShipmentAsync`, eradicating synthetic mock strings (`$"GHN-{subOrderNo}"`).
2. **Chronological Tracking Timeline**: Capture all inbound webhook carrier milestones (`ReadyToPick`, `Picking`, `Packed`, `HandedOver`, `InTransit`, `OutForDelivery`, `DeliveryAttemptFailed`, `Delivered`, `Returned`) into `OrderStatusHistories` and expose visual query `GET /api/v1/sub-orders/{subOrderId}/shipments/timeline`.
3. **Master Order State Aggregator**: Automatically transition parent `Order.Status` to `Delivered` when all sibling sub-orders conclude delivery.
4. **Delivery Exception Resilience**: Distinguish temporary attempt failures from terminal returns; trigger automated wallet refunds and warehouse inventory releases upon package return.
5. **Resilient Shipment Retry**: Provide explicit retry endpoint `POST /api/v1/sub-orders/{subOrderId}/shipments/retry` leveraging Fulfillment Service's safe idempotency.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (`net8.0`)  
**Primary Dependencies**: ASP.NET Core 8.0, Microsoft.EntityFrameworkCore 8.0.8, Npgsql.EntityFrameworkCore.PostgreSQL 8.0.4, FluentAssertions, Moq, xUnit  
**Storage**: PostgreSQL (`commerce_db:5435`), `SubOrders`, `AuctionOrders`, `Orders`, `OrderStatusHistories`, `OrderRefunds`, `OrderCancellations`  
**Testing**: xUnit with Moq and FluentAssertions (`Commerce.UnitTests`)  
**Target Platform**: Linux / Windows containerized microservice (Docker / Kubernetes), Port 8003  
**Project Type**: Inward-only ASP.NET Core Web API with Clean Architecture (Domain, Application, Infrastructure, Api)  
**Performance Goals**: Sub-50ms latency for shipment timeline queries via indexed `TrackingCode` and indexed `SubOrderId` foreign keys; single `SaveChangesAsync` transaction per webhook callback  
**Constraints**: 100% Inward-Only, No Repository over EF Core, Fail-Closed (no dummy mock fallbacks), 100% English sentence-case error messages with SCREAMING_SNAKE_CASE codes  
**Scale/Scope**: Multi-shop master orders with up to 20 sub-orders; 9 distinct carrier delivery milestones; 100% automated test pass rate  

---

## Constitution Check

*GATE: Must pass before implementation. Re-check after Phase 1 design.*

- [x] **I. Clean Architecture & Direct EF Core Access**: All logic placed in `Commerce.Application/Services/Order/`, interacting directly with `ICommerceDbContext` and entity sets. No repository or UnitOfWork pattern introduced.
- [x] **II. Synchronous Typed HTTP REST Orchestration & Compensation**: Outbound calls route strictly through `IFulfillmentClient` and `IWalletClient`. Zero message brokers or Saga state machines. Automated compensation triggers wallet refund and inventory release upon `Returned`.
- [x] **III. Immutable Infrastructure & RESTful API Architecture**: Port 8003 maintained. Plural route conventions (`/api/v1/sub-orders/{subOrderId}/shipments/timeline`, `/shipments/retry`, `/delivery-status`). `X-Internal-Service-Key` header authentication enforced.
- [x] **IV. Standardized System-Wide Error Protocol**: 100% English sentence-case messages, SCREAMING_SNAKE_CASE error codes (`SUB_ORDER_NOT_FOUND`, `INVALID_SHIPPING_ADDRESS`, `SHIPMENT_ALREADY_EXISTS`, `FORBIDDEN`).
- [x] **V. Self-Contained Repository & Inward-Only Boundaries**: Zero external workspace or sibling service file links. All assets self-contained in `Commerce.sln`.

---

## Project Structure

### Documentation (this feature)

```text
specs/021-real-world-shipping-and-tracking/
├── plan.md              # This implementation plan
├── research.md          # Technical trade-offs & decisions
├── data-model.md        # Schema changes, entities, and DTO contracts
├── quickstart.md        # End-to-end verification scenarios
├── contracts/           # OpenAPI 3.0 specification (logistics-contracts.json)
│   └── logistics-contracts.json
├── checklists/
│   └── requirements.md  # Specification quality checklist
└── tasks.md             # Implementation tasks (generated via speckit-tasks)
```

### Source Code Touches

```text
Commerce.Domain/
├── Entities/
│   ├── SubOrder.cs                      # Added CarrierCode, TrackingCode, LabelPrintUrl, EstimatedDeliveryAt, ShipmentPendingCreation
│   └── AuctionOrder.cs                  # Added CarrierCode, TrackingCode, LabelPrintUrl, EstimatedDeliveryAt
Commerce.Infrastructure/
├── Persistence/
│   ├── Configurations/
│   │   └── SubOrderConfiguration.cs     # MaxLength, Index on TrackingCode, ShipmentPendingCreation default
│   └── Migrations/
│       └── 20261112_AddShipmentTrackingToSubOrder.cs # EF Core migration script
Commerce.Application/
├── Interfaces/
│   └── IFulfillmentClient.cs            # Verified ShipmentCreationResult contract
├── Services/Order/
│   ├── IOrderService.cs                 # Added GetShipmentTimelineAsync, RetryCreateShipmentAsync
│   ├── OrderService.cs                  # Persistent shipment capture, timeline ingestion, master sync, return refund
│   ├── OrderRequests.cs                 # Extended UpdateDeliveryStatusRequest (optional carrier/note/location)
│   └── OrderResponses.cs                # Updated SubOrderShipmentInfo, added ShipmentTimelineResponse, TimelineEventDto
Commerce.Api/
└── Controllers/Public/
    └── OrdersController.cs             # Exposed /timeline and /shipments/retry endpoints
tests/Commerce.UnitTests/
└── Application/
    ├── OrderServiceTests.cs             # Tests for authentic tracking storage, retry, and timeline query
    └── OrderLifecycleTests.cs           # Tests for master order status sync and package returned refund flow
```

---

## Phases & Execution Strategy

### Phase 1: Persistence & Schema Alignment
- Add persistent properties to `SubOrder.cs` and `AuctionOrder.cs`.
- Update `SubOrderConfiguration.cs` with B-tree index on `TrackingCode` and column limits.
- Generate and verify EF Core database migration `AddShipmentTrackingToSubOrder`.

### Phase 2: Application DTOs & Contracts
- Update `OrderRequests.cs` with extended `UpdateDeliveryStatusRequest` parameters (`Note`, `Location`, `CarrierCode`, `TrackingCode`).
- Update `OrderResponses.cs` with `TimelineEventDto`, `ShipmentTimelineResponse`, `RetryShipmentResponse`.
- Add `GetShipmentTimelineAsync` and `RetryCreateShipmentAsync` interface declarations to `IOrderService.cs`.

### Phase 3: Service Logic Refactoring (`OrderService.cs`)
- Refactor `SellerAcceptSubOrderAsync`: Store `shipmentResult.TrackingCode`, `CarrierCode`, `LabelPrintUrl`, set `ShipmentPendingCreation = false`. On transient failure, set `ShipmentPendingCreation = true`.
- Implement `RetryCreateShipmentAsync`: Verify `ShipmentPendingCreation == true` and `TrackingCode == null`, invoke `CreateShipmentAsync`, persist credentials.
- Refactor `GetShipmentTrackingAsync`: Return `so.TrackingCode` and `so.CarrierCode` directly from entity without fake `GHN-...` fallback.
- Implement `GetShipmentTimelineAsync`: Query `SubOrder`, verify buyer/seller ownership, load chronological `OrderStatusHistories`, map to `ShipmentTimelineResponse`.
- Refactor `UpdateSubOrderDeliveryStatusAsync`:
  - Append `OrderStatusHistory` for every webhook transition with actor `System`, status, and location note.
  - On `Delivered`: Query siblings to auto-transition parent `Order.Status` to `OrderStatus.Delivered`.
  - On `DeliveryAttemptFailed`: Append history record without cancellation.
  - On `Returned`: Set `Cancelled`, create `OrderCancellation`, trigger `_walletClient.RefundSubOrderPaymentAsync` if paid, invoke `_fulfillmentClient.ReleaseSubOrderStockAsync`.

### Phase 4: Api Controllers & Endpoints
- Add `GET /api/v1/sub-orders/{subOrderId:guid}/shipments/timeline` to `OrdersController.cs` with buyer/seller ownership checks.
- Add `POST /api/v1/sub-orders/{subOrderId:guid}/shipments/retry` to `OrdersController.cs` with seller authorization.
- Verify `POST /api/v1/sub-orders/{subOrderId:guid}/delivery-status` authenticates `X-Internal-Service-Key`.

### Phase 5: Test Suite Verification & Quality Gates
- Update unit tests in `Commerce.UnitTests/Application/OrderServiceTests.cs` and `OrderLifecycleTests.cs`.
- Run `dotnet test Commerce.sln -c Release` to verify 100% test pass rate.
- Confirm zero warnings and compliance with Definition of Done.
