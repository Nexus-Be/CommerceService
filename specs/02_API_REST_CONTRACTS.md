# 02 — HỢP ĐỒNG GIAO TIẾP RESTFUL (COMMERCE SERVICE REST CONTRACTS)

> **Trạng thái triển khai (2026-10-30):** ✅ ĐÃ CHUẨN HÓA 100% RESTFUL — Toàn bộ giao tiếp giỏ hàng và đơn hàng qua RESTful Web API (`CartsController`, `OrdersController`). Bãi bỏ hoàn toàn gRPC, Saga phân tán và RabbitMQ.
> **Nguồn sự thật:** code (`Commerce.Api/Controllers/CartsController.cs`, `Commerce.Api/Controllers/OrdersController.cs`).
> **Cập nhật lần cuối:** 2026-10-30.

---

## 1. DANH MỤC ENDPOINTS RESTFUL CHÍNH THỨC

### 1.1. Giỏ hàng (`CartsController` — `/api/v1/carts`)
* `GET /api/v1/carts/active`: Lấy giỏ hàng đang hoạt động của người mua (`buyerId`).

### 1.2. Đơn hàng (`OrdersController` — `/api/v1/orders`)
* `GET /api/v1/orders/{id}`: Tra cứu chi tiết đơn hàng theo mã định danh `id` (GUID).
* `GET /api/v1/orders`: Danh sách đơn hàng theo `buyerId`.

---

## 2. NGUYÊN TẮC BẤT BIẾN

* **Envelope & Errors**: Phản hồi bọc trong phong bì `ApiResponse<T>` hoặc `ApiResponse<PagedData<T>>`.
* **Casing**: DTO dùng PascalCase trong C# và camelCase trong JSON serialization.
* **Typed REST Clients**: Tương tác với `WalletService` và `CatalogService` sử dụng typed HTTP clients (`IWalletClient`, `ICatalogClient`) thay vì Message Broker.
