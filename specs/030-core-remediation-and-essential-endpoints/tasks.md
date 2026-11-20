# Tasks: Core Remediation, Security Hardening & Essential Endpoints

**Input**: Design documents from `/specs/030-core-remediation-and-essential-endpoints/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

## Format: `[ID] [P?] [Story] Description with exact file path`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (`US1`, `US2`, `US3`, `US4`, `US5`, `US6`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Xác thực môi trường và baseline ban đầu

- [X] T001 Verify project dependencies and baseline build with `dotnet build Commerce.sln -c Release --no-restore`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Kiểm tra baseline test suite để sẵn sàng triển khai các User Story

**⚠️ CRITICAL**: Xác nhận toàn bộ 346 test case hiện tại đạt trạng thái Passed trước khi thay đổi mã nguồn

- [X] T002 Verify baseline unit test execution with `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore`

**Checkpoint**: Foundation ready - user story implementation can now begin

---

## Phase 3: User Story 1 - Tự động hủy vận đơn GHN khi hủy đơn hàng mẹ (Priority: P1) 🎯 MVP

**Goal**: Khi người mua hoặc người bán hủy đơn hàng mẹ (`CancelOrderAsync`), các đơn con đã ở trạng thái `Confirmed` có mã vận đơn GHN (`TrackingCode`/`CarrierCode`) sẽ tự động được gửi lệnh hủy sang `FulfillmentService` trước khi hoàn tiền ví và nhả kho.

**Independent Test**: Tạo đơn hàng mẹ có SubOrder đã `Confirmed` kèm `TrackingCode`. Khách gọi hủy đơn mẹ $\rightarrow$ `_fulfillmentClient.CancelShipmentAsync` được gọi đúng `subOrder.Id`, đơn chuyển sang `Cancelled` và ví được hoàn tiền đầy đủ.

### Tests for User Story 1
- [X] T003 [P] [US1] Add unit test in `tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs` verifying that `CancelOrderAsync` invokes `_fulfillmentClient.CancelShipmentAsync` for sub-orders with non-empty tracking codes

### Implementation for User Story 1
- [X] T004 [US1] Update `OrderService.CancelOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` to iterate over target sub-orders, check for `TrackingCode` or `CarrierCode`, and invoke `_fulfillmentClient.CancelShipmentAsync(so.Id, reason, ct)` with fail-safe try-catch logging

**Checkpoint**: User Story 1 hoàn tất và có thể kiểm thử độc lập. Hủy đơn mẹ không còn để sót bưu gửi trên GHN.

---

## Phase 4: User Story 2 - Khóa chặt Snapshot địa chỉ GHN khi tạo đơn (Priority: P1)

**Goal**: Đảm bảo `CreateOrderAsync` sử dụng nguyên vẹn thông tin địa chỉ giao hàng và mã vùng GHN (`GhnDistrictId`, `GhnWardCode`) từ báo giá đã được khóa trong `cachedQuote.Request.ShippingAddress`, ngăn chặn lỗi cước và giao sai địa chỉ do client gửi chuỗi địa chỉ văn bản tự do.

**Independent Test**: Gọi Báo giá với địa chỉ hợp lệ. Khi gọi `POST /api/v1/orders`, truyền `request.ShippingAddress` chuỗi văn bản khác $\rightarrow$ Kiểm tra `SubOrder.ShippingAddress` lưu đúng địa chỉ và mã vùng từ `cachedQuote`.

### Tests for User Story 2
- [X] T005 [P] [US2] Add unit test in `tests/Commerce.UnitTests/Application/OrderServiceTests.cs` asserting that `CreateOrderAsync` retains `cachedQuote.Request.ShippingAddress.FullAddressText` and GHN district/ward identifiers

### Implementation for User Story 2
- [X] T006 [US2] Update `OrderService.CreateOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` to resolve `FullAddressText` from `cachedQuote.Request.ShippingAddress.FullAddressText` (falling back to `request.ShippingAddress` only if empty) when constructing `ShippingAddressSnapshot`

**Checkpoint**: User Story 2 hoàn tất và bảo vệ tính toàn vẹn cước phí GHN.

---

## Phase 5: User Story 3 - API Danh sách Đơn đấu giá cho Người mua (Priority: P1)

**Goal**: Cung cấp API `GET /api/v1/auction-orders/me` cho phép người mua thắng đấu giá xem danh sách các đơn hàng đấu giá của mình, hỗ trợ phân trang và lọc theo trạng thái.

**Independent Test**: Đăng nhập tài khoản Buyer, gọi `GET /api/v1/auction-orders/me?page=1&pageSize=20` $\rightarrow$ Trả về `200 OK` với danh sách đơn chỉ thuộc về `BuyerId == currentUserId`, kèm `depositAmount` và `pagination`.

### Tests for User Story 3
- [X] T007 [P] [US3] Add unit test in `tests/Commerce.UnitTests/Application/AuctionOrderServiceTests.cs` verifying `ListBuyerOrdersAsync` filters by `BuyerId` and `status` with correct `PagedData` pagination metadata

### Implementation for User Story 3
- [X] T008 [US3] Declare `ListBuyerOrdersAsync` method returning `Task<PagedData<AuctionOrderSummaryResponse>>` in `Commerce.Application/Services/AuctionOrder/IAuctionOrderService.cs`
- [X] T009 [US3] Implement `ListBuyerOrdersAsync` in `Commerce.Application/Services/AuctionOrder/AuctionOrderService.cs` querying `_dbContext.AuctionOrders` with `BuyerId` filter, optional `status` filter, `CreatedAt DESC` sorting, and pagination mapping via `MapToSummary`
- [X] T010 [US3] Add `[HttpGet("me")]` action with `[RequireRole("buyer", "admin", "staff")]` in `Commerce.Api/Controllers/Public/AuctionOrdersController.cs` calling `_auctionOrderService.ListBuyerOrdersAsync` with `TryGetUserId()`

**Checkpoint**: User Story 3 hoàn tất. Người mua có thể tra cứu đơn đấu giá cá nhân.

---

## Phase 6: User Story 4 - Vá bảo mật IDOR & Kiểm tra giới hạn dùng Voucher (Priority: P1)

**Goal**: Ngăn chặn người dùng mượn `cartItemId` của người khác để kiểm tra voucher (IDOR), và từ chối voucher nếu đã hết tổng lượt dùng (`MaxUsesTotal`) hoặc người mua đã dùng hết lượt (`MaxUsesPerUser`).

**Independent Test**: Gọi `ValidateVoucher` với `cartItemId` thuộc giỏ hàng của tài khoản khác $\rightarrow$ Trả về HTTP 400 `VAL_INVALID_INPUT`. Gọi voucher đã chạm ngưỡng `MaxUsesTotal` $\rightarrow$ Trả về `isValid: false`, lý do `VOUCHER_OUT_OF_STOCK`.

### Tests for User Story 4
- [X] T011 [P] [US4] Add unit tests in `tests/Commerce.UnitTests/DiscountServiceTests.cs` verifying IDOR isolation for cart items and enforcement of `MaxUsesTotal` and `MaxUsesPerUser` constraints

### Implementation for User Story 4
- [X] T012 [US4] Update `GetAvailableVouchersAsync`, `ValidateVoucherAsync`, and `RecommendVouchersAsync` in `Commerce.Application/Services/Discount/DiscountService.cs` to filter `ci.Cart.BuyerId == buyerId && !ci.IsDeleted`
- [X] T013 [US4] Update `ValidateVoucherAsync` in `Commerce.Application/Services/Discount/DiscountService.cs` to check `voucher.MaxUsesTotal` (returning `VOUCHER_OUT_OF_STOCK`) and count non-released records in `_db.DiscountCodeUsages` against `voucher.MaxUsesPerUser` (returning `VOUCHER_USAGE_LIMIT_EXCEEDED`)

**Checkpoint**: User Story 4 hoàn tất. Khóa chặt bảo mật giỏ hàng và ngân sách voucher.

---

## Phase 7: User Story 5 - Bổ sung EF Core Database Migration cho IsEscrowSettled (Priority: P1)

**Goal**: Tạo migration EF Core bổ sung cột `IsEscrowSettled` vào bảng `SubOrders`, đồng bộ hoàn toàn giữa Database, Entity và ModelSnapshot.

**Independent Test**: Chạy `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~MigrationConsistencyTests"` $\rightarrow$ Test `Snapshot_MatchesCurrentModel` đạt trạng thái Passed 100%.

### Implementation for User Story 5
- [X] T014 [US5] Create EF Core migration file `Commerce.Infrastructure/Persistence/Migrations/20261118010000_AddSubOrderIsEscrowSettled.cs` adding `IsEscrowSettled` boolean column (default false) to `SubOrders`
- [X] T015 [US5] Create EF Core migration designer file `Commerce.Infrastructure/Persistence/Migrations/20261118010000_AddSubOrderIsEscrowSettled.Designer.cs` targeting `AppDbContext`
- [X] T016 [P] [US5] Create unit test in `tests/Commerce.UnitTests/MigrationConsistencyTests.cs` using `IMigrationsModelDiffer` and `IMigrator.GenerateScript` to assert `AppDbContextModelSnapshot` matches current EF Core model without pending model diffs

**Checkpoint**: User Story 5 hoàn tất. Cơ sở dữ liệu EF Core đạt tính nhất quán 100%.

---

## Phase 8: User Story 6 - Phân quyền Admin / Staff tra cứu chi tiết đơn hàng (Priority: P2)

**Goal**: Cho phép tài khoản vai trò `admin` hoặc `staff` xem chi tiết đơn hàng, đơn con và lộ trình vận đơn của bất kỳ người dùng nào trên sàn để hỗ trợ vận hành và chăm sóc khách hàng.

**Independent Test**: Đăng nhập tài khoản role `admin` hoặc `staff`, gọi `GET /api/v1/orders/{orderId}` của một khách hàng khác $\rightarrow$ Trả về `200 OK` thay vì `403 FORBIDDEN`.

### Tests for User Story 6
- [X] T017 [P] [US6] Add unit tests in `tests/Commerce.UnitTests/Application/OrderServiceTests.cs` verifying Admin/Staff role bypasses ownership check on `GetOrderDetailAsync`, `GetSubOrderDetailAsync`, `GetShipmentTrackingAsync`, and `GetShipmentTimelineAsync`

### Implementation for User Story 6
- [X] T018 [US6] Add optional `bool isAdminOrStaff = false` parameter to `GetOrderDetailAsync`, `GetSubOrderDetailAsync`, `GetShipmentTrackingAsync`, and `GetShipmentTimelineAsync` in `Commerce.Application/Services/Order/IOrderService.cs` and `Commerce.Application/Services/Order/OrderService.cs`
- [X] T019 [US6] Update `Commerce.Api/Controllers/Public/OrdersController.cs` to pass `this.IsAdmin() || this.HasRole("staff")` to order inspection service calls

**Checkpoint**: User Story 6 hoàn tất. Admin và Staff có quyền truy xuất thông tin vận hành.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Đồng bộ tài liệu hợp đồng API, kiểm chứng tổng thể và dọn dẹp working tree

- [X] T020 [P] Update `docs/COMMERCE_API_CONTRACT.md` documenting `GET /api/v1/auction-orders/me` and voucher validation error codes
- [X] T021 Run full test suite verification with `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore`
- [X] T022 Execute `quickstart.md` validation checklist and ensure clean git working tree without temporary logs or build artifacts

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 - confirms baseline before code changes
- **User Stories (Phase 3 through Phase 8)**: All depend on Foundational phase completion
  - Stories can proceed sequentially in priority order: US1 (MVP) → US2 → US3 → US4 → US5 → US6
  - Or in parallel across different domains (e.g., US1 in OrderService, US3 in AuctionOrderService, US4 in DiscountService, US5 in Migrations)
- **Polish (Phase 9)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Modifies `OrderService.CancelOrderAsync` - independent of other stories
- **User Story 2 (P1)**: Modifies `OrderService.CreateOrderAsync` - independent of other stories
- **User Story 3 (P1)**: Touches `AuctionOrderService` and `AuctionOrdersController` - independent of other stories
- **User Story 4 (P1)**: Touches `DiscountService` - independent of other stories
- **User Story 5 (P1)**: Touches EF Core migrations - independent of other stories
- **User Story 6 (P2)**: Modifies `OrderService` read methods - can follow US1/US2

---

## Parallel Execution Opportunities

```bash
# Nhóm 1: Triển khai song song các Unit Test độc lập
Task T003: "Unit test for CancelOrderAsync in tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs"
Task T005: "Unit test for CreateOrderAsync in tests/Commerce.UnitTests/Application/OrderServiceTests.cs"
Task T007: "Unit test for ListBuyerOrdersAsync in tests/Commerce.UnitTests/Application/AuctionOrderServiceTests.cs"
Task T011: "Unit test for IDOR and voucher limits in tests/Commerce.UnitTests/DiscountServiceTests.cs"

# Nhóm 2: Triển khai song song các Service khác nhau
Task T008-T010: "AuctionOrderService and AuctionOrdersController (US3)"
Task T012-T013: "DiscountService IDOR and limits (US4)"
Task T014-T015: "EF Core Migration AddSubOrderIsEscrowSettled (US5)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)
1. Hoàn thành Phase 1 & 2 (Baseline verification).
2. Triển khai Phase 3: User Story 1 (Hủy vận đơn GHN khi hủy đơn mẹ).
3. **STOP & VALIDATE**: Chạy `OrderLifecycleTests` để nghiệm thu MVP 1 ngay lập tức.

### Incremental Delivery
1. Baseline verified $\rightarrow$ Deliver US1 (GHN Courier Safety).
2. Deliver US2 (Address Snapshot Integrity).
3. Deliver US3 (Buyer Auction Orders List API).
4. Deliver US4 (Voucher IDOR & Budget Protection).
5. Deliver US5 (EF Core Migration Parity).
6. Deliver US6 (Admin/Staff RBAC Query Access).
7. Execute Polish & Final Test Suite $\rightarrow$ Ready for Production / PR.
