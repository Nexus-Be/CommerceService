# Implementation Plan: Cart Service (Quản lý Giỏ hàng)

**Feature**: `001-cart-service` | **Branch**: `feat/cart-service-v2` | **Date**: 2026-10-29 (Updated: 2026-10-31)  
**Spec**: [specs/001-cart-service/spec.md](file:///d:/CommerceService/specs/001-cart-service/spec.md)  
**Input**: Triển khai trọn bộ REST API Giỏ hàng: Phase 1 (`CART-01` đến `CART-05`) và Phase 2 (`CART-08`, `CART-09`, `CART-06`, `CART-10`) theo `api_catalog.md` và `specs/02_API_REST_CONTRACTS.md`.

---

## 1. Summary
Triển khai module Giỏ hàng (Cart Service) hoàn chỉnh theo Clean Architecture 4 tầng:
1. **Domain Layer**:
   * Thực thể `Cart.cs`: Bổ sung các phương thức nghiệp vụ và bảo vệ bất biến bên trong `Cart.cs` (`AddOrUpdateItem`, `UpdateItemQuantity`, `RemoveItem`, `ClearItems`, `BulkRemoveItems`, `RecalculateTotals`, kiểm soát `RowVersion`).
   * Ràng buộc: `MAX_CART_ITEMS = 50`, `MAX_QTY_PER_ITEM = 99`.
   * Ngoại lệ: `CommerceDomainException.cs` kế thừa `DomainException` của team.
2. **Application Layer**:
   * Phân tách DTO chuẩn Feature Co-location: `CartRequests.cs`, `CartResponses.cs`, `CartDtos.cs`.
   * Interface `ICartService.cs` & `CartService.cs`: Tương tác trực tiếp với `ICommerceDbContext` (tuân thủ **Nguyên tắc 4: Cấm dùng Repository Pattern**), đồng thời tích hợp `ICatalogClient` để xác thực SKU & giá sản phẩm snapshot.
   * Xử lý lỗi chuẩn RFC 9110 Problem Details (100% tiếng Anh, Sentence case, mã lỗi `SCREAMING_SNAKE_CASE`).
3. **API Layer**:
   * Controller chuẩn hóa tại `Commerce.Api/Controllers/Public/CartsController.cs`:
     * `CART-01`: `GET /api/v1/carts/me`
     * `CART-02`: `POST /api/v1/carts/me/items`
     * `CART-03`: `PATCH /api/v1/carts/me/items/{cartItemId}`
     * `CART-04`: `DELETE /api/v1/carts/me/items/{cartItemId}`
     * `CART-05`: `POST /api/v1/carts/me/items:bulk-remove` & `DELETE /api/v1/carts/me/clear`
     * `CART-08`: `GET /api/v1/carts/me/summary` (Badge count & tóm tắt nhanh)
     * `CART-09`: `POST /api/v1/carts/me/calculate` (Shopee Checkbox: tính tạm tính các món được tick)
     * `CART-06`: `POST /api/v1/carts/me/validate` (Pre-checkout validation: kiểm tra tồn kho & biến động giá)
     * `CART-10`: `POST /api/v1/carts/me/merge` (Gộp giỏ hàng khách vãng lai khi đăng nhập)
     * Team Endpoint: `GET /api/v1/carts/active?buyerId=...` (Tra cứu giỏ cho Admin/Staff)
   * Bảo mật: `[RequireRole("buyer", "admin", "staff")]`, trích xuất `currentUserId` an toàn qua `this.TryGetUserId()` và `this.EnsureOwnership()`.
4. **Testing Layer**:
   * Đưa dự án kiểm thử `tests/Commerce.UnitTests` vào `Commerce.sln`.
   * Viết bộ Unit Tests toàn diện cho Domain logic (`CartDomainTests.cs`), Financial Invariants (`FinancialInvariantTests.cs`), Idempotency Key (`IdempotencyKeyTests.cs`), và Application Services (`CartServiceTests.cs`).

---

## 2. Technical Context

* **Ngôn ngữ & Runtime**: C# 12 / .NET 8.0 (hỗ trợ `<RollForward>Major</RollForward>`).
* **Framework Web & ORM**: ASP.NET Core 8 Web API, Entity Framework Core 8.0.10, Npgsql 8.0.10, `EFCore.NamingConventions` 8.0.3.
* **Cơ sở dữ liệu**: Neon Serverless PostgreSQL (`commerce_db`), 21 bảng định dạng `snake_case`.
* **Testing Stack**: xUnit 2.6.2, FluentAssertions 6.12.0, Moq 4.20.70.
* **Tiêu chuẩn mã lỗi**: 100% English, Sentence case message, mã lỗi `CART_NOT_FOUND`, `CART_ITEM_NOT_FOUND`, `CART_LIMIT_EXCEEDED`, `CART_ITEM_LIMIT_EXCEEDED`, `CATALOG_SKU_NOT_FOUND`, `VAL_INVALID_INPUT`.

---

## 3. Constitution & Architecture Guardrails Check

* [x] **Nguyên tắc 1 (Plan-First Rule)**: Lập và trình bày chi tiết kế hoạch thực hiện trước khi viết code.
* [x] **Nguyên tắc 2 (Inward-Only)**: Tuyệt đối chỉ tham chiếu nội bộ `CommerceService`, không import code ngoài root.
* [x] **Nguyên tắc 4 (No Repository Pattern)**: Sử dụng trực tiếp `ICommerceDbContext` và `DbSet<Cart>`, `DbSet<CartItem>`. Cấm tạo Repository hay UnitOfWork.
* [x] **Nguyên tắc 4 (REST Only - No gRPC)**: Toàn bộ endpoints đều dùng RESTful Web API (JSON qua HTTP).
* [x] **Nguyên tắc 5 (Mandatory Error Rules)**: 100% tiếng Anh, Sentence case, mã lỗi `SCREAMING_SNAKE_CASE`.

---

## 4. Detailed Component Design

### 4.1 Domain Layer (`Commerce.Domain/Entities/Cart.cs`)
Thực thể `Cart` tự bảo vệ tính toàn vẹn (Rich Domain Model):
* `AddOrUpdateItem`: Kiểm tra số lượng hợp lệ, giới hạn 99 cái/SKU, giới hạn 50 dòng/giỏ, cộng dồn số lượng nếu trùng SKU.
* `UpdateItemQuantity`: Cập nhật số lượng item có sẵn, tự động tính lại tổng tiền.
* `RemoveItem`: Xóa item khỏi giỏ, tự động trừ tiền.
* `ClearItems` / `BulkRemoveItems`: Xóa sạch hoặc xóa hàng loạt items.
* `RecalculateTotals`: Cập nhật `ItemCount = Items.Count`, `SubtotalAmount = Sum(Qty * Price)`, tăng `RowVersion`.

### 4.2 Application Layer (`Commerce.Application/Services/Cart/`)
* **Request DTOs (`CartRequests.cs`)**:
  * `AddCartItemRequest(Guid SkuId, int Quantity, Guid? ProductId = null)`
  * `UpdateCartItemQuantityRequest(Guid CartItemId, int Quantity)`
  * `BulkRemoveCartItemsRequest(List<Guid>? CartItemIds, bool ClearAll = false)`
  * `CalculateSelectedCartItemsRequest(List<Guid> SelectedItemIds)`
  * `ValidateCartRequest(List<Guid>? SelectedItemIds = null)`
  * `MergeGuestCartRequest(List<GuestCartItem> GuestItems)`
* **Response DTOs (`CartResponses.cs`)**:
  * `CartResponse`, `CartItemResponse`
  * `CartSummaryResponse(Guid CartId, int TotalDistinctItems, int TotalQuantity, decimal EstimatedSubtotal)`
  * `CalculateSelectedCartResponse(int SelectedItemCount, int TotalQuantity, decimal SubtotalAmount, IReadOnlyList<CalculatedCartItem> Items)`
  * `ValidateCartResponse(bool IsValid, bool HasPriceChanges, bool HasStockIssues, IReadOnlyList<CartItemIssue> Issues)`
* **Typed Client (`ICatalogClient`)**:
  * `GetSkuDetailsAsync(Guid skuId, ...)` hỗ trợ tra cứu giá và tồn kho với cơ chế fallback mock thông minh.

### 4.3 API Layer (`Commerce.Api/Controllers/Public/CartsController.cs`)
Tất cả các action inject duy nhất `ICartService`, trả về pure DTOs (không bọc ResultFilter thủ công):
* `GET /api/v1/carts/me`: Lấy hoặc tự tạo giỏ hàng người mua.
* `POST /api/v1/carts/me/items`: Thêm sản phẩm vào giỏ.
* `PATCH /api/v1/carts/me/items/{cartItemId}`: Sửa số lượng.
* `DELETE /api/v1/carts/me/items/{cartItemId}`: Xóa sản phẩm.
* `POST /api/v1/carts/me/items:bulk-remove`: Xóa hàng loạt.
* `DELETE /api/v1/carts/me/clear`: Dọn sạch giỏ.
* `GET /api/v1/carts/me/summary`: Tóm tắt badge count.
* `POST /api/v1/carts/me/calculate`: Tính tiền các món được tick.
* `POST /api/v1/carts/me/validate`: Kiểm tra trước checkout.
* `POST /api/v1/carts/me/merge`: Gộp giỏ khách vãng lai.
* `GET /api/v1/carts/active`: Tra cứu giỏ người mua (Admin/Staff).

---

## 5. Verification Plan

1. **Unit Testing (`tests/Commerce.UnitTests`)**:
   * `CartDomainTests.cs`: 9 unit tests kiểm tra toàn bộ rich domain logic.
   * `CartServiceTests.cs`: 4 unit tests kiểm tra các trường hợp nghiệp vụ của service.
   * `FinancialInvariantTests.cs`: 3 unit tests kiểm tra bất biến tài chính của SubOrder.
   * Tổng cộng 16/16 tests pass 100%.
2. **Build Verification**:
   * `dotnet build Commerce.sln -c Release` $\rightarrow$ 0 lỗi.
3. **Manual Verification**:
   * File kịch bản `Commerce.Api/Commerce.Api.http` và Swagger UI sẵn sàng kiểm thử.
