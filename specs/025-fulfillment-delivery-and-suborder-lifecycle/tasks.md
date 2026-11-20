---
description: "Task list for feature 025: Fulfillment Delivery Synchronization and Sub-Order Lifecycle Governance"
---

# Tasks: Fulfillment Delivery Synchronization and Sub-Order Lifecycle Governance

**Input**: Design documents from `specs/025-fulfillment-delivery-and-suborder-lifecycle/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`  

## Format: `- [ ] [TaskID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (different files, no blocking dependencies)
- **[Story]**: Target user story (`[US1]`, `[US2]`, `[US3]`, `[US4]`)
- Exact file paths included in all task descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configure runtime settings and declare shared client contracts for fulfillment integration.

- [X] T001 Configure `OrderLifecycle:AutoCompleteDeliveredMinutes` default value (4320 minutes) in `Commerce.Api/appsettings.json`
- [X] T002 Add `CancelShipmentAsync(Guid subOrderId, string reason, CancellationToken ct)` method signature to `Commerce.Application/Interfaces/IFulfillmentClient.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core DTOs, service interfaces, and client implementations required across all user stories.

**⚠️ CRITICAL**: Must be completed before User Story implementation begins.

- [X] T003 [P] Update `UpdateDeliveryStatusRequest` record in `Commerce.Application/Services/Order/OrderRequests.cs` to add optional fields `PodUrl`, `ShipperName`, and `ShipperPhone`
- [X] T004 [P] Add `CancelSubOrderRequest` and `CancelSubOrderResponse` DTO records in `Commerce.Application/Services/Order/OrderRequests.cs` and `Commerce.Application/Services/Order/OrderResponses.cs`
- [X] T005 [P] Add `CancelSubOrderAsync(Guid currentUserId, Guid subOrderId, CancelSubOrderRequest request, CancellationToken ct)` method signature to `Commerce.Application/Services/Order/IOrderService.cs`
- [X] T006 Implement `CancelShipmentAsync` in `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs` calling `POST api/v1/shipments/{subOrderId}/cancel` with internal service secret header

**Checkpoint**: Foundation ready — user story implementations can proceed.

---

## Phase 3: User Story 1 - Carrier Delivery & Proof-of-Delivery Sync (Priority: P1) 🎯 MVP

**Goal**: Ingest delivery status callbacks from FulfillmentService containing courier info and POD URL, transitioning SubOrder to `Delivered` without premature escrow settlement.

**Independent Test**: Send webhook payload with courier details and POD URL to `POST /api/v1/sub-orders/{subOrderId}/delivery-status`. Verify SubOrder transitions to `Delivered`, history is logged with POD details, and escrow funds remain locked.

### Tests for User Story 1

- [X] T007 [P] [US1] Add unit tests for delivery status webhook with POD URL and courier details in `tests/Commerce.UnitTests/Controllers/OrdersControllerDeliveryCallbackTests.cs`

### Implementation for User Story 1

- [X] T008 [US1] Update `FormatHistoryReason` in `Commerce.Application/Services/Order/OrderService.cs` to append courier name, phone, and POD URL to history reason
- [X] T009 [US1] Update `UpdateSubOrderDeliveryStatusAsync` in `Commerce.Application/Services/Order/OrderService.cs` to persist carrier code, tracking code, `DeliveredAt`, and history record while keeping escrow funds locked
- [X] T010 [US1] Verify `OrdersController.UpdateDeliveryStatus` in `Commerce.Api/Controllers/Public/OrdersController.cs` validates `X-Internal-Service-Key` header and forwards expanded request

**Checkpoint**: User Story 1 functional and independently verifiable (MVP).

---

## Phase 4: User Story 2 - Buyer Delivery Confirmation & Escrow Settlement (Priority: P2)

**Goal**: Guard receipt confirmation so buyers can confirm only when goods are `Delivered`, triggering escrow payout, reputation reward, and review permissions.

**Independent Test**: Attempt confirmation when SubOrder is in `Shipped` status (must fail with `INVALID_STATUS`). Transition SubOrder to `Delivered` and confirm (must succeed, settle funds, and award reputation).

### Tests for User Story 2

- [X] T011 [P] [US2] Add unit tests in `tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs` verifying `BuyerConfirmSubOrderReceiptAsync` and `CompleteOrderAsync` reject confirmation unless status is `Delivered`

### Implementation for User Story 2

- [X] T012 [US2] Enforce strict `Delivered` status check in `BuyerConfirmSubOrderReceiptAsync` in `Commerce.Application/Services/Order/OrderService.cs` throwing `CommerceDomainException("INVALID_STATUS", "Sub-order cannot be confirmed until it is delivered.")` if not `Delivered`
- [X] T013 [US2] Enforce strict check in `CompleteOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` verifying all active sub-orders are `Delivered` before completing master order
- [X] T014 [US2] Enforce COD settlement bypass invariant in `BuyerConfirmSubOrderReceiptAsync` and `CompleteOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` to skip `_walletClient.SettleSellerSubOrderAsync` when `PaymentMethod == PaymentMethod.Cod`

**Checkpoint**: User Story 2 functional and independently verifiable.

---

## Phase 5: User Story 3 - Independent Sub-Order Cancellation by Buyer (Priority: P3)

**Goal**: Allow buyers to cancel an individual sub-order belonging to a specific shop prior to dispatch, releasing that shop's reserved stock, cancelling carrier shipment, and issuing a 100% wallet refund without disrupting sibling sub-orders.

**Independent Test**: Create an order with SubOrder A (`Shipped`) and SubOrder B (`Paid`). Cancel SubOrder B via `POST /api/v1/sub-orders/{subOrderIdB}/cancel`. Verify SubOrder B is cancelled, refunded, and stock released, while SubOrder A continues delivery uninterrupted.

### Tests for User Story 3

- [X] T015 [P] [US3] Add unit tests in `tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs` for `CancelSubOrderAsync` testing ownership validation, pre-shipment status guards, stock release, wallet refund, and sibling isolation

### Implementation for User Story 3

- [X] T016 [US3] Implement `CancelSubOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` validating buyer ownership, restricting cancellation to `PendingPayment`/`Paid`/`Confirmed` status, releasing stock via `ReleaseSubOrderStockAsync`, issuing refund via `RefundSubOrderPaymentAsync`, calling `CancelShipmentAsync` if tracking exists, and syncing parent order status
- [X] T017 [US3] Add controller endpoint `POST /api/v1/sub-orders/{subOrderId:guid}/cancel` in `Commerce.Api/Controllers/Public/OrdersController.cs` decorated with `RequireRole("buyer", "seller", "admin", "staff")` and ownership check

**Checkpoint**: User Story 3 functional and independently verifiable.

---

## Phase 6: User Story 4 - Automated Completion of Delivered Orders (Priority: P4)

**Goal**: Automatically complete delivered sub-orders past the inspection window, releasing seller escrow funds and awarding reputation without requiring manual buyer confirmation.

**Independent Test**: Seed a SubOrder with status `Delivered` and `DeliveredAt` older than configured window. Run worker execution cycle. Verify SubOrder transitions to `Completed` with escrow funds settled.

### Tests for User Story 4

- [X] T018 [P] [US4] Add unit tests in `tests/Commerce.UnitTests/Application/PaymentTimeoutWorkerTests.cs` verifying background worker auto-completes delivered sub-orders and triggers seller settlement

### Implementation for User Story 4

- [X] T019 [US4] Extend `PaymentTimeoutWorker.ProcessExpiredOrdersAsync` in `Commerce.Api/Workers/PaymentTimeoutWorker.cs` to scan for `Delivered` sub-orders where `DeliveredAt <= UtcNow - AutoCompleteDeliveredMinutes`, transition to `Completed`, invoke `SettleSellerSubOrderAsync`, and sync parent master order

**Checkpoint**: User Story 4 functional and independently verifiable.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Regression testing, documentation alignment, and build verification.

- [X] T020 [P] Update API documentation in `docs/` reflecting the new `CancelSubOrder` endpoint and updated delivery webhook schema
- [X] T021 Execute clean build verification `dotnet build Commerce.sln -c Release`
- [X] T022 Execute full test suite regression `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release`
- [X] T023 Perform end-to-end quickstart validation per `specs/025-fulfillment-delivery-and-suborder-lifecycle/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Independent, starts immediately.
- **Foundational (Phase 2)**: Depends on Setup (Phase 1). Blocks all User Stories.
- **User Story 1 (Phase 3)**: Depends on Foundational (Phase 2).
- **User Story 2 (Phase 4)**: Depends on Foundational (Phase 2) and User Story 1 (Phase 3).
- **User Story 3 (Phase 5)**: Depends on Foundational (Phase 2).
- **User Story 4 (Phase 6)**: Depends on User Story 2 (Phase 4).
- **Polish (Phase 7)**: Depends on all User Stories being complete.

### Parallel Opportunities

- `T003`, `T004`, `T005` in Phase 2 can run in parallel (different files).
- `T007` (US1 test) can run in parallel before US1 implementation.
- `T011` (US2 test) can run in parallel before US2 implementation.
- `T015` (US3 test) can run in parallel before US3 implementation.
- `T018` (US4 test) can run in parallel before US4 implementation.
- User Story 3 (Cancellation) and User Story 1 (Delivery Webhook) can be developed concurrently once Foundational (Phase 2) completes.

---

## Implementation Strategy

### MVP Scope (User Story 1)
1. Complete Phase 1 (Setup) and Phase 2 (Foundational).
2. Complete Phase 3 (User Story 1): Deliver full compatibility with Fulfillment Service's GHN webhook callback, including POD and courier metadata.
3. Validate independently using `tests/Commerce.UnitTests/Controllers/OrdersControllerDeliveryCallbackTests.cs`.

### Incremental Delivery
1. **Increment 1 (MVP)**: Delivery Webhook Synchronization (US1).
2. **Increment 2**: Guarded Receipt Confirmation & Escrow Protection (US2).
3. **Increment 3**: Independent Sub-Order Cancellation for Buyers (US3).
4. **Increment 4**: Auto-Completion Worker for Delivered Orders (US4).
5. **Increment 5**: Full Solution Polish, Verification, and Regression Testing.
