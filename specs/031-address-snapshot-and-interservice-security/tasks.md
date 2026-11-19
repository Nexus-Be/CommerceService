# Tasks: Address Snapshot Parity, Checkout Address Integration & S2S Internal Security

**Input**: Design documents from `/specs/031-address-snapshot-and-interservice-security/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

## Format: `[ID] [P?] [Story] Description with exact file path`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (`US1`, `US2`, `US3`, `US4`, `US5`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Xác thực môi trường và baseline ban đầu

- [X] T001 Verify project baseline build with `dotnet build Commerce.sln -c Release --no-restore`
- [X] T002 Verify baseline unit test execution (362 tests) with `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore`
- [X] T003 [P] Add `BuyerAddressDto` record in `Commerce.Application/Interfaces/UserClientModels.cs`
- [X] T004 [P] Declare `GetBuyerAddressAsync` and `GetBuyerDefaultAddressAsync` in `Commerce.Application/Interfaces/IUserClient.cs`
- [X] T005 Implement `GetBuyerAddressAsync` and `GetBuyerDefaultAddressAsync` with `X-Internal-Service-Key` and safe null-on-404 in `Commerce.Infrastructure/Services/User/UserClient.cs`
- [X] T006 [P] [US1] Create unit tests in `tests/Commerce.UnitTests/Services/UserClientAddressTests.cs` verifying `GetBuyerAddressAsync` and `GetBuyerDefaultAddressAsync` handle 200 OK, 404 Not Found, and 503 retry properly

**Checkpoint**: User Story 1 hoàn tất và có thể kiểm thử độc lập.

---

## Phase 4: User Story 2 - Hỗ trợ `AddressId` & Auto-fallback Default Address trong Báo giá và Đơn Đấu giá (Priority: P1)

**Goal**: Cho phép khách hàng chỉ cần gửi `AddressId` hoặc để trống địa chỉ khi gọi báo giá Checkout hoặc gửi địa chỉ nhận hàng đấu giá, hệ thống sẽ tự động phân giải địa chỉ hợp lệ kèm mã vùng GHN.

**Independent Test**: Gọi `POST /api/v1/orders/quote` với `shippingAddress: null` và `addressId: null` $\rightarrow$ Hệ thống tự động phân giải địa chỉ mặc định từ `UserService`, tính cước ship GHN chính xác và trả về báo giá.

### Implementation for User Story 2
- [X] T007 [P] [US2] Update `CheckoutQuoteRequest` in `Commerce.Application/Services/Order/OrderRequests.cs` to add optional `Guid? AddressId`
- [X] T008 [P] [US2] Update `SubmitAuctionShippingRequest` in `Commerce.Application/Services/AuctionOrder/AuctionOrderRequests.cs` to add optional `Guid? AddressId` and make freeform fields nullable
- [X] T009 [US2] Implement address resolution logic in `Commerce.Application/Services/Order/OrderService.cs` resolving `AddressId` $\rightarrow$ default address fallback $\rightarrow$ validated raw shipping address with GHN codes
- [X] T010 [US2] Update `OrderService.QuoteCheckoutAsync` in `Commerce.Application/Services/Order/OrderService.cs` to use the address resolution logic
- [X] T011 [US2] Update `AuctionOrderService.SubmitShippingAsync` in `Commerce.Application/Services/AuctionOrder/AuctionOrderService.cs` to resolve address using `IUserClient` when `AddressId` is present or raw address is null
- [X] T012 [P] [US2] Add unit tests in `tests/Commerce.UnitTests/Application/OrderAddressResolutionTests.cs` verifying `AddressId`, default address fallback, and GHN validation for `QuoteCheckoutAsync` and `SubmitShippingAsync`

**Checkpoint**: User Story 2 hoàn tất. Khách hàng không cần tự nhập tay địa chỉ nếu đã có trong sổ địa chỉ.

---

## Phase 5: User Story 3 - API Re-quote phí ship GHN khi đổi địa chỉ trên Checkout (Priority: P1)

**Goal**: Cung cấp API `POST /api/v1/checkouts/quote/{quoteId:guid}/recalculate` cho phép tính lại phí ship GHN đa shop khi khách hàng đổi địa chỉ tại bước thanh toán mà không làm mất giỏ hàng hay voucher.

**Independent Test**: Gọi `POST /api/v1/checkouts/quote/{quoteId}/recalculate` với địa chỉ mới $\rightarrow$ Cập nhật lại cước GHN từng shop, cập nhật `CachedQuote` và trả về `CheckoutQuoteResponse` mới.

### Implementation for User Story 3
- [X] T013 [P] [US3] Add `RecalculateQuoteRequest` DTO in `Commerce.Application/Services/Order/OrderRequests.cs`
- [X] T014 [US3] Declare `RecalculateQuoteAsync` in `Commerce.Application/Services/Order/IOrderService.cs` and implement it in `Commerce.Application/Services/Order/OrderService.cs` reading `CachedQuote`, recalculating shipping fees per shop, and updating cache
- [X] T015 [US3] Add `[HttpPost("/api/v1/checkouts/quote/{quoteId:guid}/recalculate")]` endpoint in `Commerce.Api/Controllers/Public/OrdersController.cs`
- [X] T016 [P] [US3] Add unit tests in `tests/Commerce.UnitTests/Application/OrderRequoteTests.cs` verifying recalculation with new address, quote expiry handling, and ownership enforcement

**Checkpoint**: User Story 3 hoàn tất. Frontend có thể cập nhật phí ship linh hoạt ngay tại Checkout.

---

## Phase 6: User Story 4 - Internal API xác minh đơn hàng phục vụ Khiếu nại/Tố cáo cho UserService (Priority: P1)

**Goal**: Cung cấp API nội bộ `GET /api/v1/internal/orders/dispute-eligibility` bảo vệ bằng `X-Internal-Service-Key` phục vụ xác minh giao dịch cho tính năng Tố cáo / Khiếu nại của `UserService`.

**Independent Test**: Gọi `GET /api/v1/internal/orders/dispute-eligibility` với header `X-Internal-Service-Key` $\rightarrow$ Trả về `200 OK` với `isEligible: true` nếu đơn đã ở trạng thái `Delivered` hoặc `Completed` và đúng cặp người mua/bán.

### Implementation for User Story 4
- [X] T017 [P] [US4] Add `DisputeEligibilityDto` in `Commerce.Application/Services/Order/DisputeEligibilityDto.cs`
- [X] T018 [US4] Declare `GetDisputeEligibilityAsync` in `Commerce.Application/Services/Order/IOrderService.cs` and implement it in `Commerce.Application/Services/Order/OrderService.cs` querying `SubOrders`, `Orders`, and `AuctionOrders` with status and user matching
- [X] T019 [US4] Add `[HttpGet("/api/v1/internal/orders/dispute-eligibility")]` with `[AllowAnonymous]` and `InternalServiceKeyGuard` check in `Commerce.Api/Controllers/Public/OrdersController.cs`
- [X] T020 [P] [US4] Add unit tests in `tests/Commerce.UnitTests/Application/OrderDisputeEligibilityTests.cs` verifying SubOrder, Order, AuctionOrder matching, `Delivered`/`Completed` requirement, and key authentication

**Checkpoint**: User Story 4 hoàn tất. `UserService` có thể xác thực giao dịch trước khi mở Dispute/Report.

---

## Phase 7: User Story 5 - Đồng bộ `AuctionCallbackRetryWorker` & Dọn dẹp Worker `PaymentTimeoutWorker` (Priority: P2)

**Goal**: Đảm bảo các Background Worker retry đồng bộ payload chuẩn xác qua `AuctionPaymentCallbackFactory.Build` và không spam phạt seller khi hết hạn phản hồi.

**Independent Test**: Kích hoạt `AuctionCallbackRetryWorker` $\rightarrow$ Payload gửi sang `AuctionService` chứa đầy đủ 9 trường thông tin khớp hoàn toàn với lần gửi ban đầu.

### Implementation for User Story 5
- [X] T021 [US5] Update `Commerce.Api/Workers/AuctionCallbackRetryWorker.cs` to use `AuctionPaymentCallbackFactory.Build(order, history)` ensuring identical 9 fields on callback retry
- [X] T022 [US5] Verify and cleanup `PaymentTimeoutWorker.cs` seller penalty block to ensure single call with `UserPenaltyViolationTypes.SellerResponseTimeout`
- [X] T023 [P] [US5] Add unit tests in `tests/Commerce.UnitTests/Workers/AuctionCallbackWorkerParityTests.cs` verifying factory usage on callback retry

**Checkpoint**: User Story 5 hoàn tất. Toàn bộ worker đạt tính kiên cố và nhất quán.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Đồng bộ tài liệu hợp đồng API, kiểm chứng tổng thể và dọn dẹp working tree

- [X] T024 [P] Update `docs/COMMERCE_API_CONTRACT.md` documenting recalculate-quote and dispute-eligibility endpoints
- [X] T025 Run full test suite verification with `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore` confirming 100% pass rate
- [X] T026 Execute `quickstart.md` validation checklist and ensure clean git working tree without temporary logs or build artifacts

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3 through Phase 7)**:
  - US1 (T006) can start immediately after Phase 2
  - US2 (T007-T012) depends on US1
  - US3 (T013-T016) depends on US2 (address resolution helper)
  - US4 (T017-T020) independent of US1-US3
  - US5 (T021-T023) independent of US1-US4
- **Polish (Phase 8)**: Depends on all user stories being complete

---

## Implementation Strategy

### MVP First (User Story 1 & 2)
1. Hoàn thành Phase 1 & 2 (Baseline verification & DTOs).
2. Triển khai Phase 3: US1 (`UserClient` Address S2S).
3. Triển khai Phase 4: US2 (`AddressId` & Default Address Fallback).
4. **STOP & VALIDATE**: Chạy test kiểm chứng US1 & US2 độc lập.

### Incremental Delivery
1. Baseline verified $\rightarrow$ Deliver US1 (`UserClient` Address S2S).
2. Deliver US2 (Address Resolution & Fallback).
3. Deliver US3 (Re-quote GHN API).
4. Deliver US4 (Internal Dispute Eligibility API).
5. Deliver US5 (Worker Resilience Parity).
6. Polish & Full Test Suite $\rightarrow$ Ready for Commit / Push.
