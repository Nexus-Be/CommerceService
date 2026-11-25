# Tasks: QA E2E Remediation, Buy Now Contract Parity, Stock Resilience & Lifecycle Hardening

**Input**: Design documents from `specs/036-qa-e2e-remediation-and-lifecycle-hardening/`  
**Prerequisites**: [plan.md](plan.md) (required), [spec.md](spec.md) (required for user stories), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/)  

**Tests**: Unit tests in `tests/Commerce.UnitTests` are required to guarantee 100% test pass rate and verify bug remediation without regression.  

**Organization**: Tasks are grouped by foundational phase and user stories to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Exact file paths are specified in descriptions.

---

## Phase 1: Setup (Baseline Verification)

**Purpose**: Baseline verification of compilation and existing test suite before making changes.

- [X] T001 Baseline verification: Execute `dotnet build Commerce.sln -c Release` and `dotnet test` to confirm 0 compilation errors.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core Domain models, enums, and database migrations that all user stories depend on.

**⚠️ CRITICAL**: Must complete before any user story tasks can begin.

- [X] T002 [P] Update `Commerce.Domain/Entities/AuctionOrder.cs` to change `WinningBidId` from `required Guid` to nullable `Guid?`.
- [X] T003 [P] Update `Commerce.Domain/Enums/SubOrderStatus.cs` to add `Returning = 8`.
- [X] T004 Update migration `Commerce.Infrastructure/Persistence/Migrations/20261106065814_FixSubOrderJsonSeed.cs` to delete dependent records in `DiscountCodeUsages`, `OrderInvoices`, and `OrderStatusHistories` before deleting `SubOrders` row `02000000-0000-0000-0000-000000000001` (D-12).
- [X] T005 Create new EF Core migration `Commerce.Infrastructure/Persistence/Migrations/20261121000000_MakeAuctionOrderWinningBidIdNullable.cs` making column `WinningBidId` nullable in `AuctionOrders`.

**Checkpoint**: Foundation ready - user story implementation can now begin independently.

---

## Phase 3: User Story 1 - Buy Now Settlement Ingestion with Nullable WinningBidId (Priority: P1) 🎯 MVP

**Goal**: Ingest settlements where `source = "BUY_NOW"` and `winningBidId` is null/omitted without 400 Bad Request errors.

**Independent Test**: Call `POST /api/v1/auction-orders/settlement` with `source: "BUY_NOW"` and omitted `winningBidId`. Assert `AuctionOrder` record is created with `WinningBidId = null`, `RemainingAmount = 0`, and response returns HTTP 201 Created.

### Tests for User Story 1
- [X] T006 [P] [US1] Unit tests in `tests/Commerce.UnitTests/Application/AuctionOrderServiceTests.cs` verifying:
  - Buy Now settlement without `WinningBidId` succeeds and returns HTTP 201.
  - Winner or SecondChance settlement without `WinningBidId` rejects with HTTP 400 `VAL_INVALID_SETTLEMENT`.
  - Idempotent settlement request with `WinningBidId = null` returns HTTP 200 without conflict.

### Implementation for User Story 1
- [X] T007 [P] [US1] Update `Commerce.Application/Services/AuctionOrder/AuctionOrderRequests.cs`: Change `WinningBidId` in `AuctionSettlementRequest` from `[Required] Guid WinningBidId` to `Guid? WinningBidId = null`.
- [X] T008 [P] [US1] Update `Commerce.Application/Services/AuctionOrder/AuctionOrderResponses.cs`: Change `WinningBidId` to `Guid? WinningBidId` in `AuctionOrderDetailResponse`.
- [X] T009 [US1] Update `ReceiveSettlementAsync` in `Commerce.Application/Services/AuctionOrder/AuctionOrderService.cs`:
  - Validate `WinningBidId` is required ONLY if `source != AuctionOrderSource.BuyNow`.
  - Safe idempotency comparison: `identicalOrder.WinningBidId != request.WinningBidId` when both are null.
  - Map `request.WinningBidId` into `AuctionOrder` entity.

**Checkpoint**: User Story 1 fully functional and testable independently.

---

## Phase 4: User Story 2 - Fail-Closed Stock Reservation & Resilience Governance (Priority: P1)

**Goal**: Enforce fail-closed stock reservation by default in non-development environments to prevent overselling.

**Independent Test**: Simulate warehouse reservation failure with `StockPolicy:FailOpen = false`. Assert order placement is rejected with HTTP 400 `INSUFFICIENT_STOCK` (or 503 on connection failure) and error is logged at `LogError` level.

### Tests for User Story 2
- [X] T010 [P] [US2] Unit tests in `tests/Commerce.UnitTests/Infrastructure/FulfillmentClientTests.cs` verifying:
  - `_failOpen` defaults to `false` when environment is not Development.
  - Failed reservation returns `false` (fail-closed) and logs `LogError`.

### Implementation for User Story 2
- [X] T011 [US2] Update `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs`:
  - Initialize `_failOpen` to `false` by default; only `true` if `StockPolicy:FailOpen` is `"true"` or environment is `Development`.
  - Escalate stock reservation failure logging from `LogWarning` to `LogError`.

**Checkpoint**: User Story 2 fully functional and testable independently.

---

## Phase 5: User Story 3 - Delivery Status "Returning" & Auction Order Resilience (Priority: P1)

**Goal**: Accept `"Returning"` status webhook, log the transition, keep order active, and do NOT cancel auction orders on interim `DeliveryFailed`.

**Independent Test**: Send webhook `POST /api/v1/sub-orders/{id}/delivery-status` with `status: "Returning"`. Assert `OrderStatusHistory` logs `"Returning"`, and `AuctionOrder` is not cancelled on `DeliveryFailed`.

### Tests for User Story 3
- [X] T012 [P] [US3] Unit tests in `tests/Commerce.UnitTests/Application/OrderDeliveryStatusTests.cs` verifying:
  - SubOrder accepts `"Returning"` status and records history.
  - AuctionOrder remains active upon receiving `DeliveryFailed` (interim attempt).

### Implementation for User Story 3
- [X] T013 [US3] Update `UpdateSubOrderDeliveryStatusAsync` in `Commerce.Application/Services/Order/OrderService.cs`:
  - Add branch for `status == "Returning"`: update `so.Status = SubOrderStatus.Returning`, update `UpdatedAt`, add `OrderStatusHistory`.
- [X] T014 [US3] Update `UpdateAuctionOrderDeliveryStatusAsync` in `Commerce.Application/Services/Order/OrderService.cs`:
  - Remove `DeliveryFailed` from the immediate cancellation check; only cancel upon `"Returned"`.
  - Add branch for `status == "Returning"`.

**Checkpoint**: User Story 3 fully functional and testable independently.

---

## Phase 6: User Story 4 - Buyer Reputation Penalty on Delivery Failure Return ("Bom Hàng") (Priority: P1)

**Goal**: Deduct 5 reputation points from Buyer when a package is marked `"Returned"` due to delivery failure / refusal.

**Independent Test**: Send webhook `POST /api/v1/sub-orders/{id}/delivery-status` with `status: "Returned"`. Assert `_userClient.AdjustReputationAsync` is invoked with `TargetRole = "Buyer"` and `ScoreDelta = -5`.

### Tests for User Story 4
- [X] T015 [P] [US4] Unit tests in `tests/Commerce.UnitTests/Application/OrderDeliveryStatusTests.cs` verifying:
  - When sub-order is marked `"Returned"`, `IUserClient.AdjustReputationAsync` is called with `-5` points.
  - If `IUserClient` throws exception, cancellation and refund still complete successfully without rolling back.

### Implementation for User Story 4
- [X] T016 [US4] Update `Returned` branch in `OrderService.UpdateSubOrderDeliveryStatusAsync`:
  - Add call to `_userClient.AdjustReputationAsync` with `UserId = so.BuyerId`, `TargetRole = "Buyer"`, `PartnerUserId = so.SellerId`, `ScoreDelta = -5`, `Reason = "PackageReturnedDeliveryFailed"`.
  - Wrap call in try/catch block to ensure delivery return flow is never aborted.

**Checkpoint**: User Story 4 fully functional and testable independently.

---

## Phase 7: User Story 5 - Database Migration Seed Foreign Key Remediation (Priority: P1)

**Goal**: Ensure clean database migrations execute without FK constraint violation `23503`.

**Independent Test**: Run EF Core migration from a clean database schema and assert 0 errors.

### Implementation for User Story 5
- [X] T017 [US5] Verify migration `20261106065814_FixSubOrderJsonSeed.cs` has dependent seed deletes in place and snapshot is consistent.

**Checkpoint**: User Story 5 verified.

---

## Phase 8: User Story 6 - Internal S2S Dispute Resolution Refund Endpoint (Priority: P2)

**Goal**: Provide secure internal endpoint for UserService to trigger buyer refund upon dispute resolution.

**Independent Test**: Call `POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund` with `X-Internal-Service-Key`. Assert buyer wallet is credited, `SubOrder.Status` becomes `Refunded`, and `OrderRefund` record is completed.

### Tests for User Story 6
- [X] T018 [P] [US6] Unit tests in `tests/Commerce.UnitTests/Api/DisputeRefundEndpointTests.cs` verifying:
  - Endpoint rejects requests with missing or invalid `X-Internal-Service-Key`.
  - Valid internal request calls `_walletClient.RefundSubOrderPaymentAsync`, updates `SubOrder.Status = Refunded`, and returns HTTP 200.
  - Ineligible sub-order status rejects with HTTP 400 `INVALID_REFUND_STATUS`.

### Implementation for User Story 6
- [X] T019 [P] [US6] Add DTO records `DisputeRefundRequest` and `DisputeRefundResponse` in `Commerce.Application/Services/Order/OrderRequests.cs`.
- [X] T020 [US6] Add method `Task<DisputeRefundResponse> DisputeRefundSubOrderAsync(Guid subOrderId, DisputeRefundRequest request, CancellationToken ct)` to `IOrderService` and implement in `Commerce.Application/Services/Order/OrderService.cs`.
- [X] T021 [US6] Add endpoint `POST /api/v1/internal/sub-orders/{subOrderId:guid}/dispute-refund` in `Commerce.Api/Controllers/Public/OrdersController.cs` protected by `InternalServiceKeyGuard.Check(Request, configuration)` and `AllowAnonymous`.

**Checkpoint**: User Story 6 fully functional and testable independently.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final verification, contract synchronization, and test pass rate validation.

- [X] T022 [P] Update `docs/COMMERCE_API_CONTRACT.md` with the new internal dispute refund endpoint and updated settlement contract.
- [X] T023 Run full solution build: `dotnet build Commerce.sln -c Release` (0 errors, 0 warnings).
- [X] T024 Run full test suite: `dotnet test Commerce.sln -c Release` (100% pass rate).
- [X] T025 Run quickstart validation scenarios defined in [quickstart.md](quickstart.md).

---

## Dependencies & Execution Order

### Phase Dependencies
- **Setup (Phase 1)**: Can start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1; blocks all User Stories.
- **User Stories (Phase 3 through 8)**: Depend on Foundational phase completion. Can proceed sequentially in priority order (P1 $\rightarrow$ P2) or in parallel.
- **Polish (Phase 9)**: Depends on all user stories being complete.

### User Story Dependencies
- **US1 (Buy Now WinningBidId)**: Depends on Foundational Phase 2 (T002, T005).
- **US2 (Fail-Closed Stock)**: Independent of database; can execute immediately after Phase 2.
- **US3 (Returning Status)**: Depends on Foundational Phase 2 (T003 enum).
- **US4 (Buyer Bom Hàng Penalty)**: Follows US3 delivery status handling.
- **US5 (Migration FK Fix)**: Completed in Foundational Phase 2 (T004).
- **US6 (Internal Dispute Refund)**: Independent endpoint; can execute in parallel with US1-US4.

---

## Implementation Strategy

### MVP First (User Story 1 Only)
1. Complete Phase 1: Setup baseline.
2. Complete Phase 2: Foundational entities and migrations.
3. Complete Phase 3: User Story 1 (Buy Now settlement nullable WinningBidId).
4. **VALIDATE**: Run `dotnet test --filter "FullyQualifiedName~AuctionOrder"` to confirm Buy Now settlement works independently.

### Incremental Delivery
1. Add User Story 2 (Fail-Closed stock policy) $\rightarrow$ Test with `FulfillmentClientTests`.
2. Add User Story 3 & 4 (Delivery returning and buyer penalty) $\rightarrow$ Test with `OrderDeliveryStatusTests`.
3. Add User Story 6 (Internal dispute refund endpoint) $\rightarrow$ Test with `DisputeRefundEndpointTests`.
4. Run full suite and update documentation.
