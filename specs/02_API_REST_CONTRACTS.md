# 02 — HỢP ĐỒNG GIAO TIẾP RESTFUL (COMMERCE SERVICE REST CONTRACTS)

> **Trạng thái triển khai (2026-11-01):** ✅ ĐÃ CHUẨN HÓA 100% RESTFUL — Toàn bộ giao tiếp giỏ hàng và đơn hàng qua RESTful Web API (`CartsController`, `OrdersController`). Bãi bỏ hoàn toàn gRPC, Saga phân tán và RabbitMQ.
> **Nguồn sự thật:** code (`Commerce.Api/Controllers/Public/CartsController.cs`, `Commerce.Api/Controllers/Public/OrdersController.cs`), tài liệu hợp đồng chi tiết `docs/COMMERCE_CART_API_CONTRACT.md`.
> **Cập nhật lần cuối:** 2026-11-01.

---

## 1. DANH MỤC ENDPOINTS RESTFUL CHÍNH THỨC

### 1.1. Giỏ hàng (`CartsController` — `/api/v1/carts`)
Chuẩn hóa liền mạch theo trình tự nghiệp vụ người dùng (User Journey):
* `GET /api/v1/carts/me`: Lấy hoặc tự động khởi tạo giỏ hàng của chính người mua đang đăng nhập (`CART-01`).
* `GET /api/v1/carts/me/summary`: Lấy số lượng và tổng tạm tính hiển thị Badge Icon Header (`CART-02`).
* `GET /api/v1/carts/me/grouped`: Phân nhóm giỏ hàng theo từng Người bán/Shop (`CART-03`).
* `POST /api/v1/carts/me/items`: Thêm sản phẩm (SKU) vào giỏ hàng (`CART-04`).
* `PATCH /api/v1/carts/me/items/{cartItemId}`: Cập nhật số lượng của dòng sản phẩm (`CART-05`).
* `PATCH /api/v1/carts/me/items/{cartItemId}/variant`: Đổi phân loại hàng trực tiếp trong giỏ (`CART-06`).
* `DELETE /api/v1/carts/me/items/{cartItemId}`: Xóa một món khỏi giỏ hàng (`CART-07`).
* `POST /api/v1/carts/me/items:bulk-remove`: Xóa hàng loạt món được chọn (`CART-08`).
* `DELETE /api/v1/carts/me/clear`: Dọn sạch toàn bộ giỏ hàng (`CART-09`).
* `POST /api/v1/carts/me/calculate`: Tính tiền tạm tính các món được tick chọn — cơ chế Shopee Checkbox (`CART-10`).
* `POST /api/v1/carts/me/validate`: Kiểm tra tồn kho & biến động giá trước khi thanh toán (`CART-11`).
* `POST /api/v1/carts/me/merge`: Gộp giỏ hàng khách vãng lai khi đăng nhập (`CART-12`).
* `POST /api/v1/carts/me/cleanup-checked-out`: Dọn dẹp các món đã thanh toán thành công sau checkout (`CART-13`).
* `GET /api/v1/carts/active`: Tra cứu giỏ hàng đang hoạt động của người mua (`buyerId`) cho Admin/Staff (`CART-14`).

### 1.2. Đơn hàng (`OrdersController` — `/api/v1/orders`)
* `GET /api/v1/orders/{id}`: Tra cứu chi tiết đơn hàng theo mã định danh `id` (GUID).
* `GET /api/v1/orders`: Danh sách đơn hàng theo `buyerId`.

---

## 2. NGUYÊN TẮC BẤT BIẾN

* **Envelope & Errors**: Phản hồi bọc trong phong bì `ApiResponse<T>` hoặc `ApiResponse<PagedData<T>>`.
* **Casing**: DTO dùng PascalCase trong C# và camelCase trong JSON serialization.
* **Typed REST Clients**: Tương tác với `WalletService` và `CatalogService` sử dụng typed HTTP clients (`IWalletClient`, `ICatalogClient`) thay vì Message Broker.
