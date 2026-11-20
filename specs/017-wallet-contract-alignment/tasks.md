# Tasks: WalletService Integration Contract Alignment & Direct VNPay Orchestration

**Input**: Design documents from `/specs/017-wallet-contract-alignment/`  
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/wallet-integration-api.yaml](contracts/wallet-integration-api.yaml)

## Format: `[ID] [P?] [Story?] Description with file path`
- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story identifier (US1, US2, US3, US4)
- Exact file paths included in every task

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verification of baseline test suite and environment configuration

- [x] T001 Inspect and execute existing test suite baseline via `dotnet test Commerce.sln -c Release` to ensure 100% passing state prior to changes
- [x] T002 [P] Verify `WALLET_SERVICE_URL` configuration fallback in `Commerce.Infrastructure/DependencyInjection.cs` pointing to `http://localhost:8008/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core application interfaces, DTO definitions, and transport parsers required before any user story can be executed

**⚠️ CRITICAL**: All user story implementations depend on these foundational contracts

- [x] T003 [P] Update `Commerce.Application/Interfaces/WalletPaymentResult.cs` to add `DirectCheckoutResult` record and problem details fields (`ErrorCode`, `ErrorMessage`, `IsTransientFailure`)
- [x] T004 [P] Update `Commerce.Application/Interfaces/IWalletClient.cs` to add `Guid buyerId` parameter to `PayOrderAsync`, and declare `InitiateDirectCheckoutAsync` and `GetDirectCheckoutStatusAsync`
- [x] T005 Implement RFC 9110 Problem Details parser in `Commerce.Infrastructure/Services/Wallet/WalletClient.cs` to extract `errorCode` directly from `application/problem+json` (handling `INSUFFICIENT_BALANCE` at HTTP 400 and `PAYMENT_ORDER_CONFLICT` at HTTP 409)
- [x] T006 Remove legacy `Idempotency-Key` headers from all S2S calls in `Commerce.Infrastructure/Services/Wallet/WalletClient.cs` to anchor strictly on route business keys (`{orderId}` and `{subOrderId}`)

**Checkpoint**: Foundational contracts ready — User story implementation can now begin.

---

## Phase 3: User Story 1 - Reliable Wallet Payment via Trusted Identity Propagation (Priority: P1) 🎯 MVP

**Goal**: Authenticate wallet debit calls using `x-user-id: <buyer-uuid>`, capture Problem Details `INSUFFICIENT_BALANCE`, and record transaction UUIDs without token mismatch.

**Independent Test**: Submit `POST /api/v1/orders` with `PaymentMethod.Wallet` as an authenticated buyer with sufficient funds. Verify `_walletClient.PayOrderAsync` sends `x-user-id: <buyer-uuid>`, returns `PurchaseDebit`, sets `order.Status = Paid`, and commits stock via `_fulfillmentClient.CommitStockAsync`.

### Tests for User Story 1
- [x] T007 [P] [US1] Update unit tests in `tests/Commerce.UnitTests/Orders/OrderCheckoutTests.cs` to mock `IWalletClient.PayOrderAsync` with `buyerId` and assert `x-user-id` header presence and `400 INSUFFICIENT_BALANCE` handling

### Implementation for User Story 1
- [x] T008 [US1] Update `PayOrderAsync` in `Commerce.Infrastructure/Services/Wallet/WalletClient.cs` to inject `x-user-id: <buyer-uuid>` HTTP header using provided `buyerId` or falling back to `_httpContextAccessor`
- [x] T009 [US1] Update `CreateOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` to pass `order.BuyerId` into `_walletClient.PayOrderAsync(order.Id, order.BuyerId, order.TotalAmount, cancellationToken: ct)`
- [x] T010 [US1] Run unit tests in `tests/Commerce.UnitTests/Orders/OrderCheckoutTests.cs` to confirm User Story 1 MVP passes completely

**Checkpoint**: User Story 1 (MVP) is fully functional and independently verified.

---

## Phase 4: User Story 2 - Automated Seller Escrow Settlement & Order Cancellation Refunds (Priority: P1)

**Goal**: Wire automated seller payouts upon buyer receipt confirmation and automated buyer refunds upon sub-order cancellation or rejection.

**Independent Test**:
1. Confirm receipt on a delivered sub-order; assert `SubOrder.Status = Completed` and verify outbound POST to `/api/v1/wallets/sub-orders/{subOrderId}/settle` with `{ "sellerId": <uuid>, "amount": <amount> }`.
2. Reject a paid sub-order; assert `SubOrder.Status = Cancelled` and verify outbound POST to `/api/v1/wallets/sub-orders/{subOrderId}/refund`.

### Tests for User Story 2
- [x] T011 [P] [US2] Add unit tests in `tests/Commerce.UnitTests/Orders/OrderSettlementRefundTests.cs` covering seller escrow settlement on receipt confirmation and automated refund on sub-order cancellation

### Implementation for User Story 2
- [x] T012 [US2] Update `SettleSellerSubOrderAsync`, `RefundSubOrderPaymentAsync`, and `RefundOrderPaymentAsync` in `Commerce.Infrastructure/Services/Wallet/WalletClient.cs` to transmit clean payloads without identity headers or `Idempotency-Key` headers
- [x] T013 [US2] Connect `BuyerConfirmSubOrderReceiptAsync` and `ConfirmDeliveryMasterOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` to invoke `_walletClient.SettleSellerSubOrderAsync` with idempotency conflict handling
- [x] T014 [US2] Connect `SellerRejectSubOrderAsync` and `CancelSubOrderByBuyerAsync`/`CancelSubOrderBySellerAsync` in `Commerce.Application/Services/Order/OrderService.cs` to invoke `_walletClient.RefundSubOrderPaymentAsync` and update `OrderRefund.Status = Completed`
- [x] T015 [US2] Ensure database compensation block in `OrderService.CreateOrderAsync` invokes `_walletClient.RefundOrderPaymentAsync` to reimburse buyer if order persistence fails

**Checkpoint**: User Story 2 is fully functional with automated financial disbursements and refunds verified.

---

## Phase 5: User Story 3 - Direct Gateway Checkout & Background Payment Reconciliation (Priority: P1)

**Goal**: Implement authentic merchant-signed VNPay direct checkout initiation (`POST /api/v1/wallets/checkout/direct`) and background polling worker (`GET /api/v1/wallets/checkout/direct/{orderId}`) to commit stock asynchronously upon settlement.

**Independent Test**: Place an order with `PaymentMethod.VnPay`, verify genuine `PaymentUrl` in response. Simulate polling success, verify order transitions to `Paid` and stock is committed.

### Tests for User Story 3
- [x] T016 [P] [US3] Add unit tests in `tests/Commerce.UnitTests/Workers/VnPayPaymentPollingWorkerTests.cs` validating polling status handling (`Success`, `Pending`, `Failed`), stock commit on success, and timeout cancellation

### Implementation for User Story 3
- [x] T017 [US3] Implement `InitiateDirectCheckoutAsync` and `GetDirectCheckoutStatusAsync` in `Commerce.Infrastructure/Services/Wallet/WalletClient.cs` targeting `POST /api/v1/wallets/checkout/direct` and `GET /api/v1/wallets/checkout/direct/{orderId}` with `x-user-id` header
- [x] T018 [US3] Update `CreateOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` to call `InitiateDirectCheckoutAsync` when `request.PaymentMethod == PaymentMethod.VnPay`, replace hardcoded sandbox URL, and populate authentic `PaymentUrl` in `CreateOrderResponse`
- [x] T019 [US3] Create `Commerce.Api/Workers/VnPayPaymentPollingWorker.cs` inheriting `BackgroundService` to poll pending VNPay orders every 30 seconds and execute status transitions
- [x] T020 [US3] Register `VnPayPaymentPollingWorker` in `Commerce.Api/Program.cs` as a hosted service (`builder.Services.AddHostedService<VnPayPaymentPollingWorker>()`)

**Checkpoint**: User Story 3 is fully functional with direct VNPay payments initiated and reconciled asynchronously.

---

## Phase 6: User Story 4 - Real-Time Wallet Balance In Checkout Quotation (Priority: P2)

**Goal**: Propagate `x-user-id` in `GetWalletBalanceAsync` to query live balance from `GET /api/v1/wallets/me` and display in quote response.

**Independent Test**: Call `POST /api/v1/checkouts/quote` as an authenticated buyer, verify outbound call to `GET /api/v1/wallets/me` includes `x-user-id` and returned balance matches `CheckoutQuoteResponse.BuyerWalletBalance`.

### Tests for User Story 4
- [x] T021 [P] [US4] Update unit tests in `tests/Commerce.UnitTests/Orders/CheckoutQuoteTests.cs` to assert live wallet balance is populated in `CheckoutQuoteResponse`

### Implementation for User Story 4
- [x] T022 [US4] Update `GetWalletBalanceAsync` in `Commerce.Infrastructure/Services/Wallet/WalletClient.cs` to transmit `x-user-id: <userId>` header when querying `GET api/v1/wallets/me`
- [x] T023 [US4] Verify `QuoteCheckoutAsync` in `Commerce.Application/Services/Order/OrderService.cs` passes the live balance into `CheckoutQuoteResponse` with graceful zero fallback

**Checkpoint**: User Story 4 is complete with live balance inquiry functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation synchronization, error message auditing, and full regression testing

- [x] T024 [P] Update `docs/WALLET_CAPABILITY_GAP_REPORT.md` and `docs/Commerce/R181_POST_WALLETS_CHECKOUT_DIRECT.md` to reflect contract alignment and close GAP-01, GAP-02, GAP-04, and GAP-06
- [x] T025 Audit all new and modified log messages in `WalletClient.cs`, `OrderService.cs`, and `VnPayPaymentPollingWorker.cs` to ensure 100% English Sentence-case and `SCREAMING_SNAKE_CASE` error codes
- [x] T026 Execute `dotnet build Commerce.sln -c Release` and confirm 0 errors and 0 warnings
- [x] T027 Execute `dotnet test Commerce.sln -c Release` and confirm 100% passing rate across all unit and integration test suites
- [x] T028 Validate all runnable verification scenarios in `specs/017-wallet-contract-alignment/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies
- **Setup (Phase 1)**: No dependencies — starts immediately.
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS ALL USER STORIES**.
- **User Story 1 (Phase 3 - MVP)**: Depends on Foundational completion.
- **User Story 2 (Phase 4)**: Depends on Foundational completion.
- **User Story 3 (Phase 5)**: Depends on Foundational completion.
- **User Story 4 (Phase 6)**: Depends on Foundational completion.
- **Polish (Phase 7)**: Depends on all user stories completing.

### User Story Dependencies
- **US1 (P1 - Wallet Debit)**: Independent after Phase 2.
- **US2 (P1 - Settle/Refund)**: Independent after Phase 2.
- **US3 (P1 - Direct VNPay)**: Independent after Phase 2.
- **US4 (P2 - Quote Balance)**: Independent after Phase 2.

### Parallel Opportunities
- Within Phase 1: `T002` [P] can run alongside `T001`.
- Within Phase 2: `T003` [P] and `T004` [P] can run in parallel.
- Across Stories: Once Phase 2 completes, `US1`, `US2`, `US3`, and `US4` can be implemented in parallel across separate files.
- Within Stories: Test tasks (`T007`, `T011`, `T016`, `T021`) marked [P] can be authored before or in parallel with implementations.

---

## Parallel Execution Example: User Story 3

```bash
# Author unit test for polling worker in parallel:
Task: "Add unit tests in tests/Commerce.UnitTests/Workers/VnPayPaymentPollingWorkerTests.cs"

# Implement client and worker in parallel (different files):
Task: "Implement InitiateDirectCheckoutAsync in Commerce.Infrastructure/Services/Wallet/WalletClient.cs"
Task: "Create Commerce.Api/Workers/VnPayPaymentPollingWorker.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1)
1. Complete Phase 1 (Setup) and Phase 2 (Foundational prerequisites).
2. Complete Phase 3 (User Story 1 - Wallet Payment with `x-user-id`).
3. **VALIDATE MVP**: Run `dotnet test --filter "FullyQualifiedName~OrderCheckoutTests"` to confirm core wallet payment works.

### Incremental Delivery
1. Foundation + US1 $\rightarrow$ Core wallet payment authenticated and functional (MVP).
2. Add US2 $\rightarrow$ Automated refunds on cancellation and seller payouts on delivery confirmation.
3. Add US3 $\rightarrow$ Genuine VNPay checkout links and background polling reconciliation.
4. Add US4 $\rightarrow$ Real-time wallet balance visible on checkout quotation.
5. Polish (Phase 7) $\rightarrow$ Full regression build, docs updated, 0 warnings.
