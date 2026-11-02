# COMMERCE SERVICE — MASTER REST API CONTRACT & INTEGRATION SPECIFICATION
## (Hợp đồng Giao tiếp RESTful Toàn diện: Giỏ hàng, Báo giá, Đơn hàng & Vận chuyển)

> **Ghi chú điều hướng:** Tài liệu này là phiên bản mở rộng toàn diện của Giỏ hàng & Đơn hàng (v3.0.0).  
> **Master File:** Xem bản gốc tại [`docs/COMMERCE_API_CONTRACT.md`](COMMERCE_API_CONTRACT.md).  
> **Cổng dịch vụ (Port):** `8003` | **Database:** `5435` (`commerce_db`)  

---

## MỤC LỤC
1. [Phần 1: Quy chuẩn Kiến trúc & Giao thức Chung](#phần-1-quy-chuẩn-kiến-trúc--giao-thức-chung)
2. [Phần 2: Bounded Context 1 — Quản lý Giỏ hàng (`CART-01` .. `CART-14`)](#phần-2-bounded-context-1--quản-lý-giỏ-hàng-cart-01--cart-14)
3. [Phần 3: Bounded Context 2 — Báo giá & Đặt hàng 2 Bước (`CHK-01` .. `CHK-02`)](#phần-3-bounded-context-2--báo-giá--đặt-hàng-2-bước-chk-01--chk-02)
4. [Phần 4: Bounded Context 3 — Vòng đời Đơn hàng & Vận chuyển (`ORD-01` .. `ORD-13`)](#phần-4-bounded-context-3--vòng-đời-đơn-hàng--vận-chuyển-ord-01--ord-13)
5. [Phần 5: Hợp đồng Gọi Liên dịch vụ (Outbound Contract `R181`)](#phần-5-hợp-đồng-gọi-liên-dịch-vụ-outbound-contract-r181)
6. [Phần 6: Bảng Tra cứu Mã Lỗi Hệ thống Chuẩn RFC 9110](#phần-6-bảng-tra-cứu-mã-lỗi-hệ-thống-chuẩn-rfc-9110)
7. [Phần 7: Bộ TypeScript Type Definitions Hoàn chỉnh](#phần-7-bộ-typescript-type-definitions-hoàn-chỉnh)

---

# PHẦN 1: QUY CHUẨN KIẾN TRÚC & GIAO THỨC CHUNG
* **Pure DTO**: Controllers nội bộ chỉ trả về Pure DTOs, API Gateway bọc ngoài `ApiResponse<T>`.
* **Casing**: Mã lỗi `SCREAMING_SNAKE_CASE`, Enum dữ liệu `PascalCase`, JSON properties `camelCase`.
* **Tiền tệ**: Bắt buộc `shippingFeeAmount`, cấm `shippingFee`.

---

# PHẦN 2: BOUNDED CONTEXT 1 — QUẢN LÝ GIỎ HÀNG (`CART-01` .. `CART-14`)
* `GET /api/v1/carts/me`: Lấy hoặc tự động tạo giỏ hàng người mua (`CART-01`).
* `GET /api/v1/carts/me/summary`: Badge số lượng & tổng tiền tạm tính cho Header (`CART-02`).
* `GET /api/v1/carts/me/grouped`: Giỏ hàng gom nhóm theo Shop (`CART-03`).
* `POST /api/v1/carts/me/items`: Thêm sản phẩm (SKU) vào giỏ hàng (`CART-04`).
* `PATCH /api/v1/carts/me/items/{cartItemId}`: Cập nhật số lượng của dòng sản phẩm (`CART-05`).
* `PATCH /api/v1/carts/me/items/{cartItemId}/variant`: Đổi phân loại hàng trực tiếp trong giỏ (`CART-06`).
* `DELETE /api/v1/carts/me/items/{cartItemId}`: Xóa một món khỏi giỏ hàng (`CART-07`).
* `POST /api/v1/carts/me/items:bulk-remove`: Xóa hàng loạt món được tick chọn (`CART-08`).
* `DELETE /api/v1/carts/me/clear`: Dọn sạch toàn bộ giỏ hàng (`CART-09`).
* `POST /api/v1/carts/me/calculate`: Tính tiền tạm tính các món tick chọn — Shopee Checkbox (`CART-10`).
* `POST /api/v1/carts/me/validate`: Kiểm tra tồn kho & biến động giá trước checkout (`CART-11`).
* `POST /api/v1/carts/me/merge`: Gộp giỏ khách vãng lai khi đăng nhập (`CART-12`).
* `POST /api/v1/carts/me/cleanup-checked-out`: Xóa món đã mua sau khi thanh toán thành công (`CART-13`).
* `GET /api/v1/carts/active?buyerId={guid}`: Quản trị tra cứu giỏ hàng đang hoạt động (`CART-14`).

---

# PHẦN 3: BOUNDED CONTEXT 2 — BÁO GIÁ & ĐẶT HÀNG (`CHK-01` .. `CHK-02`)
* `POST /api/v1/checkouts/quote` (`CHK-01` / `R75`): Xem trước báo giá giỏ hàng đa Shop, phí ship GHN, voucher, sinh `quoteId` (15m TTL).
* `POST /api/v1/orders` (`CHK-02` / `R76`): Tạo đơn hàng chính thức từ `quoteId`, thanh toán ví/vnpay, dọn giỏ hàng.
* **Địa chỉ Snapshot**: Lưu trữ dưới dạng PostgreSQL `jsonb` qua EF Core 8 `.ToJson()`.

---

# PHẦN 4: BOUNDED CONTEXT 3 — VÒNG ĐỜI ĐƠN HÀNG & VẬN CHUYỂN (`ORD-01` .. `ORD-13`)
* `GET /api/v1/orders` (`ORD-01` / `R78`): Danh sách lịch sử đơn hàng của tôi (phân trang database).
* `GET /api/v1/orders/{orderId}` (`ORD-02` / `R77`): Chi tiết đơn hàng cha.
* `GET /api/v1/sub-orders/{subOrderId}` (`ORD-02B` / `R79`): Chi tiết đơn con của Shop (Single DTO đầy đủ breakdown giảm giá).
* `POST /api/v1/orders/{orderId}/accept` (`ORD-04` / `R83`): Shop tiếp nhận đơn con (`Confirmed`).
* `POST /api/v1/orders/{orderId}/reject` (`ORD-05` / `R84`): Shop từ chối đơn con (`Cancelled` + `SellerRejected`).
* `POST /api/v1/orders/{orderId}/cancel` (`ORD-06` / `R80`): Buyer hủy đơn `PendingPayment`.
* `GET /api/v1/shipments/order/{orderId}` (`ORD-08` / `R87`): Theo dõi tiến độ vận chuyển đơn hàng.
* `POST /api/v1/sub-orders/{subOrderId}/refund` (`ORD-13` / `R99`): Hoàn tiền đơn con.

---

> Chi tiết Request/Response payloads và TypeScript interface đầy đủ, vui lòng xem tại: [COMMERCE_API_CONTRACT.md](COMMERCE_API_CONTRACT.md).
