# Tasks: Cart Service (Quản lý Giỏ hàng)

**Feature**: `001-cart-service` | **Branch**: `feat/setup-commerce` | **Date**: 2026-10-29  
**Spec**: [specs/001-cart-service/spec.md](file:///d:/CommerceService/specs/001-cart-service/spec.md)  
**Plan**: [specs/001-cart-service/plan.md](file:///d:/CommerceService/specs/001-cart-service/plan.md)  
**Status**: Completed (15/15 tests pass)

---

## Phase 1: Setup & Foundational (Domain & Infrastructure)

**Purpose**: Thiết lập Domain Exception, Rich Domain Methods cho `Cart`, và DTOs dùng chung trước khi triển khai các User Story.

- [x] T001 Tạo Domain Exception `CommerceDomainException.cs` trong `Commerce.Domain/Exceptions/` hỗ trợ `ErrorCode` và `Message` (100% English).
- [x] T002 Bổ sung các phương thức Rich Domain vào `Commerce.Domain/Entities/Cart.cs` (`AddOrUpdateItem`, `UpdateItemQuantity`, `RemoveItem`, `ClearItems`, `RecalculateTotals`).
- [x] T003 Cập nhật DTOs trong `Commerce.Application/Services/Cart/CartDtos.cs` (`AddCartItemRequest`, `UpdateCartItemQuantityRequest`, `BulkRemoveCartItemsRequest`, `CartDto`, `CartItemDto`).
- [x] T004 Cập nhật interface `Commerce.Application/Services/Cart/ICartService.cs` đầy đủ 5 phương thức.

---

## Phase 2: User Story 1 - Xem Giỏ Hàng Của Tôi (Priority: P1) 🎯 MVP

**Goal**: Cho phép Buyer xem giỏ hàng `ACTIVE` hiện tại, tự động tạo giỏ nếu chưa có.

- [x] T005 [P] [US1] Viết Unit Test kiểm tra logic `RecalculateTotals` và tự động sinh giỏ hàng trong `tests/Commerce.UnitTests/Domain/CartDomainTests.cs`.
- [x] T006 [US1] Triển khai phương thức `GetOrCreateActiveCartAsync` trong `Commerce.Application/Services/Cart/CartService.cs`.
- [x] T007 [US1] Triển khai endpoint `GET /api/v1/carts/me` trong `Commerce.Api/Controllers/CartsController.cs` dùng `ICurrentUserService`.

---

## Phase 3: User Story 2 - Thêm Sản Phẩm Vào Giỏ (Priority: P1)

**Goal**: Thêm SKU vào giỏ, xác thực qua `ICatalogServiceClient`, cộng dồn số lượng nếu trùng SKU, chặn khi vượt giới hạn 50 items hoặc 99 quantity.

- [x] T008 [P] [US2] Viết Unit Test kiểm tra logic `AddOrUpdateItem`, cộng dồn số lượng, và ném lỗi khi vượt giới hạn trong `tests/Commerce.UnitTests/Domain/CartDomainTests.cs`.
- [x] T009 [US2] Triển khai phương thức `AddItemAsync` trong `Commerce.Application/Services/Cart/CartService.cs` tích hợp `ICatalogServiceClient`.
- [x] T010 [US2] Triển khai endpoint `POST /api/v1/carts/me/items` trong `Commerce.Api/Controllers/CartsController.cs`.

---

## Phase 4: User Story 3 - Cập Nhật Số Lượng Sản Phẩm (Priority: P1)

**Goal**: Cho phép cập nhật số lượng của một dòng sản phẩm trong giỏ, tự động tính lại tổng tiền.

- [x] T011 [P] [US3] Viết Unit Test kiểm tra logic `UpdateItemQuantity` trong `tests/Commerce.UnitTests/Domain/CartDomainTests.cs`.
- [x] T012 [US3] Triển khai phương thức `UpdateItemQuantityAsync` trong `Commerce.Application/Services/Cart/CartService.cs`.
- [x] T013 [US3] Triển khai endpoint `PATCH /api/v1/carts/me/items/{cartItemId}` trong `Commerce.Api/Controllers/CartsController.cs`.

---

## Phase 5: User Story 4 & 5 - Xóa Sản Phẩm & Làm Rỗng Giỏ (Priority: P2)

**Goal**: Cho phép xóa từng dòng, xóa nhiều dòng hoặc làm trống toàn bộ giỏ hàng.

- [x] T014 [P] [US4] Viết Unit Test kiểm tra logic `RemoveItem` và `ClearItems` trong `tests/Commerce.UnitTests/Domain/CartDomainTests.cs`.
- [x] T015 [US4] Triển khai phương thức `RemoveItemAsync` và `BulkRemoveOrClearAsync` trong `Commerce.Application/Services/Cart/CartService.cs`.
- [x] T016 [US4] Triển khai các endpoints `DELETE /api/v1/carts/me/items/{cartItemId}`, `POST /api/v1/carts/me/items:bulk-remove`, và `DELETE /api/v1/carts/me/clear` trong `Commerce.Api/Controllers/CartsController.cs`.

---

## Phase 6: Polish & Verification

**Purpose**: Xử lý ngoại lệ toàn cục RFC 9110 Problem Details và chạy kiểm thử tự động toàn diện.

- [x] T017 Thiết lập Global Exception Handling / Problem Details middleware trong `Commerce.Api/Program.cs` map `CommerceDomainException` sang HTTP 400/404/409.
- [x] T018 Bổ sung Service Integration Tests trong `tests/Commerce.UnitTests/Application/CartServiceTests.cs`.
- [x] T019 Kiểm chứng cuối cùng: `dotnet build Commerce.sln` 0 lỗi và `dotnet test Commerce.sln` 100% pass (15/15 tests passed).


---

## Phase 7: Advanced Cart Capabilities (Shopee Checkbox & Merge)

**Goal**: Triển khai các tính năng giỏ hàng nâng cao cho Storefront TMĐT hiện đại.

- [x] T020 Triển khai `CART-08` Badge Count & Tóm tắt giỏ `GET /api/v1/carts/me/summary`.
- [x] T021 Triển khai `CART-09` Tính toán tức thời các món được tick chọn `POST /api/v1/carts/me/calculate` (Cơ chế Shopee Checkbox).
- [x] T022 Triển khai `CART-06` Pre-checkout validation và kiểm tra giá/kho `POST /api/v1/carts/me/validate`.
- [x] T023 Triển khai `CART-10` Gộp giỏ hàng khách vãng lai khi đăng nhập `POST /api/v1/carts/me/merge`.
- [x] T024 Kiểm chứng toàn diện: `dotnet test` 100% pass (16/16 tests passed).


---

## Phase 8: Group by Seller, Variant Switching & Checkout Cleanup (CART-11, 12, 15)

**Goal**: Triển khai các tính năng phân nhóm Shop, đổi phân loại hàng, và dọn dẹp giỏ sau checkout.

- [x] T025 [P] [CART-12] Viết Unit Test kiểm tra Rich Domain method `ChangeItemVariant` trong `tests/Commerce.UnitTests/Domain/CartDomainTests.cs` (cập nhật SKU mới, cộng dồn khi trùng SKU, ném lỗi khi không tìm thấy item).
- [x] T026 [CART-12] Triển khai phương thức Rich Domain `ChangeItemVariant` trong `Commerce.Domain/Entities/Cart.cs`.
- [x] T027 [CART-11, 12, 15] Thêm DTOs vào `Commerce.Application/Services/Cart/CartRequests.cs` (`ChangeCartItemVariantRequest`, `CleanupCheckedOutItemsRequest`) và `Commerce.Application/Services/Cart/CartResponses.cs` (`ShopCartGroupResponse`, `GroupedCartResponse`).
- [x] T028 [CART-11, 12, 15] Khai báo 3 phương thức mới trong interface `Commerce.Application/Services/Cart/ICartService.cs`.
- [x] T029 [P] [CART-11, 15] Viết Unit Test cho Service methods trong `tests/Commerce.UnitTests/Application/CartServiceTests.cs` (`GetGroupedCartAsync`, `CleanupCheckedOutItemsAsync`, `ChangeItemVariantAsync`).
- [x] T030 [CART-11] Triển khai phương thức `GetGroupedCartAsync` trong `Commerce.Application/Services/Cart/CartService.cs` gom nhóm theo `SellerId`.
- [x] T031 [CART-12] Triển khai phương thức `ChangeItemVariantAsync` trong `Commerce.Application/Services/Cart/CartService.cs` tích hợp `_catalogClient.GetSkuDetailsAsync`.
- [x] T032 [CART-15] Triển khai phương thức `CleanupCheckedOutItemsAsync` trong `Commerce.Application/Services/Cart/CartService.cs` xóa sạch các món đã mua thành công.
- [x] T033 [CART-11, 12, 15] Triển khai 3 endpoints mới trong `Commerce.Api/Controllers/Public/CartsController.cs` (`GET /me/grouped`, `PATCH /me/items/{cartItemId}/variant`, `POST /me/cleanup-checked-out`).
- [x] T034 [Tooling] Cập nhật file kịch bản `Commerce.Api/Commerce.Api.http` với các request mẫu cho 3 tính năng mới.
- [x] T035 [Verification] Kiểm chứng toàn diện: `dotnet build Commerce.sln` 0 lỗi và `dotnet test` 100% pass (21/21 tests passed).
