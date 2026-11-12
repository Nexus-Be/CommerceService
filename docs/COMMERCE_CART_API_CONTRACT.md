# COMMERCE SERVICE — MASTER REST API CONTRACT & INTEGRATION SPECIFICATION
## (Hợp đồng Giao tiếp RESTful Toàn diện: Giỏ hàng, Báo giá, Đơn hàng & Vận chuyển)

> **Phiên bản:** v3.1.0 (Master Unified REST Contract)  
> **Cổng dịch vụ (Port):** `8003` | **Database:** `commerce_db:5435` (PostgreSQL 16)  
> **Giao thức:** HTTP/1.1 & HTTP/2 RESTful Web API  
> **Đối tượng sử dụng:** Frontend (Web & Mobile), API Gateway Team, Wallet Service Team, Catalog Service Team, Fulfillment Service Team, QA Engineers  
> **Cập nhật lần cuối:** 2026-11-05  
> **Trạng thái:** ✅ ĐÃ CHUẨN HÓA 100% VỚI CODE THỰC THI (115/115 TESTS PASSING)  

---

## MỤC LỤC BỐ CỤC TÀI LIỆU
1. [Phần 1: Quy chuẩn Kiến trúc & Giao thức Chung](#phần-1-quy-chuẩn-kiến-trúc--giao-thức-chung)
2. [Phần 2: Bounded Context 1 — Quản lý Giỏ hàng (`CART-01` .. `CART-14`)](#phần-2-bounded-context-1--quản-lý-giỏ-hàng-cart-01--cart-14)
3. [Phần 3: Bounded Context 2 — Báo giá & Đặt hàng (`CHK-01` .. `CHK-02`)](#phần-3-bounded-context-2--báo-giá--đặt-hàng-chk-01--chk-02)
4. [Phần 4: Bounded Context 3 — Vòng đời Đơn hàng & Vận chuyển (`ORD-01` .. `ORD-13`)](#phần-4-bounded-context-3--vòng-đời-đơn-hàng--vận-chuyển-ord-01--ord-13)
5. [Phần 5: Webhook Callback Giao vận (`FulfillmentService` ➔ `CommerceService`)](#phần-5-webhook-callback-giao-vận)
6. [Phần 6: Hợp đồng Outbound Typed REST Clients](#phần-6-hợp-đồng-outbound-typed-rest-clients)
7. [Phần 7: Bảng Tra cứu Mã Lỗi Hệ thống Chuẩn RFC 9110](#phần-7-bảng-tra-cứu-mã-lỗi-hệ-thống-chuẩn-rfc-9110)
8. [Phần 8: Bộ TypeScript Type Definitions Hoàn chỉnh](#phần-8-bộ-typescript-type-definitions-hoàn-chỉnh)

---

# PHẦN 1: QUY CHUẨN KIẾN TRÚC & GIAO THỨC CHUNG

### 1.1 Nguyên Tắc Đóng Gói (Pure DTOs vs Envelope)
* **Tại Commerce Service (C# Controllers):** Các action trong Controller **CHỈ TRẢ VỀ PURE DTO** (DTO thuần túy) với HTTP status code chuẩn mực (200, 201, 400, 403, 404, 409, 500, 503). Tuyệt đối không tự bọc lặp `ApiResponse<T>`.
* **Tại API Gateway:** Tự động đóng gói vỏ phong bì khi phản hồi ra ngoài Internet:
  ```json
  {
    "success": true,
    "data": { ... },
    "error": null,
    "timestamp": "2026-11-05T10:00:00Z",
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
  }
  ```
* **Khi xảy ra lỗi (RFC 9110 Problem Details):**
  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    "title": "Business rule violation",
    "status": 400,
    "errorCode": "QUOTE_EXPIRED",
    "detail": "Checkout quote has expired, is invalid, or is currently being processed.",
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
  }
  ```

### 1.2 Quy Chuẩn Casing Bất Biến
* **Mã lỗi hệ thống (`errorCode`):** 100% `SCREAMING_SNAKE_CASE` (`QUOTE_EXPIRED`, `VAL_INVALID_INPUT`, `CART_ITEM_NOT_FOUND`).
* **Enum Dữ liệu nghiệp vụ trong JSON:** 100% chuỗi `PascalCase` (`OrderStatus: "PendingPayment"`, `SubOrderStatus: "Shipping"`). Cấm số nguyên và cấm in hoa toàn bộ.
* **Tên trường JSON payload:** 100% `camelCase` (`cartItemId`, `subtotalAmount`, `shippingFeeAmount`).
* **Chuẩn hóa trường tiền tệ:** Bắt buộc hậu tố `Amount` (`shippingFeeAmount`, `itemsAmount`, `totalAmount`), khai tử hoàn toàn `shippingFee`.

### 1.3 Headers Xác Thực Danh Tính Từ API Gateway
Mọi request gửi tới Commerce Service được `UserContextMiddleware` phân tích:
* `x-user-id`: GUID người dùng từ claim `sub` (Bắt buộc).
* `x-user-roles`: Quyền hạn (`buyer`, `seller`, `admin`, `staff`).
* `x-user-email`: Email người dùng.
* `Authorization`: `Bearer <JWT>` (Tùy chọn cho luồng ví).

---

# PHẦN 2: BOUNDED CONTEXT 1 — QUẢN LÝ GIỎ HÀNG (`CART-01` .. `CART-14`)

### `CART-01`: Lấy hoặc Tự Động Tạo Giỏ Hàng (`GET /api/v1/carts/me`)
* **Mô tả:** Lấy giỏ hàng đang hoạt động (`ACTIVE`) của người mua đang đăng nhập. Nếu chưa có, tự động tạo mới giỏ rỗng.
* **Quyền:** `buyer`
* **Response (HTTP 200 OK):**
  ```json
  {
    "id": "c1111111-1111-1111-1111-000000000001",
    "buyerId": "b1111111-1111-1111-1111-000000000001",
    "status": "Active",
    "itemCount": 2,
    "subtotalAmount": 500000.0,
    "items": [
      {
        "id": "i1111111-1111-1111-1111-000000000001",
        "cartId": "c1111111-1111-1111-1111-000000000001",
        "productId": "p1111111-1111-1111-1111-000000000001",
        "skuId": "s1111111-1111-1111-1111-000000000001",
        "sellerId": "shop1111-1111-1111-1111-000000000001",
        "productNameSnapshot": "Áo Thun Cotton Nexus",
        "skuCodeSnapshot": "AT-BLK-L",
        "quantity": 2,
        "unitPriceSnapshot": 250000.0,
        "lineTotal": 500000.0,
        "currency": "VND",
        "isAvailable": true,
        "availableStock": 50,
        "disabledReason": null,
        "createdAt": "2026-11-05T08:00:00Z",
        "updatedAt": "2026-11-05T08:00:00Z"
      }
    ],
    "lastActivityAt": "2026-11-05T08:00:00Z"
  }
  ```

### `CART-02`: Lấy Badge Tóm Tắt Giỏ Hàng (`GET /api/v1/carts/me/summary`)
* **Mô tả:** Lấy số lượng và tổng tiền tạm tính cho biểu tượng giỏ hàng trên Header/Navbar.
* **Response (HTTP 200 OK):**
  ```json
  {
    "cartId": "c1111111-1111-1111-1111-000000000001",
    "totalItemCount": 2,
    "subtotalAmount": 500000.0,
    "hasUnavailableItems": false
  }
  ```

### `CART-03`: Giỏ Hàng Phân Nhóm Theo Shop (`GET /api/v1/carts/me/grouped`)
* **Mô tả:** Trả về giỏ hàng đã phân nhóm theo từng Người bán (`SellerId`), sẵn sàng hiển thị giao diện sàn đa người bán.
* **Response (HTTP 200 OK):**
  ```json
  {
    "cartId": "c1111111-1111-1111-1111-000000000001",
    "shops": [
      {
        "sellerId": "shop1111-1111-1111-1111-000000000001",
        "shopName": "Nexus Official Store",
        "shopSubtotal": 500000.0,
        "items": [ ... ]
      }
    ],
    "grandSubtotal": 500000.0,
    "totalItemCount": 2
  }
  ```

### `CART-04`: Thêm Sản Phẩm Vào Giỏ (`POST /api/v1/carts/me/items`)
* **Request Body:**
  ```json
  {
    "skuId": "s1111111-1111-1111-1111-000000000001",
    "quantity": 1,
    "sellerId": "shop1111-1111-1111-1111-000000000001"
  }
  ```
* **Quy tắc:** Gọi Catalog `:8002` check tồn tại và giá; gọi Fulfillment `:8006` check tồn kho. Tối đa 50 món khác nhau / giỏ, tối đa 99 đơn vị / món.
* **Error Codes:** `INSUFFICIENT_STOCK` (400), `MAX_QUANTITY_EXCEEDED` (400), `MAX_ITEMS_REACHED` (400), `CATALOG_SKU_NOT_FOUND` (404), `CATALOG_SKU_INACTIVE` (400).

### `CART-05`: Cập Nhật Số Lượng Món (`PATCH /api/v1/carts/me/items/{cartItemId}`)
* **Request Body:** `{ "quantity": 3 }`
* **Quy tắc:** Quantity phải từ 1 đến 99. Kiểm tra tồn kho trước khi cập nhật.

### `CART-06`: Đổi Biến Thể Sản Phẩm Trong Giỏ (`PATCH /api/v1/carts/me/items/{cartItemId}/variant`)
* **Request Body:** `{ "newSkuId": "s2222222-2222-2222-2222-000000000002" }`
* **Quy tắc:** Gọi Catalog `:8002` xác nhận SKU mới thuộc cùng sản phẩm cha và cùng người bán. Nếu biến thể mới đã có sẵn dòng khác trong giỏ $\rightarrow$ Tự động gộp số lượng và xóa dòng cũ.

### `CART-07`: Xóa Một Món Khỏi Giỏ (`DELETE /api/v1/carts/me/items/{cartItemId}`)
* **Response (HTTP 200 OK):** Trả về `CartResponse` mới nhất sau khi xóa.

### `CART-08`: Xóa Hàng Loạt Món (`POST /api/v1/carts/me/items:bulk-remove`)
* **Request Body:**
  ```json
  {
    "cartItemIds": ["i1111111-...", "i2222222-..."],
    "clearAll": false
  }
  ```

### `CART-09`: Dọn Sạch Giỏ Hàng (`DELETE /api/v1/carts/me/clear`)
* **Response (HTTP 200 OK):** Trả về giỏ hàng rỗng (`itemCount = 0`, `subtotalAmount = 0`).

### `CART-10`: Tính Tiền Tạm Tính Các Món Tick Chọn (`POST /api/v1/carts/me/calculate`)
* **Mô tả:** Cơ chế checkbox Shopee: Khách chọn một vài món trong giỏ để xem trước tổng tiền hàng.
* **Request Body:** `{ "selectedItemIds": ["i1111111-..."] }`
* **Response (HTTP 200 OK):**
  ```json
  {
    "selectedItemCount": 1,
    "selectedSubtotal": 250000.0,
    "currency": "VND",
    "canCheckout": true,
    "validationMessages": []
  }
  ```

### `CART-11`: Kiểm Tra Tồn Kho & Độ Tươi Giá Trước Checkout (`POST /api/v1/carts/me/validate`)
* **Request Body:** `{ "selectedItemIds": ["i1111111-..."] }`
* **Response (HTTP 200 OK):**
  ```json
  {
    "isValid": true,
    "issues": [],
    "totalPriceChange": 0.0
  }
  ```

### `CART-12`: Gộp Giỏ Khách Vãng Lai Khi Đăng Nhập (`POST /api/v1/carts/me/merge`)
* **Request Body:**
  ```json
  {
    "guestItems": [
      {
        "skuId": "s1111111-...",
        "quantity": 1,
        "sellerId": "shop1111-..."
      }
    ]
  }
  ```

### `CART-13`: Xóa Món Đã Mua Sau Khi Thanh Toán (`POST /api/v1/carts/me/cleanup-checked-out`)
* **Request Body:** `{ "purchasedItemIds": ["i1111111-..."] }`

### `CART-14`: Tra Cứu Giỏ Hàng Đang Hoạt Động (`GET /api/v1/carts/active?buyerId={guid}`)
* **Quyền:** `buyer` (cho chính mình), `admin`, `staff` (cho bất kỳ ai).

---

# PHẦN 3: BOUNDED CONTEXT 2 — BÁO GIÁ & ĐẶT HÀNG (`CHK-01` .. `CHK-02`)

### `CHK-01 / R75`: Khảo Giá Thanh Toán & Tính Phí Ship GHN (`POST /api/v1/checkouts/quote`)
* **Request Body:**
  ```json
  {
    "cartItemIds": ["i1111111-1111-1111-1111-000000000001"],
    "shippingAddress": {
      "receiverName": "Nguyen Van A",
      "contactPhone": "0901234567",
      "street": "123 Le Loi",
      "ward": "Phường Bến Nghé",
      "district": "Quận 1",
      "province": "TP. Hồ Chí Minh",
      "ghnDistrictId": 1442,
      "ghnWardCode": "20101"
    },
    "contactPhone": "0901234567",
    "paymentMethod": "Wallet",
    "voucherCodes": []
  }
  ```
* **Response (HTTP 200 OK):**
  ```json
  {
    "quoteId": "q1111111-1111-1111-1111-000000000001",
    "grandTotal": 530000.0,
    "walletBalance": 1500000.0,
    "shops": [
      {
        "sellerId": "shop1111-1111-1111-1111-000000000001",
        "shopName": "Shop shop1111",
        "items": [
          {
            "cartItemId": "i1111111-1111-1111-1111-000000000001",
            "productId": "p1111111-1111-1111-1111-000000000001",
            "skuId": "s1111111-1111-1111-1111-000000000001",
            "productName": "Áo Thun Cotton Nexus",
            "quantity": 2,
            "unitPrice": 250000.0,
            "lineTotal": 500000.0
          }
        ],
        "itemsAmount": 500000.0,
        "productDiscountAmount": 0.0,
        "shopDiscountAmount": 0.0,
        "systemDiscountAmount": 0.0,
        "discountAmount": 0.0,
        "shippingFeeAmount": 30000.0,
        "totalAmount": 530000.0
      }
    ],
    "appliedVouchers": [],
    "appliedPriceDrops": null,
    "expiredAt": "2026-11-05T10:15:00Z"
  }
  ```

### `CHK-02 / R76`: Tạo Đơn Hàng Chính Thức (`POST /api/v1/orders`)
* **Request Body:**
  ```json
  {
    "quoteId": "q1111111-1111-1111-1111-000000000001",
    "paymentMethod": "Wallet",
    "shippingAddress": "123 Le Loi, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh",
    "contactPhone": "0901234567"
  }
  ```
* **Kịch bản thực thi:**
  1. Atomic Pop đọc và xóa ngay `checkout_quote:{quoteId}` khỏi Distributed Cache.
  2. Asymmetric Price Drift: Quét giá Catalog `:8002`. Nếu có món tăng giá $\rightarrow$ Trả 409 `PRICE_CHANGED`. Nếu giảm giá $\rightarrow$ Tự động áp dụng giá rẻ hơn.
  3. Giữ kho 15 phút tại Fulfillment `:8006` (`ReserveStockAsync`).
  4. Trừ tiền ví tại Wallet `:8008` (`PayOrderAsync`). Nếu thành công $\rightarrow$ Trừ kho vĩnh viễn (`CommitStockAsync`), đơn chuyển `Paid`.
  5. Dọn dẹp giỏ hàng và lưu DB. Bọc Circuit Breaker bồi hoàn nếu DB bị lỗi.
* **Response (HTTP 201 Created):**
  ```json
  {
    "orderId": "o1111111-1111-1111-1111-000000000001",
    "orderNo": "ORD-20261105-O11111",
    "totalAmount": 530000.0,
    "status": "Paid",
    "paymentDeadlineAt": "2026-11-05T10:15:00Z",
    "subOrderIds": ["so111111-1111-1111-1111-000000000001"],
    "appliedPriceDrops": null
  }
  ```

---

# PHẦN 4: BOUNDED CONTEXT 3 — VÒNG ĐỜI ĐƠN HÀNG & VẬN CHUYỂN (`ORD-01` .. `ORD-13`)

### `ORD-01 / R78`: Lịch Sử Đơn Hàng Của Tôi (`GET /api/v1/orders`)
* **Query Params:** `page` (int, default: 1), `pageSize` (int, default: 20), `status` (OrderStatus enum, optional).
* **Response (HTTP 200 OK):**
  ```json
  {
    "items": [
      {
        "id": "o1111111-1111-1111-1111-000000000001",
        "orderNo": "ORD-O1111111",
        "totalAmount": 530000.0,
        "status": "Paid",
        "paymentDeadlineAt": "2026-11-05T10:15:00Z",
        "subOrderCount": 1,
        "createdAt": "2026-11-05T10:00:00Z"
      }
    ],
    "pagination": {
      "pageIndex": 1,
      "pageSize": 20,
      "totalCount": 1,
      "totalPages": 1,
      "hasPreviousPage": false,
      "hasNextPage": false
    }
  }
  ```

### `ORD-02 / R77`: Chi Tiết Đơn Hàng Cha (`GET /api/v1/orders/{orderId}`)
* **Cơ chế:** Kèm Passive Guard tự hủy đơn và nhả kho nếu quá hạn thanh toán. Kiểm tra quyền sở hữu `order.BuyerId == currentUserId`.

### `ORD-02B / R79`: Chi Tiết Đơn Con Của Shop (`GET /api/v1/sub-orders/{subOrderId}`)
* **Response (HTTP 200 OK):** Single DTO bóc tách đầy đủ địa chỉ snapshot JSONB, discount breakdown và danh sách item.

### `ORD-06 / R80`: Người Mua Hủy Đơn Chờ Thanh Toán (`POST /api/v1/orders/{orderId}/cancel`)
* **Request Body:** `{ "reasonCode": "BuyerChangedMind", "reasonText": "Muốn đổi địa chỉ khác" }`
* **Quy tắc:** Chỉ hủy được khi đơn ở trạng thái `PendingPayment`. Tự động nhả kho Fulfillment.

### `ORD-04 / R83`: Người Bán Tiếp Nhận Đơn Con (`POST /api/v1/orders/{orderId}/accept`)
* **Request Body:** `{ "subOrderId": "so111111-..." }`
* **Quy tắc:** Chuyển `SubOrder.Status = Confirmed`, tự động gọi Fulfillment `:8006` tạo vận đơn GHN (`POST /api/v1/internal/shipments`).

### `ORD-05 / R84`: Người Bán Từ Chối Đơn Con (`POST /api/v1/orders/{orderId}/reject`)
* **Request Body:**
  ```json
  {
    "subOrderId": "so111111-...",
    "reasonCode": "OutOfStock",
    "reasonText": "Hết size L màu đen"
  }
  ```
* **Quy tắc:** Chuyển `SubOrder.Status = Cancelled`, tạo `OrderRefund` (Status: `Requested`), gọi Fulfillment nhả kho có mục tiêu (`POST /api/v1/internal/inventories/reservations/{orderId}/release-items`).

### `ORD-08 / R87`: Theo Dõi Tiến Độ Giao Hàng (`GET /api/v1/shipments/order/{orderId}`)
* **Response (HTTP 200 OK):** Trả về danh sách kiện hàng, carrier (`GHN`), mã vận đơn và mốc thời gian `ShippedAt`, `DeliveredAt`.

### `ORD-13 / R99`: Khởi Tạo Yêu Cầu Hoàn Tiền (`POST /api/v1/sub-orders/{subOrderId}/refund`)
* **Request Body:** `{ "reason": "DamagedItem", "note": "Sản phẩm bị rách mép" }`

---

# PHẦN 5: WEBHOOK CALLBACK GIAO VẬN

### Endpoint: `POST /api/v1/sub-orders/{subOrderId}/delivery-status`
* **Nguồn gọi:** `FulfillmentService` (`:8006`).
* **Header bảo mật bắt buộc:** `X-Internal-Service-Key: <secret>` (Khớp với cấu hình; thiếu hoặc sai trả 403 `FORBIDDEN`).
* **Request Body:**
  ```json
  {
    "status": "Delivered",
    "deliveredAt": "2026-11-05T12:00:00Z"
  }
  ```
* **Ma trận 9 trạng thái giao vận:**
  * `ReadyToPick`, `Picking`, `Packed`: Cập nhật timestamp chuẩn bị hàng (Idempotent 200 OK).
  * `HandedOver`, `InTransit`, `Shipped`: Chuyển `SubOrderStatus.Shipped`, ghi nhận `ShippedAt`.
  * `Delivered`: Chuyển `SubOrderStatus.Delivered`, ghi nhận `DeliveredAt`.
  * `Cancelled`: Chuyển `SubOrderStatus.Cancelled`, ghi nhận `CancelledAt`.
  * `DeliveryFailed`, `Returned`: Chuyển `SubOrderStatus.Cancelled`, tự động ghi nhận `OrderCancellation` (Lý do: `DeliveryFailed` hoặc `PackageReturned`) và tự động tạo `OrderRefund` (Status: `Requested`) nếu đơn đã thanh toán.

---

# PHẦN 6: HỢP ĐỒNG OUTBOUND TYPED REST CLIENTS

### 6.1 `CatalogClient` (`:8002` — `ICatalogClient`)
* `POST /api/v1/internal/variants`: Batch tra cứu thông tin SKU (VariantId, ProductId, SellerId, ProductName, Price, IsPurchasable).
* `POST /api/v1/internal/catalog/skus/validate-variant-switch`: Kiểm tra hợp lệ đổi biến thể (thuộc cùng Product, cùng Seller).

### 6.2 `FulfillmentClient` (`:8006` — `IFulfillmentClient`)
* `GET /api/v1/inventories/{skuId}`: Check tồn kho đơn lẻ.
* `POST /api/v1/inventories/by-skus`: Batch check tồn kho toàn giỏ hàng.
* `POST /api/v1/internal/inventories/reservations`: Giữ hàng 15 phút.
* `POST /api/v1/internal/inventories/reservations/{orderId}/commit`: Trừ kho vĩnh viễn.
* `POST /api/v1/internal/inventories/reservations/{orderId}/release?reason=OrderCancelled`: Nhả kho cả đơn.
* `POST /api/v1/internal/inventories/reservations/batch-release`: Batch nhả kho cho nhiều đơn quá hạn.
* `POST /api/v1/internal/inventories/reservations/{orderId}/release-items`: Nhả kho có mục tiêu cho SubOrder bị từ chối (`{ items: [{ skuId, quantity }], reason: "SubOrderRejected" }`).
* `POST /api/v1/internal/shipping/quote`: Tính cước GHN động.
* `POST /api/v1/internal/shipments`: Tạo vận đơn GHN.

### 6.3 `WalletClient` (`:8008` — `IWalletClient`)
* `GET /api/v1/wallets/me`: Lấy số dư ví người mua.
* `POST /api/v1/internal/wallet/orders/{orderId}/payment`: Trừ tiền ví (`{ amount }`).

---

# PHẦN 7: BẢNG TRA CỨU MÃ LỖI HỆ THỐNG CHUẨN RFC 9110

| HTTP Status | Mã Lỗi (`errorCode`) | Ngữ Cảnh Xảy Ra | Câu Thông Báo Tiếng Anh Chuẩn (Sentence Case) |
| :-: | - | - | - |
| **400** | `VAL_INVALID_INPUT` | Body thiếu trường hoặc dữ liệu sai định dạng | `"Shipping address fields cannot be empty."` |
| **400** | `CART_EMPTY` | Giỏ hàng rỗng hoặc không có món hợp lệ khi checkout | `"No valid items selected for checkout."` |
| **400** | `QUOTE_EXPIRED` | Báo giá hết hạn 15m hoặc bấm đúp nút Đặt hàng | `"Checkout quote has expired, is invalid, or is currently being processed."` |
| **400** | `INSUFFICIENT_STOCK` | Kho không đủ số lượng tồn khả dụng | `"One or more items in the order have insufficient stock."` |
| **400** | `MAX_QUANTITY_EXCEEDED` | Mua vượt quá 99 chiếc/món | `"Quantity per item cannot exceed 99."` |
| **400** | `MAX_ITEMS_REACHED` | Vượt quá 50 món khác nhau trong giỏ | `"Cart cannot contain more than 50 distinct items."` |
| **400** | `INVALID_STATUS` | Chuyển trạng thái đơn không hợp lệ | `"Sub-order cannot be accepted in its current status."` |
| **400** | `ORDER_CANNOT_BE_CANCELLED` | Hủy đơn không ở trạng thái PendingPayment | `"Order cannot be cancelled in its current status."` |
| **400** | `CATALOG_SKU_INACTIVE` | Sản phẩm đã ngừng bán trên sàn | `"Product is no longer available for purchase."` |
| **400** | `INVALID_DELIVERY_STATUS` | Webhook gửi status giao vận lạ | `"The provided delivery status is not supported."` |
| **403** | `FORBIDDEN` | Truy cập đơn hàng/giỏ hàng của người khác | `"You do not have permission to access this order."` |
| **403** | `FORBIDDEN` | Webhook thiếu hoặc sai X-Internal-Service-Key | `"Access denied: Missing or invalid X-Internal-Service-Key header."` |
| **404** | `CART_ITEM_NOT_FOUND` | Không tìm thấy dòng sản phẩm trong giỏ | `"Cart item not found."` |
| **404** | `ORDER_NOT_FOUND` | Không tìm thấy đơn hàng cha | `"Order was not found."` |
| **404** | `SUB_ORDER_NOT_FOUND` | Không tìm thấy đơn hàng con | `"SubOrder was not found."` |
| **404** | `CATALOG_SKU_NOT_FOUND` | SKU không tồn tại trong Catalog Service | `"Product SKU was not found in catalog."` |
| **409** | `PRICE_CHANGED` | Sản phẩm bị tăng giá lúc bấm Đặt hàng | `"Price of '...' has increased from ... to .... Cart prices have been refreshed."` |
| **500** | `ORDER_CREATION_FAILED` | Sập DB lúc tạo đơn, đã kích hoạt bù trừ | `"Failed to save order to database. Automatic compensation refund initiated."` |
| **503** | `STOCK_CHECK_UNAVAILABLE` | Kho Fulfillment tạm thời không phản hồi | `"Warehouse inventory service is temporarily unreachable."` |
| **503** | `FULFILLMENT_SERVICE_UNAVAILABLE` | Tính cước GHN hoặc tạo vận đơn lỗi | `"Fulfillment service is temporarily unreachable."` |
| **503** | `CATALOG_SERVICE_UNAVAILABLE` | Catalog Service tạm thời mất kết nối | `"Product catalog service is temporarily unreachable."` |

---

# PHẦN 8: BỘ TYPESCRIPT TYPE DEFINITIONS HOÀN CHỈNH

Dành cho Frontend Web/Mobile và SDK Client:

```typescript
export type OrderStatus = 'PendingPayment' | 'Paid' | 'Processing' | 'Completed' | 'Cancelled';
export type SubOrderStatus = 'PendingPayment' | 'Paid' | 'Confirmed' | 'Shipping' | 'Delivered' | 'Completed' | 'Cancelled';
export type PaymentMethod = 'Cod' | 'Wallet' | 'VNPay';

export interface CartItemDto {
  id: string;
  cartId: string;
  productId: string;
  skuId: string;
  sellerId: string;
  productNameSnapshot: string;
  skuCodeSnapshot?: string | null;
  quantity: number;
  unitPriceSnapshot: number;
  lineTotal: number;
  currency: string;
  isAvailable: boolean;
  availableStock: number;
  disabledReason?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CartResponse {
  id: string;
  buyerId: string;
  status: string;
  itemCount: number;
  subtotalAmount: number;
  items: CartItemDto[];
  lastActivityAt: string;
}

export interface CheckoutQuoteRequest {
  cartItemIds: string[];
  shippingAddress: {
    receiverName: string;
    contactPhone: string;
    street: string;
    ward: string;
    district: string;
    province: string;
    ghnDistrictId?: number | null;
    ghnWardCode?: string | null;
  };
  contactPhone: string;
  paymentMethod: PaymentMethod;
  voucherCodes?: string[];
}

export interface CheckoutQuoteResponse {
  quoteId: string;
  grandTotal: number;
  walletBalance: number;
  shops: ShopOrderQuote[];
  appliedVouchers: string[];
  appliedPriceDrops?: PriceDropDto[] | null;
  expiredAt: string;
}

export interface ShopOrderQuote {
  sellerId: string;
  shopName: string;
  items: ShopQuoteItem[];
  itemsAmount: number;
  productDiscountAmount: number;
  shopDiscountAmount: number;
  systemDiscountAmount: number;
  discountAmount: number;
  shippingFeeAmount: number;
  totalAmount: number;
}

export interface ShopQuoteItem {
  cartItemId: string;
  productId: string;
  skuId: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface CreateOrderRequest {
  quoteId: string;
  paymentMethod: PaymentMethod;
  shippingAddress: string;
  contactPhone: string;
}

export interface CreateOrderResponse {
  orderId: string;
  orderNo: string;
  totalAmount: number;
  status: OrderStatus;
  paymentDeadlineAt: string;
  subOrderIds: string[];
  appliedPriceDrops?: PriceDropDto[] | null;
}

export interface SubOrderDetailResponse {
  id: string;
  subOrderNo: string;
  orderId: string;
  buyerId: string;
  sellerId: string;
  shopName: string;
  status: SubOrderStatus;
  paymentMethod: PaymentMethod;
  shippingAddress: any;
  contactPhone: string;
  itemsAmount: number;
  productDiscountAmount: number;
  shopDiscountAmount: number;
  systemDiscountAmount: number;
  discountAmount: number;
  shippingFeeAmount: number;
  totalAmount: number;
  createdAt: string;
  items: SubOrderItemResponse[];
}

export interface SubOrderItemResponse {
  id: string;
  productId: string;
  skuId: string;
  productName: string;
  skuCode?: string | null;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface PriceDropDto {
  skuId: string;
  productName: string;
  quotedUnitPrice: number;
  currentUnitPrice: number;
  savingsAmount: number;
}
```
