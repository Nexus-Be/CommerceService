# Tasks: Post-Order Lifecycle, Address Modification and Refund Governance

**Input**: Design documents from `/specs/032-post-order-lifecycle-and-refund-governance/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

## Format: `[ID] [P?] [Story] Description with exact file path`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (`US1`, `US2`, `US3`, `US4`)

---

## Phase 1: Setup & Baseline Verification

**Purpose**: Xác thực môi trường và baseline test trước khi can thiệp

- [X] T001 Verify project baseline build with `dotnet build Commerce.sln -c Release --no-restore`
- [X] T002 Verify baseline unit test execution (385 tests) with `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Tạo DTOs và Interfaces nền tảng cho đổi địa chỉ, quản trị hoàn tiền và vòng đời đơn hàng

- [X] T003 [P] Add `ChangeShippingAddressRequest` and `ChangeShippingAddressResponse` in `Commerce.Application/Services/Order/OrderRequests.cs` and `OrderResponses.cs`
- [X] T004 [P] Add `ApproveRefundRequest`, `RejectRefundRequest`, and `RefundDetailResponse` in `Commerce.Application/Services/Order/OrderRequests.cs` and `OrderResponses.cs`
- [X] T005 [P] Add `CancelAuctionOrderRequest`, `CancelAuctionOrderResponse`, and `RetryAuctionShipmentResponse` in `Commerce.Application/Services/AuctionOrder/AuctionOrderRequests.cs` and `AuctionOrderResponses.cs`
- [X] T006 [P] Add `AdminOrderFilterRequest` in `Commerce.Application/Services/Order/OrderRequests.cs`

**Checkpoint**: Foundation ready - user story implementation can now begin

---

## Phase 3: User Story 1 - Đổi địa chỉ nhận hàng sau khi đặt hàng (Priority: P1) 🎯 MVP

**Goal**: Cho phép người mua đổi địa chỉ nhận hàng cho `SubOrder`, `Order` và `AuctionOrder` khi đơn chưa xuất kho và bảo vệ cước vận chuyển GHN.

**Independent Test**: Gọi `PATCH /api/v1/sub-orders/{subOrderId}/shipping-address` với `AddressId` mới $\rightarrow$ Kiểm tra cước mới $\le$ cước cũ, cập nhật snapshot và ghi nhận lịch sử thay đổi.

### Implementation for User Story 1
- [X] T007 [US1] Declare and implement `ChangeSubOrderShippingAddressAsync` in `Commerce.Application/Services/Order/IOrderService.cs` and `OrderService.cs`
- [X] T008 [US1] Declare and implement `ChangeOrderShippingAddressAsync` in `Commerce.Application/Services/Order/IOrderService.cs` and `OrderService.cs`
- [X] T009 [US1] Declare and implement `ChangeAuctionShippingAddressAsync` in `Commerce.Application/Services/AuctionOrder/IAuctionOrderService.cs` and `AuctionOrderService.cs`
- [X] T010 [US1] Add endpoints `PATCH /api/v1/sub-orders/{subOrderId:guid}/shipping-address` and `PATCH /api/v1/orders/{orderId:guid}/shipping-address` in `Commerce.Api/Controllers/Public/OrdersController.cs`
- [X] T011 [US1] Add endpoint `PATCH /api/v1/auction-orders/{auctionOrderId:guid}/shipping-address` in `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`
- [X] T012 [P] [US1] Add unit tests in `tests/Commerce.UnitTests/Application/OrderAddressChangeTests.cs` verifying address change on PendingPayment, Paid (fee <= old fee), fee increase rejection (400), and shipped boundary (400)

**Checkpoint**: User Story 1 hoàn tất và có thể kiểm thử độc lập.

---

## Phase 4: User Story 2 - Chuẩn hóa & Mở rộng Quản trị Hoàn tiền (`OrderRefund`) (Priority: P1)

**Goal**: Khóa chặt điều kiện hoàn tiền (chỉ `Delivered` hoặc `Completed`), chống spam trùng lặp và cung cấp bộ API quản trị `RefundsController` (list, detail, approve, reject).

**Independent Test**: Khách hàng gửi yêu cầu refund $\rightarrow$ Chặn gửi trùng với HTTP 409 `REFUND_REQUEST_ALREADY_EXISTS`. Seller duyệt `approve` $\rightarrow$ Hoàn tiền ví qua `WalletClient` và chuyển trạng thái SubOrder sang `Refunded`.

### Implementation for User Story 2
- [X] T013 [US2] Update `RefundSubOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` validating `Delivered`/`Completed` status and blocking duplicate pending requests (409)
- [X] T014 [US2] Declare and implement `ListRefundsAsync`, `GetRefundDetailAsync`, `ApproveRefundAsync`, `RejectRefundAsync` in `Commerce.Application/Services/Order/IOrderService.cs` and `OrderService.cs`
- [X] T015 [US2] Create `Commerce.Api/Controllers/Public/RefundsController.cs` with `GET /api/v1/refunds`, `GET /api/v1/refunds/{id}`, `POST /api/v1/refunds/{id}/approve`, `POST /api/v1/refunds/{id}/reject`
- [X] T016 [P] [US2] Add unit tests in `tests/Commerce.UnitTests/Application/RefundGovernanceTests.cs` verifying state restrictions, duplicate prevention, approve wallet refund, and reject reason

**Checkpoint**: User Story 2 hoàn tất và có thể kiểm thử độc lập.

---

## Phase 5: User Story 3 - Mở rộng vòng đời Đơn hàng Đấu giá (Priority: P1)

**Goal**: Bổ sung theo dõi lộ trình vận chuyển, hủy đơn đấu giá trước khi người bán chấp nhận (kèm hoàn tiền ví), và nút thử lại tạo vận đơn GHN khi nghẽn mạng.

**Independent Test**: Gọi `POST /api/v1/auction-orders/{id}/cancel` khi đơn ở trạng thái `Paid` $\rightarrow$ Tự động hoàn tiền ví, hủy đơn và gửi callback sang `AuctionService`.

### Implementation for User Story 3
- [X] T017 [US3] Implement `GetAuctionShipmentTimelineAsync` in `Commerce.Application/Services/AuctionOrder/IAuctionOrderService.cs` and `AuctionOrderService.cs`
- [X] T018 [US3] Implement `CancelAuctionOrderAsync` in `Commerce.Application/Services/AuctionOrder/IAuctionOrderService.cs` and `AuctionOrderService.cs`
- [X] T019 [US3] Implement `RetryAuctionShipmentAsync` in `Commerce.Application/Services/AuctionOrder/IAuctionOrderService.cs` and `AuctionOrderService.cs`
- [X] T020 [US3] Add endpoints `GET /api/v1/auction-orders/{id:guid}/shipments/timeline`, `POST /api/v1/auction-orders/{id:guid}/cancel`, `POST /api/v1/auction-orders/{id:guid}/shipments/retry` in `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`
- [X] T021 [P] [US3] Add unit tests in `tests/Commerce.UnitTests/Application/AuctionOrderExtendedLifecycleTests.cs` verifying timeline tracking, pre-accept cancellation with refund, and shipment creation retry

**Checkpoint**: User Story 3 hoàn tất và có thể kiểm thử độc lập.

---

## Phase 6: User Story 4 - Quản trị Đơn hàng Tập trung cho Admin & Staff (Priority: P2)

**Goal**: Cung cấp API tra cứu toàn bộ đơn hàng cha và đơn hàng con trên toàn sàn cho Admin/Staff với bộ lọc đa tiêu chí và phân trang tại database.

**Independent Test**: Gọi `GET /api/v1/admin/orders` với role Admin $\rightarrow$ Nhận danh sách đơn hàng toàn sàn phân trang chuẩn `PagedData<OrderSummaryResponse>`.

### Implementation for User Story 4
- [X] T022 [US4] Declare and implement `ListAdminOrdersAsync` and `ListAdminSubOrdersAsync` in `Commerce.Application/Services/Order/IOrderService.cs` and `OrderService.cs`
- [X] T023 [US4] Add endpoints `GET /api/v1/admin/orders` and `GET /api/v1/admin/sub-orders` in `Commerce.Api/Controllers/Public/OrdersController.cs` with `[RequireRole("admin", "staff")]`
- [X] T024 [P] [US4] Add unit tests in `tests/Commerce.UnitTests/Application/AdminOrderQueryTests.cs` verifying role protection, query filters, and database pagination

**Checkpoint**: User Story 4 hoàn tất.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Cập nhật tài liệu hợp đồng API, kiểm chứng toàn diện và dọn dẹp working tree

- [X] T025 [P] Update `docs/COMMERCE_API_CONTRACT.md` documenting new post-order address change, refund governance, auction lifecycle, and admin endpoints
- [X] T026 Run full test suite verification with `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore` confirming 100% pass rate
- [X] T027 Execute `quickstart.md` validation checklist and ensure clean git working tree without temporary logs or build artifacts

---

## Dependencies & Execution Order

### Phase Dependencies
- **Setup (Phase 1)**: Kiểm tra baseline ban đầu
- **Foundational (Phase 2)**: Nền tảng DTOs (BLOCKS các phase sau)
- **User Stories (Phase 3 through Phase 6)**:
  - US1 (T007-T012): Phụ thuộc Phase 2
  - US2 (T013-T016): Phụ thuộc Phase 2
  - US3 (T017-T021): Phụ thuộc Phase 2
  - US4 (T022-T024): Phụ thuộc Phase 2
- **Polish (Phase 7)**: Hoàn tất sau khi tất cả user stories đạt trạng thái xanh
