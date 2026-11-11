# Tasks: Auction Order Lifecycle, Settlement & Automated Reversal

**Input**: Design documents from `/specs/019-auction-order-lifecycle-and-settlement/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/auction-order-api.yaml`, `quickstart.md`  

---

## Phase 1: Setup (Shared Infrastructure & Environment)

**Purpose**: Configuration initialization and feature structure preparation

- [X] T001 Add `AUCTION_SERVICE_URL: "${AUCTION_SERVICE_URL:-http://auction-service:8005}"` in `docker-compose.yml`
- [X] T002 [P] Configure `AuctionService:BaseUrl` as `"http://auction-service:8005"` in `Commerce.Api/appsettings.json` and `Commerce.Api/appsettings.Development.json`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core client interfaces, request/response DTOs, and dependency registrations that all user stories depend on

**⚠️ CRITICAL**: Must complete before user story implementation begins

- [X] T003 [P] Define `IAuctionClient` outbound contract with method `NotifyPaymentStatusAsync` (supporting `Paid`, `SellerAccepted`, `SellerRejected`, `SellerExpired`, `PaymentTimedOut`, `paidAt`, `amount`, `eventId`) in `Commerce.Application/Interfaces/IAuctionClient.cs`
- [X] T004 [P] Implement `AuctionClient` using typed `HttpClient`, `InternalServiceAuthHandler` (`X-Internal-Service-Key`), and stable `eventId` retry generation in `Commerce.Infrastructure/Services/Auction/AuctionClient.cs`
- [X] T005 Register `AddHttpClient<IAuctionClient, AuctionClient>` with resilience handlers in `Commerce.Infrastructure/DependencyInjection.cs`
- [X] T006 [P] Create request DTOs (`AuctionSettlementRequest`, `SubmitAuctionShippingRequest`, `SellerAcceptAuctionOrderRequest`, `SellerRejectAuctionOrderRequest`) with verbatim constraints in `Commerce.Application/Services/AuctionOrder/AuctionOrderRequests.cs`
- [X] T007 [P] Create response DTOs (`AuctionSettlementResponse`, `AuctionOrderDetailResponse`, `AuctionOrderSummaryResponse`, `PayAuctionOrderResponse`, `SellerRespondResponse`, `ConfirmReceiptResponse`, `PagedAuctionOrderSummary`) in `Commerce.Application/Services/AuctionOrder/AuctionOrderResponses.cs`
- [X] T008 Define `IAuctionOrderService` interface covering all 8 feature methods in `Commerce.Application/Services/AuctionOrder/IAuctionOrderService.cs`
- [X] T009 Register `IAuctionOrderService` as scoped service in `Commerce.Application/DependencyInjection.cs`

**Checkpoint**: Foundation ready - all core contracts, DTOs, and client infrastructure are compiled and ready for user stories.

---

## Phase 3: User Story 1 - Settlement Ingestion & Multi-Winner Idempotency (Priority: P1) 🎯 MVP

**Goal**: Auction Service pushes settlement to Commerce to create an `AuctionOrder` in `PendingPayment` status, enforcing `(AuctionId, WinnerId)` idempotency to support runner-up winners.

**Independent Test**: Call `POST /api/v1/auction-orders/settlement`. Assert HTTP 201 returns `orderId` and `orderNo`. Resending identical payload returns HTTP 200 OK. Resending for same `AuctionId` with different `WinnerId` when original is cancelled creates a new order.

- [X] T010 [P] [US1] Create unit tests for settlement ingestion, duplicate idempotency (HTTP 200), and runner-up replacement in `tests/Commerce.UnitTests/Application/AuctionOrderServiceTests.cs`
- [X] T011 [US1] Implement `ReceiveSettlementAsync` in `Commerce.Application/Services/AuctionOrder/AuctionOrderService.cs` (composite `(AuctionId, WinnerId)` idempotency, sequence generator `AO-yyyyMMdd-xxxx`, platform fee calculation, exact `paymentDeadlineAt` persistence)
- [X] T012 [US1] Implement endpoint `POST /api/v1/auction-orders/settlement` with `X-Internal-Service-Key` verification in `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`

**Checkpoint**: User Story 1 (MVP) is independently functional and testable.

---

## Phase 4: User Story 2 - Winner Shipping Address Submission & Logistics Quote (Priority: P1)

**Goal**: Authenticated winning buyer submits delivery address to calculate carrier shipping fee and finalize `TotalAmount`.

**Independent Test**: Call `PUT /api/v1/auction-orders/{id}/shipping`. Assert `ShippingAddress` jsonb is persisted and `TotalAmount = WinningAmount + ShippingFeeAmount`.

- [X] T013 [P] [US2] Create unit tests for shipping address submission, GHN quote calculation, and deadline validation in `tests/Commerce.UnitTests/Application/AuctionOrderServiceTests.cs`
- [X] T014 [US2] Implement `SubmitShippingAsync` in `Commerce.Application/Services/AuctionOrder/AuctionOrderService.cs` (verify ownership, check deadline, calculate fee via `_fulfillmentClient.CalculateShopShippingFeeAsync`, serialize `ShippingAddress` jsonb, finalize `TotalAmount`)
- [X] T015 [US2] Implement endpoint `PUT /api/v1/auction-orders/{auctionOrderId}/shipping` in `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`

**Checkpoint**: User Story 2 is independently functional and testable.

---

## Phase 5: User Story 3 - Winner Wallet Payment & Immediate "Paid" Callback Dispatch (Priority: P1)

**Goal**: Authenticated winner pays via Nexus Wallet, sets `PaidAt`, activates seller 24h deadline, and immediately calls Auction Service callback `status: "Paid"`.

**Independent Test**: Call `POST /api/v1/auction-orders/{id}/pay`. Assert wallet debit, `Status == Paid`, `SellerRespondDeadlineAt` set to +24h, and mock Auction Service receives callback with `paidAt` and `amount`.

- [X] T016 [P] [US3] Create unit tests for wallet payment, deadline validation, and "Paid" callback dispatch in `tests/Commerce.UnitTests/Application/AuctionOrderServiceTests.cs`
- [X] T017 [US3] Implement `PayAuctionOrderAsync` in `Commerce.Application/Services/AuctionOrder/AuctionOrderService.cs` (validate `TotalAmount` present, enforce strict `PaymentDeadlineAt`, execute `_walletClient.PayOrderAsync`, record `PaidAt`, set `SellerRespondDeadlineAt = PaidAt + 24h`, dispatch callback `Paid` with `amount = WinningAmount` and `paidAt`)
- [X] T018 [US3] Implement endpoint `POST /api/v1/auction-orders/{auctionOrderId}/pay` in `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`

**Checkpoint**: User Story 3 is independently functional and testable.

---

## Phase 6: User Story 4 - Seller Order Acceptance & "SellerAccepted" Callback Dispatch (Priority: P1)

**Goal**: Seller accepts paid auction order within 24 hours, transitions order to `Confirmed`, creates carrier shipment, and dispatches callback `status: "SellerAccepted"`.

**Independent Test**: Call `POST /api/v1/auction-orders/{id}/accept`. Assert `Status == Confirmed`, shipment created via Fulfillment Service, and callback dispatched.

- [X] T019 [P] [US4] Create unit tests for seller accept within deadline and "SellerAccepted" callback dispatch in `tests/Commerce.UnitTests/Application/AuctionOrderServiceTests.cs`
- [X] T020 [US4] Implement `SellerAcceptAsync` in `Commerce.Application/Services/AuctionOrder/AuctionOrderService.cs` (verify `SellerId`, enforce `UtcNow <= SellerRespondDeadlineAt`, set `Status = Confirmed`, trigger `_fulfillmentClient.CreateShipmentAsync`, dispatch callback `SellerAccepted`)
- [X] T021 [US4] Implement endpoint `POST /api/v1/auction-orders/{auctionOrderId}/accept` in `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`

**Checkpoint**: User Story 4 is independently functional and testable.

---

## Phase 7: User Story 5 - Seller Order Rejection with Automated Wallet Refund & "SellerRejected" Callback (Priority: P1)

**Goal**: Seller rejects paid auction order, transitions to `Cancelled`, executes 100% automated wallet refund to buyer (fail-safe), and dispatches callback `status: "SellerRejected"`.

**Independent Test**: Call `POST /api/v1/auction-orders/{id}/reject`. Assert `Status == Cancelled`, `OrderCancellation` and `OrderRefund` created with `AuctionOrderId`, buyer refunded via `IWalletClient`, and callback dispatched.

- [X] T022 [P] [US5] Create unit tests for seller reject, 100% wallet refund, and fail-safe callback dispatch in `tests/Commerce.UnitTests/Application/AuctionOrderServiceTests.cs`
- [X] T023 [US5] Implement `SellerRejectAsync` in `Commerce.Application/Services/AuctionOrder/AuctionOrderService.cs` (verify `SellerId`, prevent reject if shipped, set `Status = Cancelled`, create `OrderCancellation` with `AuctionOrderId`, execute 100% wallet refund via `_walletClient.RefundOrderPaymentAsync`, insert `OrderRefund`, fail-safe dispatch callback `SellerRejected`)
- [X] T024 [US5] Implement endpoint `POST /api/v1/auction-orders/{auctionOrderId}/reject` in `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`

**Checkpoint**: User Story 5 is independently functional and testable.

---

## Phase 8: User Story 6 - Automated 24h Payment Timeout Cancellation & "PaymentTimedOut" Callback (Priority: P1)

**Goal**: Background worker auto-cancels auction orders exceeding 24h payment deadline, remediating the latent foreign key bug and notifying Auction Service with callback `status: "PaymentTimedOut"`.

**Independent Test**: Run `PaymentTimeoutWorker.ProcessExpiredOrdersAsync` with an expired pending auction order. Assert order cancels, `OrderCancellation.AuctionOrderId` is set (`SubOrderId` is null), and callback `PaymentTimedOut` is dispatched.

- [X] T025 [P] [US6] Create unit tests verifying the foreign key remediation (`AuctionOrderId` populated, `SubOrderId` null) and "PaymentTimedOut" callback in `tests/Commerce.UnitTests/Application/PaymentTimeoutWorkerTests.cs`
- [X] T026 [US6] Remediate `PaymentTimeoutWorker.cs` line 160 by replacing `SubOrderId = auctionOrder.Id` with `AuctionOrderId = auctionOrder.Id` and `SubOrderId = null`, and dispatching callback `status: "PaymentTimedOut"` via `_auctionClient`

**Checkpoint**: User Story 6 is independently functional and testable.

---

## Phase 9: User Story 7 - Automated 24h Seller Response Timeout with Wallet Refund & "SellerExpired" Callback (Priority: P1)

**Goal**: Background worker scans for paid auction orders where seller failed to accept within 24 hours (`SellerRespondDeadlineAt < UtcNow`), auto-cancels, executes 100% wallet refund, and dispatches callback `status: "SellerExpired"`.

**Independent Test**: Run worker with an unaccepted paid order past `SellerRespondDeadlineAt`. Assert order cancels, 100% wallet refund executes, and callback `SellerExpired` is dispatched.

- [X] T027 [P] [US7] Create unit tests for seller respond timeout, 100% wallet refund, and "SellerExpired" callback dispatch in `tests/Commerce.UnitTests/Application/PaymentTimeoutWorkerTests.cs`
- [X] T028 [US7] Implement scanning loop in `Commerce.Api/Workers/PaymentTimeoutWorker.cs` for `Status == Paid && SellerRespondDeadlineAt < UtcNow`: auto-cancel, insert `OrderCancellation` (`ActorRole = System`, `ReasonCode = SellerRejected`), execute 100% wallet refund via `_walletClient.RefundOrderPaymentAsync`, insert `OrderRefund`, and dispatch callback `SellerExpired`

**Checkpoint**: User Story 7 is independently functional and testable.

---

## Phase 10: User Story 8 - Buyer Delivery Confirmation & Seller Escrow Settlement (Priority: P2)

**Goal**: Buyer confirms package receipt, transitioning order to `Completed` and releasing escrow payout to seller.

**Independent Test**: Call `POST /api/v1/auction-orders/{id}/confirm-receipt`. Assert `Status == Completed` and `_walletClient.SettleSellerSubOrderAsync` is called with `SellerReceiveAmount`.

- [X] T029 [P] [US8] Create unit tests for buyer receipt confirmation and escrow release in `tests/Commerce.UnitTests/Application/AuctionOrderServiceTests.cs`
- [X] T030 [US8] Implement `ConfirmReceiptAsync` in `Commerce.Application/Services/AuctionOrder/AuctionOrderService.cs` (verify `BuyerId`, verify `Status == Delivered`, set `Status = Completed`, call `_walletClient.SettleSellerSubOrderAsync`)
- [X] T031 [US8] Implement endpoint `POST /api/v1/auction-orders/{auctionOrderId}/confirm-receipt` in `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`

**Checkpoint**: User Story 8 is independently functional and testable.

---

## Phase 11: User Story 9 - Logistics Webhook Synchronization & Order Queries (Priority: P2)

**Goal**: Synchronize carrier delivery status updates for auction orders and provide buyer/seller order query endpoints.

**Independent Test**: Send webhook to `POST /api/v1/sub-orders/{subOrderId}/delivery-status` with auction order ID. Assert order updates to `Shipped` or `Delivered`. Call `GET /api/v1/auction-orders/seller` and verify pagination.

- [X] T032 [P] [US9] Implement `GetOrderDetailAsync` and `ListSellerOrdersAsync` with database pagination in `Commerce.Application/Services/AuctionOrder/AuctionOrderService.cs`
- [X] T033 [US9] Implement endpoints `GET /api/v1/auction-orders/{auctionOrderId}` and `GET /api/v1/auction-orders/seller` in `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`
- [X] T034 [US9] Extend delivery status webhook in `Commerce.Application/Services/Order/OrderService.cs` (`UpdateSubOrderDeliveryStatusAsync`) to fallback and update `AuctionOrders` when `subOrderId` matches an auction order

**Checkpoint**: User Story 9 is independently functional and testable.

---

## Phase 12: Polish & Cross-Cutting Concerns

**Purpose**: Documentation updates, full regression test execution, and verification against constitution quality gates

- [X] T035 [P] Update API documentation in `docs/COMMERCE_API_CONTRACT.md` with new Auction Order endpoints
- [X] T036 Execute full solution build `dotnet build Commerce.sln -c Release` (verify 0 errors, 0 warnings)
- [X] T037 Execute full test suite `dotnet test Commerce.sln -c Release` (verify 100% pass rate)
- [X] T038 Execute end-to-end verification scenarios per `quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies
- **Setup (Phase 1)**: Can start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1; blocks all User Story phases.
- **User Story 1 (Phase 3)**: Depends on Phase 2 (MVP!).
- **User Story 2 (Phase 4)**: Depends on Phase 3 (needs created order).
- **User Story 3 (Phase 5)**: Depends on Phase 4 (needs quoted total).
- **User Story 4 & 5 (Phases 6 & 7)**: Depend on Phase 5 (needs paid order).
- **User Story 6 & 7 (Phases 8 & 9)**: Depend on Phase 2; can be developed in parallel with or after User Stories 1-5.
- **User Story 8 & 9 (Phases 10 & 11)**: Depend on Phase 6 (confirmed/shipped order).
- **Polish (Phase 12)**: Depends on all user stories completion.

### Parallel Opportunities
- Within Foundational Phase: T003, T004, T006, T007 can be authored in parallel.
- Across User Stories: Once Foundational is ready, test authoring tasks (T010, T013, T016, T019, T022, T025, T027, T029) can run in parallel.
- Worker timeout fixes (Phases 8 & 9) can proceed in parallel with API endpoint implementations.

---

## Implementation Strategy: MVP First

1. **Step 1**: Complete Phase 1 (Setup) and Phase 2 (Foundational).
2. **Step 2**: Implement Phase 3 (User Story 1).
3. **Step 3 - STOP & VALIDATE**: Verify settlement ingestion and idempotency tests pass $\rightarrow$ **MVP Delivered!**
4. **Step 4**: Incrementally deliver User Story 2 (Shipping) $\rightarrow$ User Story 3 (Pay) $\rightarrow$ User Story 4 (Accept) $\rightarrow$ User Story 5 (Reject & Refund).
5. **Step 5**: Remediate background workers in Phases 8 & 9.
6. **Step 6**: Complete delivery webhook and queries in Phases 10 & 11.
7. **Step 7**: Run Phase 12 validation for 0 error / 0 warning clean build and 100% test pass.
