---
description: "Task list for User Service Alignment — Buyer Permission Gate, Seller Reputation Rewards & Violation Penalties"
---

# Tasks: User Service Alignment

**Input**: Design documents from `/specs/016-user-service-alignment/`  
**Prerequisites**: `spec.md` (required), `plan.md` (required), `research.md`, `data-model.md`, `contracts/`, `quickstart.md`  
**Tests**: Automated Unit Tests are required per Constitution Principle V and Quality Gate DoD.  
**Organization**: Tasks are grouped by phase and user story to enable independent implementation and testing.

## Format: `[TaskID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (different files, no blocking dependencies)
- **[Story]**: User story identifier ([US1], [US2], [US3])
- Exact file paths are specified for every task.

---

## Phase 1: Setup & Environment Configuration

**Purpose**: Configure inter-service URL for UserService across configuration files.

- [X] T001 [P] Add `USER_SERVICE_URL: "${USER_SERVICE_URL:-http://user-service:8001}"` to `docker-compose.yml`
- [X] T002 [P] Add `"USER_SERVICE_URL": "http://user-service:8001"` to `Commerce.Api/appsettings.json` and `"http://localhost:8001"` to `Commerce.Api/appsettings.Development.json`

---

## Phase 2: Foundational (Domain Enums & Application Contracts)

**Purpose**: Core types, DTOs, and typed client infrastructure required across all user stories.

**⚠️ CRITICAL**: Must be completed before user story implementation begins.

- [X] T003 [P] Add `Completed = 5` to `OrderStatus` enum in `Commerce.Domain/Enums/OrderStatus.cs`
- [X] T004 [P] Add `CancellationWithoutReason = 7` to `CancellationReasonCode` enum in `Commerce.Domain/Enums/CancellationReasonCode.cs`
- [X] T005 [P] Create `UserClientModels.cs` in `Commerce.Application/Interfaces/UserClientModels.cs` declaring `UserPermissionCheckResult`, `ReputationAdjustRequest`, `ReputationAdjustResponse`, `UserPenaltyRequest`, and `UserPenaltyResponse`
- [X] T006 Create `IUserClient.cs` in `Commerce.Application/Interfaces/IUserClient.cs` declaring `CheckBuyerPermissionAsync`, `AdjustReputationAsync`, and `CreatePenaltyAsync`
- [X] T007 Implement `UserClient.cs` in `Commerce.Infrastructure/Services/User/UserClient.cs` implementing `IUserClient` with timeout (10s), header `X-Internal-Service-Key`, and fail-closed logging
- [X] T008 Register `IUserClient` and `UserClient` in `Commerce.Infrastructure/DependencyInjection.cs` with `USER_SERVICE_URL` and `IdentityPropagationHandler`

**Checkpoint**: Foundation ready - all core interfaces and client registration compile cleanly.

---

## Phase 3: User Story 1 - Buyer Purchasing Permission Enforcement (Priority: P1) 🎯 MVP

**Goal**: Block restricted or banned buyers from placing orders at `POST /api/v1/orders`.

**Independent Test**: Call `CreateOrderAsync` with a buyer having active restrictions; verify it fails immediately with HTTP 403 `BUYER_ACTION_RESTRICTED` without reserving stock or debiting funds.

### Tests for User Story 1
- [X] T009 [P] [US1] Create unit tests for `UserClient.CheckBuyerPermissionAsync` (handling 200 OK, 403 Forbidden, and 503 unavailable) in `tests/Commerce.UnitTests/Infrastructure/UserClientTests.cs`

### Implementation for User Story 1
- [X] T010 [US1] Inject `IUserClient` into `OrderService` constructor in `Commerce.Application/Services/Order/OrderService.cs`
- [X] T011 [US1] Update `OrderService.CreateOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` to call `_userClient.CheckBuyerPermissionAsync(buyerId, "buy", ct)` before stock reservation and wallet debit; throw 403 `CommerceDomainException("BUYER_ACTION_RESTRICTED", "User has an active restriction prohibiting purchase actions.", 403)` on failure
- [X] T012 [P] [US1] Add unit test `CreateOrder_ShouldThrowBuyerActionRestricted_WhenUserIsRestricted` in `tests/Commerce.UnitTests/Application/OrderCheckoutTests.cs`

**Checkpoint**: User Story 1 complete and independently verifiable. Restricted buyers are blocked.

---

## Phase 4: User Story 2 - Order Completion & Partner Reputation Adjustment (Priority: P1)

**Goal**: Complete orders and reward sellers with +1 reputation score via `POST /api/v1/internal/users/reputation-adjust`.

**Independent Test**: Call `POST /api/v1/orders/{orderId}/complete` and `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt`; verify order status transitions to `Completed` and `AdjustReputationAsync` is invoked with `scoreDelta: 1`.

### Implementation for User Story 2
- [X] T013 [P] [US2] Add `OrderCompleteResponse` record in `Commerce.Application/Services/Order/OrderResponses.cs`
- [X] T014 [US2] Add `Task<OrderCompleteResponse> CompleteOrderAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default)` to `IOrderService` in `Commerce.Application/Services/Order/IOrderService.cs`
- [X] T015 [US2] Implement `CompleteOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs`:
  - Verify buyer ownership (`order.BuyerId == currentUserId`)
  - Ensure all sub-orders are delivered or completed
  - Set `order.Status = OrderStatus.Completed` and complete any remaining sub-orders
  - Invoke `_userClient.AdjustReputationAsync` for each seller (`scoreDelta: 1`, `reason: "OrderCompleted"`, `referenceType: "Order"`, `referenceId: orderNo`)
- [X] T016 [US2] Update `BuyerConfirmSubOrderReceiptAsync` in `Commerce.Application/Services/Order/OrderService.cs` to invoke `_userClient.AdjustReputationAsync` for the sub-order seller and auto-set `order.Status = OrderStatus.Completed` when all sibling sub-orders are completed
- [X] T017 [US2] Add `[HttpPost("{orderId:guid}/complete")]` endpoint in `Commerce.Api/Controllers/Public/OrdersController.cs` calling `_orderService.CompleteOrderAsync`
- [X] T018 [P] [US2] Add unit tests `CompleteOrder_ShouldRewardSellerReputation_WhenOrderDelivered` and `ConfirmReceipt_ShouldRewardSellerReputation` in `tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs`

**Checkpoint**: User Story 2 complete. Master orders can be completed and sellers earn reputation rewards.

---

## Phase 5: User Story 3 - Seller Violation Cancellation Penalties (Priority: P1)

**Goal**: Deduct 5 reputation points from sellers who cancel confirmed/paid orders without force majeure via `POST /api/v1/internal/users/penalties`.

**Independent Test**: Cancel a confirmed sub-order with reason `CancellationWithoutReason`; verify status becomes `Cancelled`, funds refunded, and `CreatePenaltyAsync` called with deduction 5.

### Implementation for User Story 3
- [X] T019 [US3] Update `CancelOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` to:
  - Permit seller actors for sub-orders they own within the order
  - Detect cancellation of confirmed/paid sub-orders with reason `CancellationWithoutReason = 7` or `SellerRejected = 3`
  - Invoke `_userClient.CreatePenaltyAsync` with `customScoreDeduction: 5`, `policyClauseCode: "POLICY-SEC-3.2"`, `violationType: "CancellationWithoutReason"`
- [X] T020 [US3] Update `SellerRejectSubOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` to invoke `_userClient.CreatePenaltyAsync` when shop rejects a confirmed/paid sub-order without force majeure
- [X] T021 [US3] Update `OrdersController.CancelOrder` in `Commerce.Api/Controllers/Public/OrdersController.cs` to support seller cancellation requests
- [X] T022 [P] [US3] Add unit test `CancelOrder_ShouldPenalizeSeller_WhenCancelledWithoutReason` in `tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs`

**Checkpoint**: User Story 3 complete. Seller cancellation violations trigger automatic penalties.

---

## Phase 6: Polish, Documentation & Verification

**Purpose**: Documentation updates and quality gate verification.

- [X] T023 [P] Create service alignment document `docs/USER_SERVICE_ALIGNMENT_CONTRACT.md` detailing the 3 integration contracts
- [X] T024 [P] Update `specs/02_API_REST_CONTRACTS.md` with `POST /api/v1/orders/{orderId}/complete` and outbound `IUserClient` specifications
- [X] T025 Execute clean compilation `dotnet build Commerce.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false` (must succeed with 0 warnings, 0 errors)
- [X] T026 Run automated test suite `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore --no-build` and confirm 100% pass rate

---

## Dependencies & Execution Order

### Phase Dependencies
- **Phase 1 (Setup)**: No dependencies - can start immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1 - BLOCKS all User Stories.
- **Phase 3 (User Story 1)**: Depends on Phase 2 - Delivers MVP (Buyer Permission Gate).
- **Phase 4 (User Story 2)**: Depends on Phase 2 - Delivers Order Completion & Rewards.
- **Phase 5 (User Story 3)**: Depends on Phase 2 - Delivers Violation Penalty Enforcement.
- **Phase 6 (Polish & Verification)**: Depends on Phases 3, 4, and 5 completion.

### Parallel Opportunities
- T001 and T002 in Phase 1 can run in parallel.
- T003, T004, and T005 in Phase 2 can run in parallel.
- Once Phase 2 completes, Phase 3, Phase 4, and Phase 5 can be worked on in parallel across different files.
- Documentation updates T023 and T024 can run in parallel with test verification.
