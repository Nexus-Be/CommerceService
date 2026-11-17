# Tasks: Comprehensive Order Cancellation & Resource Rollback Lifecycle

**Input**: Design documents from `/specs/027-order-cancellation-and-resource-lifecycle/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/  
**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.  

---

## Format: `- [ ] [ID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., [US1], [US2], [US3])
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verification of baseline models, enums, and project structure

- [X] T001 Verify active feature context and environment parity in specs/027-order-cancellation-and-resource-lifecycle/plan.md
- [X] T002 [P] Inspect CancellationReasonCode and OrderActorRole enums in Commerce.Domain/Enums/CancellationReasonCode.cs and Commerce.Domain/Enums/OrderActorRole.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core entity configurations and exception error codes required by all cancellation stories

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 Verify OrderCancellation entity and configuration in Commerce.Domain/Entities/OrderCancellation.cs and Commerce.Infrastructure/Persistence/Configurations/OrderCancellationConfiguration.cs
- [X] T004 [P] Verify error code ORDER_CANNOT_BE_CANCELLED and sentence-case error message in Commerce.Domain/Exceptions/CommerceDomainException.cs

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Pre-Shipping Cancellation Boundary & Post-Delivery Block (Priority: P1) 🎯 MVP

**Goal**: Ensure cancellation is strictly allowed only before shipping (`PendingPayment`, `Paid`, `Confirmed`). Any attempt to cancel an order or sub-order once in `Shipping`, `Delivered`, or `Completed` is rejected with HTTP 400 `ORDER_CANNOT_BE_CANCELLED`. All sales are final post-delivery with zero automated buyer refunds.

**Independent Test**: Seed sub-orders in `Shipping` and `Delivered` states, attempt cancellation via `CancelOrderAsync` and `CancelSubOrderAsync`, and verify that HTTP 400 `ORDER_CANNOT_BE_CANCELLED` is thrown and status remains unchanged.

### Tests for User Story 1
- [X] T005 [P] [US1] Write unit tests in tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs asserting CancelOrderAsync and CancelSubOrderAsync reject cancellation with HTTP 400 ORDER_CANNOT_BE_CANCELLED when sub-order is in Shipping, Delivered, or Completed

### Implementation for User Story 1
- [X] T006 [US1] Enforce boundary check in CancelOrderAsync in Commerce.Application/Services/Order/OrderService.cs rejecting cancellation if any target sub-order is Shipping, Delivered, or Completed
- [X] T007 [US1] Enforce boundary check in CancelSubOrderAsync in Commerce.Application/Services/Order/OrderService.cs rejecting cancellation if sub-order is Shipping, Delivered, or Completed
- [X] T008 [US1] Enforce that completed or delivered orders cannot trigger automated buyer refunds in Commerce.Application/Services/Order/OrderService.cs

**Checkpoint**: User Story 1 is functional and testable independently.

---

## Phase 4: User Story 2 - Batch Payment Timeout Worker Stock Release & Zero Wallet Interaction (Priority: P2)

**Goal**: In `PaymentTimeoutWorker`, sweep expired `PendingPayment` orders in batches of up to 50, dispatch `_fulfillmentClient.BatchReleaseStockAsync(orderIds)` in a single call, batch release vouchers (`Reserved` -> `Released`), and guarantee zero calls to WalletService.

**Independent Test**: Seed multiple expired `PendingPayment` orders, trigger worker iteration, verify `BatchReleaseStockAsync` is invoked with all order IDs, vouchers are released, and `_mockWalletClient` receives zero calls.

### Tests for User Story 2
- [X] T009 [P] [US2] Write unit tests in tests/Commerce.UnitTests/Application/PaymentTimeoutWorkerTests.cs verifying BatchReleaseStockAsync is called once with array of expired orderIds and zero calls made to _walletClient

### Implementation for User Story 2
- [X] T010 [US2] Verify and optimize batch cancellation in Commerce.Api/Workers/PaymentTimeoutWorker.cs ensuring BatchReleaseStockAsync passes up to 50 orderIds, vouchers are released, and WalletService is never invoked

**Checkpoint**: User Story 2 is functional and testable independently.

---

## Phase 5: User Story 3 - Package Return (`Returned`) Resource Rollback & COD Safety (Priority: P3)

**Goal**: When delivery status updates to `Returned`, update sub-order to `Cancelled`, release warehouse stock back to inventory, trigger wallet refund ONLY if `PaymentMethod == PaymentMethod.Wallet && WasPaid`, and strictly skip wallet refund if `PaymentMethod == PaymentMethod.Cod`.

**Independent Test**: Dispatch `UpdateSubOrderDeliveryStatusAsync` with status `Returned` for a COD order and a Wallet order; assert Wallet order receives refund and COD order releases stock with zero wallet calls.

### Tests for User Story 3
- [X] T011 [P] [US3] Write unit tests in tests/Commerce.UnitTests/Application/OrderSettlementRefundTests.cs verifying UpdateSubOrderDeliveryStatusAsync for Returned status releases inventory, refunds wallet if paid by Wallet, and strictly skips wallet if COD

### Implementation for User Story 3
- [X] T012 [US3] Ensure UpdateSubOrderDeliveryStatusAsync in Commerce.Application/Services/Order/OrderService.cs initiates wallet refund ONLY when wasPaid && PaymentMethod == PaymentMethod.Wallet, and skips wallet completely for COD

**Checkpoint**: User Story 3 is functional and testable independently.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Guardrails verification, build cleanliness, and end-to-end regression testing

- [X] T013 [P] Verify Seller penalty (-5 points) is applied on unjustified cancellation of confirmed/paid orders in Commerce.Application/Services/Order/OrderService.cs
- [X] T014 [P] Update API documentation and integration notes in docs/COMMERCE_API_CONTRACT.md
- [X] T015 Run full solution build validation via dotnet build Commerce.sln -c Release
- [X] T016 Run full automated test suite via dotnet test Commerce.sln -c Release
- [X] T017 Execute validation scenarios defined in specs/027-order-cancellation-and-resource-lifecycle/quickstart.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — executes immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational completion.
- **User Story 2 (Phase 4)**: Depends on Foundational completion.
- **User Story 3 (Phase 5)**: Depends on Foundational completion.
- **Polish (Phase 6)**: Depends on all user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: Independent after Phase 2.
- **User Story 2 (P2)**: Independent after Phase 2.
- **User Story 3 (P3)**: Independent after Phase 2.

### Parallel Opportunities

- `T002` [P] can run in parallel with `T001`.
- `T004` [P] can run in parallel with `T003`.
- `T005` [P] [US1], `T009` [P] [US2], and `T011` [P] [US3] can be written in parallel.
- `T013` [P] and `T014` [P] can run in parallel during Polish.

---

## Implementation Strategy

### MVP First (User Story 1 Only)
1. Complete Phase 1: Setup (`T001`, `T002`).
2. Complete Phase 2: Foundational (`T003`, `T004`).
3. Complete Phase 3: User Story 1 (`T005`, `T006`, `T007`, `T008`).
4. **STOP and VALIDATE**: Test pre-shipping cancellation boundaries and verify that shipped/delivered orders reject cancellation.

### Incremental Delivery
1. Add User Story 2 (`T009`, `T010`) for batch expiration without wallet calls.
2. Add User Story 3 (`T011`, `T012`) for package return and COD safety.
3. Complete Polish (`T013` to `T017`) ensuring 0 compiler warnings, 0 failed tests, and clean Git hygiene.
