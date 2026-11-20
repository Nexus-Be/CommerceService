# Tasks: Wallet Escrow Contract Alignment & Advanced Financial Error Handling

**Input**: Design documents from `/specs/026-wallet-escrow-contract-alignment/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/  
**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.  

---

## Format: `- [ ] [ID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., [US1], [US2])
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verification of baseline contracts and environment parity

- [X] T001 Verify active feature context and environment parity in specs/026-wallet-escrow-contract-alignment/plan.md
- [X] T002 [P] Inspect and verify IWalletClient signatures and models in Commerce.Application/Interfaces/IWalletClient.cs and Commerce.Application/Interfaces/WalletPaymentResult.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core error parsing and schema infrastructure that MUST be complete before user stories can execute

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 Upgrade ProblemDetails error parser to extract specific financial collision codes (`ORDER_ESCROW_INSUFFICIENT`, `REFUND_EXCEEDS_PAYMENT`, `SETTLEMENT_CONFLICT`, `FEATURE_DISABLED`) in Commerce.Infrastructure/Services/Wallet/WalletClient.cs
- [X] T004 [P] Ensure OrderRefund entity mapping supports nullable SubOrderId when OrderId is set for full master order refunds in Commerce.Infrastructure/Persistence/Configurations/OrderRefundConfiguration.cs

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Master Order Cancellation Full Escrow Drawdown (Priority: P1) 🎯 MVP

**Goal**: When a buyer cancels their entire master order before shipment, CommerceService calls `POST /api/v1/wallets/orders/{orderId}/refund` once with `order.TotalAmount`, drawing down directly from the parent `OrderEscrow` rather than iterating through child sub-orders.

**Independent Test**: Can be verified by placing a multi-shop order, cancelling it as the buyer, and asserting that `RefundOrderPaymentAsync` is invoked once with the total order amount, and a single `OrderRefund` record is persisted with `OrderId` set and `Status == Completed`.

### Tests for User Story 1
- [X] T005 [P] [US1] Write unit tests in tests/Commerce.UnitTests/Application/OrderServiceTests.cs verifying CancelOrderAsync calls RefundOrderPaymentAsync once with order.TotalAmount when buyer cancels master order

### Implementation for User Story 1
- [X] T006 [US1] Refactor CancelOrderAsync in Commerce.Application/Services/Order/OrderService.cs to call _walletClient.RefundOrderPaymentAsync once for the entire master order when isBuyer is true, instead of looping through sub-orders
- [X] T007 [US1] Record single comprehensive OrderRefund entity for master order cancellation with RefundType.Full and OrderId populated in Commerce.Application/Services/Order/OrderService.cs
- [X] T008 [US1] Handle WalletClient 503 FEATURE_DISABLED and 409 collisions gracefully, recording OrderRefund.Status as Requested for reconciliation in Commerce.Application/Services/Order/OrderService.cs

**Checkpoint**: User Story 1 is functional and testable independently.

---

## Phase 4: User Story 2 - Sub-Order Escrow Payout & Conflict Governance (Priority: P2)

**Goal**: Settle seller payouts from `OrderEscrow` cleanly on delivery confirmation, accurately handle `409 SETTLEMENT_CONFLICT` replays and `409 ORDER_ESCROW_INSUFFICIENT` without service crashes.

**Independent Test**: Mock WalletService returning `409 SETTLEMENT_CONFLICT` or `503 FEATURE_DISABLED` during `SettleSellerSubOrderAsync`, and verify that `WalletClient` returns `false`, logs an actionable audit message, and does not crash the calling workflow.

### Tests for User Story 2
- [X] T009 [P] [US2] Write unit tests in tests/Commerce.UnitTests/Infrastructure/WalletClientTests.cs verifying SettleSellerSubOrderAsync and RefundSubOrderPaymentAsync handle 409 SETTLEMENT_CONFLICT and 503 FEATURE_DISABLED correctly

### Implementation for User Story 2
- [X] T010 [US2] Enhance SettleSellerSubOrderAsync in Commerce.Infrastructure/Services/Wallet/WalletClient.cs to log explicit warnings on 409 ORDER_ESCROW_INSUFFICIENT and 503 FEATURE_DISABLED
- [X] T011 [US2] Enhance BuyerConfirmSubOrderReceiptAsync and CompleteOrderAsync in Commerce.Application/Services/Order/OrderService.cs to catch escrow settlement errors and flag for manual reconciliation without breaking completion

**Checkpoint**: User Story 2 is functional and testable independently.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Guardrails verification, build cleanliness, and end-to-end regression testing

- [X] T012 [P] Verify COD safety check across all cancellation paths ensuring PaymentMethod.Cod never invokes _walletClient in Commerce.Application/Services/Order/OrderService.cs
- [X] T013 [P] Update API documentation and integration notes in docs/COMMERCE_API_CONTRACT.md
- [X] T014 Run full solution build validation via dotnet build Commerce.sln -c Release
- [X] T015 Run full automated test suite via dotnet test Commerce.sln -c Release
- [X] T016 Execute validation scenarios defined in specs/026-wallet-escrow-contract-alignment/quickstart.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — executes immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational completion.
- **User Story 2 (Phase 4)**: Depends on Foundational completion.
- **Polish (Phase 5)**: Depends on all user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: Independent after Phase 2.
- **User Story 2 (P2)**: Independent after Phase 2.

### Parallel Opportunities

- `T002` [P] can run in parallel with `T001`.
- `T004` [P] can run in parallel with `T003`.
- `T005` [P] [US1] and `T009` [P] [US2] can be written in parallel.
- `T012` [P] and `T013` [P] can run in parallel during Polish.

---

## Parallel Example: User Story 1

```powershell
# Launch tests for User Story 1:
Task: "Write unit tests in tests/Commerce.UnitTests/Application/OrderServiceTests.cs verifying CancelOrderAsync calls RefundOrderPaymentAsync once"

# Concurrently verify entity mapping:
Task: "Ensure OrderRefund entity mapping supports nullable SubOrderId when OrderId is set in Commerce.Infrastructure/Persistence/Configurations/OrderRefundConfiguration.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)
1. Complete Phase 1: Setup (`T001`, `T002`).
2. Complete Phase 2: Foundational (`T003`, `T004`).
3. Complete Phase 3: User Story 1 (`T005`, `T006`, `T007`, `T008`).
4. **STOP and VALIDATE**: Verify master order cancellation refunds 100% of funds from `OrderEscrow` in a single call.

### Incremental Delivery
1. Add User Story 2 (`T009`, `T010`, `T011`) for settlement resilience and conflict handling.
2. Complete Polish (`T012` to `T016`) ensuring 0 compiler warnings, 0 failed tests, and clean Git hygiene.
