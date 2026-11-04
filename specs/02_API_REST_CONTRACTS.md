# 02 — HỢP ĐỒNG GIAO TIẾP RESTFUL (COMMERCE SERVICE REST CONTRACTS)

> **Trạng thái triển khai (2026-11-01):** ✅ ĐÃ CHUẨN HÓA 100% RESTFUL — Toàn bộ giao tiếp giỏ hàng và đơn hàng qua RESTful Web API (`CartsController`, `OrdersController`). Bãi bỏ hoàn toàn gRPC, Saga phân tán và RabbitMQ.
> **Nguồn sự thật:** code (`Commerce.Api/Controllers/Public/CartsController.cs`, `Commerce.Api/Controllers/Public/OrdersController.cs`), tài liệu hợp đồng chi tiết `docs/COMMERCE_CART_API_CONTRACT.md` và `docs/COMMERCE_API_RECONCILIATION_REPORT.md`.
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

### 1.2. Đơn hàng & Thanh toán (`OrdersController` — `/api/v1/orders`, `/api/v1/checkouts`, `/api/v1/sub-orders`)
Chuẩn hóa 10 RESTful endpoints chính thức phục vụ luồng Quote-Based Checkout và Quản lý Đơn hàng:
* `POST /api/v1/checkouts/quote`: Tạo phiên xem trước báo giá (tính ship GHN, áp voucher, sinh `quoteId` TTL 15 phút) (`R75` / `CHK-01`).
* `POST /api/v1/orders`: Tạo đơn hàng chính thức từ `quoteId` (gồm Order cha và các SubOrder con theo Shop) (`R76` / `CHK-02`).
* `GET /api/v1/orders/{orderId}`: Tra cứu chi tiết đơn hàng cha (`OrderResponse`) (`R77` / `ORD-02`).
* `GET /api/v1/orders`: Danh sách lịch sử đơn hàng của người mua (phân trang chuẩn `PagedData<T>`) (`R78` / `ORD-01`).
* `GET /api/v1/sub-orders/{subOrderId}`: Tra cứu chi tiết đơn con của Shop (Single `SubOrderDetailResponse` có discount breakdown & địa chỉ JSONB) (`R79`).
* `POST /api/v1/orders/{orderId}/cancel`: Người mua hoặc hỗ trợ hủy đơn hàng khi chưa xác nhận (`R80` / `ORD-06`).
* `POST /api/v1/orders/{orderId}/accept`: Người bán xác nhận nhận đơn con (`SubOrderStatus.Confirmed`) (`R83` / `ORD-04`).
* `POST /api/v1/orders/{orderId}/reject`: Người bán từ chối đơn con (`SubOrderStatus.Cancelled` + `cancellationReason: "SellerRejected"`) (`R84` / `ORD-05`).
* `GET /api/v1/shipments/order/{orderId}`: Tra cứu tiến độ vận chuyển đơn hàng (`R87`).
* `POST /api/v1/sub-orders/{subOrderId}/refund`: Yêu cầu hoàn tiền đơn con (`R99` / `ORD-13`).

### 1.3. Hợp đồng Gọi ra Ngoài (Outbound Service Contract)
* `POST /api/v1/wallets/checkout/direct`: Endpoint thuộc quyền sở hữu của `WalletService` (Port 8008). CommerceService **không mở Controller** cho route này mà tương tác qua Typed Client `IWalletClient.CheckoutDirectPaymentAsync(...)` (`R181`).
* `POST /api/v1/fulfillment/inventory/reserve`: Giữ kho vật lý 15 phút tại `FulfillmentService` (Port 8005) qua `IFulfillmentClient.ReserveStockAsync(...)`.
* `GET /api/v1/catalog/skus/{skuId}`: Lấy giá niêm yết thời gian thực từ `CatalogService` (Port 8002) qua `ICatalogClient.GetSkuDetailsAsync(...)`.

---

## 2. NGUYÊN TẮC BẤT BIẾN

* **Envelope & Errors**: Controller trả về Pure DTO. API Gateway tự động bọc vỏ `ApiResponse<T>` hoặc `ApiResponse<PagedData<T>>`. Lỗi tuân thủ RFC 9110 ProblemDetails với `errorCode` dạng `SCREAMING_SNAKE_CASE`.
* **Casing**: DTO dùng PascalCase trong C# và camelCase trong JSON serialization. 100% Enum trả về PascalCase (ví dụ: `"PendingPayment"`, `"SellerRejected"`).
* **Trường tiền tệ**: Bắt buộc dùng `shippingFeeAmount`, cấm tuyệt đối `shippingFee`.
* **Typed REST Clients**: Tương tác với `WalletService` (:8008), `CatalogService` (:8002) và `FulfillmentService` (:8005) sử dụng typed HTTP clients (`IWalletClient`, `ICatalogClient`, `IFulfillmentClient`) thay vì Message Broker.
