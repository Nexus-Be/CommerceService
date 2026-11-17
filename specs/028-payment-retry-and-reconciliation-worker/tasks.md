# Tasks: Payment Self-Healing, Order Repay & Financial Reconciliation Engine

**Input**: Design documents from `/specs/028-payment-retry-and-reconciliation-worker/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/  
**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.  

---

## Format: `- [ ] [ID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., [US1], [US2], [US3])
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Define response DTO contracts and initialize project dependencies

- [X] T001 Verify active feature context and environment parity in specs/028-payment-retry-and-reconciliation-worker/plan.md
- [X] T002 [P] Create DTO records RepayOrderResponse, OrderRefundResponse, and SubOrderFinancialBreakdownResponse in Commerce.Application/DTOs/Order/RepayOrderResponse.cs, Commerce.Application/DTOs/Order/OrderRefundResponse.cs, and Commerce.Application/DTOs/Order/SubOrderFinancialBreakdownResponse.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core entity changes and escrow settlement tracking required by all subsequent stories

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 Add property public bool IsEscrowSettled { get; set; } = false to SubOrder entity in Commerce.Domain/Entities/SubOrder.cs
- [X] T004 [P] Configure builder.Property(x => x.IsEscrowSettled).HasDefaultValue(false) in Commerce.Infrastructure/Persistence/Configurations/SubOrderConfiguration.cs
- [X] T005 Update ConfirmSubOrderReceiptAsync and auto-complete logic in Commerce.Application/Services/Order/OrderService.cs to set so.IsEscrowSettled = true upon successful escrow settlement or when PaymentMethod == PaymentMethod.Cod

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Repay Pending Order API (Priority: P1) 🎯 MVP

**Goal**: Provide `POST /api/v1/orders/{orderId}/pay` allowing buyers to retry wallet payment for orders in `PendingPayment` status within the 15-minute deadline.

**Independent Test**: Seed an order in `PendingPayment` status, call `POST /api/v1/orders/{orderId}/pay`, assert status transitions to `Paid`, stock is committed, and child sub-orders transition to `Paid`. Also verify 400 rejection when deadline expired or balance insufficient.

### Tests for User Story 1
- [X] T006 [P] [US1] Write unit tests in tests/Commerce.UnitTests/Application/OrderRepayTests.cs testing RepayOrderAsync for success, deadline expiration (HTTP 400 ORDER_PAYMENT_DEADLINE_EXPIRED), insufficient balance (HTTP 400 INSUFFICIENT_WALLET_BALANCE), ownership check (HTTP 403 FORBIDDEN), and COD order rejection (HTTP 400 INVALID_PAYMENT_METHOD)

### Implementation for User Story 1
- [X] T007 [US1] Implement RepayOrderAsync in Commerce.Application/Services/Order/IOrderService.cs and Commerce.Application/Services/Order/OrderService.cs verifying deadline, deducting wallet balance, updating status to Paid, and committing inventory via _fulfillmentClient.CommitStockAsync
- [X] T008 [US1] Add POST /api/v1/orders/{orderId}/pay endpoint on Commerce.Api/Controllers/Public/OrdersController.cs with TryGetUserId ownership validation and RFC 9110 Problem Details mapping

**Checkpoint**: User Story 1 is functional and testable independently.

---

## Phase 4: User Story 2 - Automated Reconciliation & Self-Healing Worker (Priority: P2)

**Goal**: Extend `PaymentTimeoutWorker` to sweep and automatically retry stalled `Requested` refunds and unsettled seller escrow payouts.

**Independent Test**: Seed `OrderRefund` with `Status == RefundStatus.Requested` and `SubOrder` with `Status == SubOrderStatus.Completed && !IsEscrowSettled`, trigger worker sweep, and verify refunds transition to `Completed` and `IsEscrowSettled` updates to `true`.

### Tests for User Story 2
- [X] T009 [P] [US2] Write unit tests in tests/Commerce.UnitTests/Application/OrderReconciliationTests.cs asserting ReconcilePendingRefundsAsync and ReconcileUnsettledEscrowsAsync retry wallet calls with idempotent keys and update database records to Completed and IsEscrowSettled = true

### Implementation for User Story 2
- [X] T010 [US2] Implement ReconcilePendingRefundsAsync in Commerce.Api/Workers/PaymentTimeoutWorker.cs sweeping up to 50 refunds with Status == RefundStatus.Requested and retrying _walletClient.RefundSubOrderPaymentAsync or RefundOrderPaymentAsync
- [X] T011 [US2] Implement ReconcileUnsettledEscrowsAsync in Commerce.Api/Workers/PaymentTimeoutWorker.cs sweeping up to 50 sub-orders with Status == SubOrderStatus.Completed && !IsEscrowSettled && PaymentMethod == PaymentMethod.Wallet and retrying _walletClient.SettleSellerSubOrderAsync

**Checkpoint**: User Story 2 is functional and testable independently.

---

## Phase 5: User Story 3 - Financial Transparency & Statement APIs (Priority: P3)

**Goal**: Expose transparent financial query endpoints for buyers to view refund histories and sellers to inspect sub-order fee breakdowns.

**Independent Test**: Query `GET /api/v1/orders/{orderId}/refunds` as buyer and assert refund history items; query `GET /api/v1/sub-orders/{subOrderId}/financial-breakdown` as seller and verify platform fee, shipping fee, system discount, and net payout match accounting invariant.

### Tests for User Story 3
- [X] T012 [P] [US3] Write unit tests in tests/Commerce.UnitTests/Application/OrderFinancialQueryTests.cs testing GetOrderRefundsAsync and GetSubOrderFinancialBreakdownAsync for ownership checks (HTTP 403), not found cases (HTTP 404), and exact calculation invariant assertions

### Implementation for User Story 3
- [X] T013 [US3] Implement GetOrderRefundsAsync and GetSubOrderFinancialBreakdownAsync in Commerce.Application/Services/Order/IOrderService.cs and Commerce.Application/Services/Order/OrderService.cs
- [X] T014 [US3] Add GET /api/v1/orders/{orderId}/refunds and GET /api/v1/sub-orders/{subOrderId}/financial-breakdown endpoints on Commerce.Api/Controllers/Public/OrdersController.cs

**Checkpoint**: User Story 3 is functional and testable independently.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation parity, build cleanliness, and end-to-end regression testing

- [X] T015 [P] Update API documentation and error matrix in docs/COMMERCE_API_CONTRACT.md for /api/v1/orders/{orderId}/pay, /refunds, and /financial-breakdown
- [X] T016 Run full solution build validation via dotnet build Commerce.sln -c Release
- [X] T017 Run full automated test suite via dotnet test Commerce.sln -c Release
- [X] T018 Execute validation scenarios defined in specs/028-payment-retry-and-reconciliation-worker/quickstart.md

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
- `T006` [P] [US1], `T009` [P] [US2], and `T012` [P] [US3] test authoring can proceed in parallel.
- `T015` [P] can run in parallel during Polish.

---

## Implementation Strategy

### MVP First (User Story 1 Only)
1. Complete Phase 1: Setup (`T001`, `T002`).
2. Complete Phase 2: Foundational (`T003`, `T004`, `T005`).
3. Complete Phase 3: User Story 1 (`T006`, `T007`, `T008`).
4. **STOP and VALIDATE**: Verify that repaying a pending order within 15 minutes updates status to `Paid` and commits stock.

### Incremental Delivery
1. Add User Story 2 (`T009`, `T010`, `T011`) for background self-healing sweeps.
2. Add User Story 3 (`T012`, `T013`, `T014`) for financial transparency statements.
3. Complete Polish (`T015` to `T018`) ensuring 0 compiler warnings, 0 failed tests, and clean Git hygiene.
