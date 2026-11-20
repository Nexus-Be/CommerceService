# Tasks: Real-World Carrier Logistics and Shipment Tracking

**Input**: Design documents from `specs/021-real-world-shipping-and-tracking/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/logistics-contracts.json`, `quickstart.md`  
**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.  

## Format: `[ID] [P?] [Story] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Mapped user story (US1, US2, US3, US4, US5)
- Exact file paths included in all task descriptions

---

## Phase 1: Setup & Foundational Prerequisites

**Purpose**: Database schema expansion and baseline models required before any user story can proceed

- [x] T001 [P] Add persistent carrier tracking columns (`CarrierCode`, `TrackingCode`, `LabelPrintUrl`, `EstimatedDeliveryAt`, `ShipmentPendingCreation`) to `Commerce.Domain/Entities/SubOrder.cs`
- [x] T002 [P] Add persistent carrier tracking columns (`CarrierCode`, `TrackingCode`, `LabelPrintUrl`, `EstimatedDeliveryAt`) to `Commerce.Domain/Entities/AuctionOrder.cs`
- [x] T003 Configure Fluent API mapping and B-tree index on `TrackingCode` in `Commerce.Infrastructure/Persistence/Configurations/SubOrderConfiguration.cs`
- [x] T004 Create and verify EF Core database migration `20261112_AddShipmentTrackingToSubOrder.cs` in `Commerce.Infrastructure/Persistence/Migrations/`

**Checkpoint**: Database schema and domain entities ready — user stories can now be implemented.

---

## Phase 2: User Story 1 - Persistent Carrier Shipment Creation & Dispatch (Priority: P1) 🎯 MVP

**Goal**: Store genuine carrier tracking credentials returned by Fulfillment Service upon seller acceptance, allow shipment retries on network error, and eliminate mock tracking strings.

**Independent Test**: Seller accepts an order; verify `SubOrders` row stores `TrackingCode = "GHN-SGN-1029384"`, `CarrierCode = "GHN"`, and `GetShipmentTrackingAsync` returns authentic tracking codes with zero `GHN-{subOrderNo}` occurrences.

### Tests for User Story 1
- [x] T005 [P] [US1] Add unit tests in `tests/Commerce.UnitTests/Application/OrderServiceTests.cs` verifying `SellerAcceptSubOrderAsync` persists `TrackingCode`, `CarrierCode`, and `LabelPrintUrl`
- [x] T006 [P] [US1] Add unit tests in `tests/Commerce.UnitTests/Application/OrderServiceTests.cs` asserting `GetShipmentTrackingAsync` returns real tracking codes without artificial mock formatting

### Implementation for User Story 1
- [x] T007 [P] [US1] Add `RetryShipmentResponse` record to `Commerce.Application/Services/Order/OrderResponses.cs`
- [x] T008 [P] [US1] Declare `RetryCreateShipmentAsync(Guid sellerId, Guid subOrderId, CancellationToken ct)` in `Commerce.Application/Services/Order/IOrderService.cs`
- [x] T009 [US1] Update `SellerAcceptSubOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` to capture `ShipmentCreationResult`, persist credentials, set `ShipmentPendingCreation = false`, or catch transient 503 setting `ShipmentPendingCreation = true`
- [x] T010 [US1] Implement `RetryCreateShipmentAsync` in `Commerce.Application/Services/Order/OrderService.cs` to invoke `CreateShipmentAsync`, save credentials, and reset `ShipmentPendingCreation = false`
- [x] T011 [US1] Refactor `GetShipmentTrackingAsync` in `Commerce.Application/Services/Order/OrderService.cs` to return `so.TrackingCode` directly from entity without synthetic string interpolation
- [x] T012 [US1] Add endpoint `POST /api/v1/sub-orders/{subOrderId:guid}/shipments/retry` to `Commerce.Api/Controllers/Public/OrdersController.cs` enforcing seller authorization

**Checkpoint**: User Story 1 functional and testable independently.

---

## Phase 3: User Story 2 - Real-Time Carrier Delivery Webhook & Visual Timeline (Priority: P1) 🎯 MVP

**Goal**: Ingest carrier status updates into `OrderStatusHistories` and expose a chronological tracking timeline for buyers and sellers.

**Independent Test**: Send webhook callbacks with `HandedOver`, `InTransit`, and `Delivered`; query `GET /api/v1/sub-orders/{subOrderId}/shipments/timeline` and assert all events match chronologically with location and timestamps.

### Tests for User Story 2
- [x] T013 [P] [US2] Add unit tests in `tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs` verifying delivery status webhooks append `OrderStatusHistory` entries for milestone transitions
- [x] T014 [P] [US2] Add unit tests in `tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs` verifying `GetShipmentTimelineAsync` returns chronological timeline events with ownership validation

### Implementation for User Story 2
- [x] T015 [P] [US2] Update `UpdateDeliveryStatusRequest` in `Commerce.Application/Services/Order/OrderRequests.cs` with optional `CarrierCode`, `TrackingCode`, `Note`, and `Location`
- [x] T016 [P] [US2] Add `TimelineEventDto` and `ShipmentTimelineResponse` records to `Commerce.Application/Services/Order/OrderResponses.cs`
- [x] T017 [P] [US2] Declare `GetShipmentTimelineAsync(Guid currentUserId, Guid subOrderId, CancellationToken ct)` in `Commerce.Application/Services/Order/IOrderService.cs`
- [x] T018 [US2] Update `UpdateSubOrderDeliveryStatusAsync` in `Commerce.Application/Services/Order/OrderService.cs` to append `OrderStatusHistory` with actor role `System`, status transition, timestamp, and location note for all milestone events
- [x] T019 [US2] Implement `GetShipmentTimelineAsync` in `Commerce.Application/Services/Order/OrderService.cs` querying `SubOrder`, verifying buyer/seller ownership, loading chronological `OrderStatusHistories`, and mapping to `ShipmentTimelineResponse`
- [x] T020 [US2] Add endpoint `GET /api/v1/sub-orders/{subOrderId:guid}/shipments/timeline` to `Commerce.Api/Controllers/Public/OrdersController.cs`

**Checkpoint**: User Story 2 functional and testable independently.

---

## Phase 4: User Story 3 - Master Order State Aggregation & Delivery Completion (Priority: P2)

**Goal**: Automatically transition master `Order.Status` to `OrderStatus.Delivered` when 100% of sibling sub-orders complete delivery.

**Independent Test**: In a multi-shop order with 2 sub-orders, deliver SubOrder A (Order remains `Paid`), then deliver SubOrder B, asserting parent `Order.Status` transitions to `Delivered`.

### Tests for User Story 3
- [x] T021 [P] [US3] Add unit tests in `tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs` verifying multi-shop master order status transitions to `Delivered` upon final sub-order completion

### Implementation for User Story 3
- [x] T022 [US3] Update `UpdateSubOrderDeliveryStatusAsync` in `Commerce.Application/Services/Order/OrderService.cs` to query all sibling `SubOrders` of the parent `OrderId` and update `Order.Status = OrderStatus.Delivered` when all siblings are `Delivered` or `Completed`

**Checkpoint**: User Story 3 functional and testable independently.

---

## Phase 5: User Story 4 - Delivery Exception Handling & Automated Return/Refund (Priority: P2)

**Goal**: Handle delivery attempt failures without premature cancellation; automatically process wallet refund and inventory release when a package is returned.

**Independent Test**: Post `DeliveryAttemptFailed` (sub-order remains `Shipped`, history recorded), then post `Returned` on a paid sub-order, asserting status is `Cancelled`, `OrderRefund` created, wallet refund triggered, and `ReleaseSubOrderStockAsync` invoked.

### Tests for User Story 4
- [x] T023 [P] [US4] Add unit tests in `tests/Commerce.UnitTests/Application/OrderSettlementRefundTests.cs` verifying `DeliveryAttemptFailed` does not cancel order and `Returned` triggers automated wallet refund and inventory release

### Implementation for User Story 4
- [x] T024 [US4] Update `UpdateSubOrderDeliveryStatusAsync` in `Commerce.Application/Services/Order/OrderService.cs` to handle `DeliveryAttemptFailed` by logging history while retaining `Shipped` status
- [x] T025 [US4] Update `UpdateSubOrderDeliveryStatusAsync` in `Commerce.Application/Services/Order/OrderService.cs` on `Returned` to transition to `Cancelled`, issue wallet refund via `_walletClient.RefundSubOrderPaymentAsync` if paid, and release warehouse stock via `_fulfillmentClient.ReleaseSubOrderStockAsync` with reason `"PackageReturned"`

**Checkpoint**: User Story 4 functional and testable independently.

---

## Phase 6: User Story 5 - Dynamic Weight-Based Carrier Quotation (Priority: P3)

**Goal**: Calculate dynamic package weights from item quantities and validate GHN destination codes during checkout preview.

**Independent Test**: Calculate shipping quotes with line items of varying weights, verifying outbound `ShopShippingQuoteRequest` payload serializes accurate weights.

### Tests for User Story 5
- [x] T026 [P] [US5] Add unit tests in `tests/Commerce.UnitTests/Application/OrderCheckoutTests.cs` verifying dynamic weight calculation and address validation

### Implementation for User Story 5
- [x] T027 [US5] Update `OrderService.PreviewCheckoutAsync` in `Commerce.Application/Services/Order/OrderService.cs` to compute cumulative item weights (`quantity * unitWeightGrams`, minimum 100g) for `ShopShippingQuoteRequest`
- [x] T028 [US5] Add destination address validation in `OrderService.PreviewCheckoutAsync` verifying `GhnDistrictId > 0` and non-empty `GhnWardCode`, throwing `CommerceDomainException("INVALID_SHIPPING_ADDRESS", ...)` on invalid address

**Checkpoint**: User Story 5 functional and testable independently.

---

## Phase 7: Polish, Verification & Quality Gates

**Purpose**: System-wide test suite verification and documentation alignment

- [x] T029 Update OpenAPI documentation in `docs/COMMERCE_API_CONTRACT.md` with new timeline and retry routes
- [x] T030 Execute full validation scenarios from `specs/021-real-world-shipping-and-tracking/quickstart.md`
- [x] T031 Run `dotnet build Commerce.sln -c Release` confirming 0 errors and 0 warnings
- [x] T032 Run `dotnet test Commerce.sln -c Release` confirming 100% test pass rate

---

## Dependencies & Execution Order

### Phase Dependencies
- **Phase 1 (Foundational)**: No dependencies — can start immediately. BLOCKS all user stories.
- **Phase 2 (US1 - MVP)**: Depends on Phase 1 completion.
- **Phase 3 (US2 - MVP)**: Depends on Phase 1 completion. Can execute in parallel with or after US1.
- **Phase 4 (US3)**: Depends on Phase 3 completion (extends `UpdateSubOrderDeliveryStatusAsync`).
- **Phase 5 (US4)**: Depends on Phase 3 completion (extends `UpdateSubOrderDeliveryStatusAsync`).
- **Phase 6 (US5)**: Depends on Phase 1 completion (independent checkout preview logic).
- **Phase 7 (Polish)**: Depends on all user stories being complete.

### Parallel Opportunities
- Foundational entities (T001, T002) can run in parallel.
- DTO records and interface declarations across US1 and US2 (T007, T008, T015, T016, T017) can be authored in parallel.
- Test creation tasks marked with `[P]` can be authored in parallel.
