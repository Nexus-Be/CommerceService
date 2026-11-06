# COMMERCE SERVICE — MASTER REST API CONTRACT & INTEGRATION SPECIFICATION
## (Hợp đồng Giao tiếp RESTful Toàn diện: Giỏ hàng, Báo giá, Đơn hàng, Vận chuyển & Liên dịch vụ)

> **Phiên bản:** v3.2.0 (Master Unified REST Contract)  
> **Phân hệ:** Commerce Service (`Commerce.Api`)  
> **Cổng dịch vụ (Port):** `8003` (Container & Localhost)  
> **Cơ sở dữ liệu:** PostgreSQL port `5435` (`commerce_db`)  
> **Giao thức:** HTTP/1.1 & HTTP/2 RESTful Web API  
> **Đối tượng sử dụng:** Frontend (Web & Mobile), API Gateway Team, Wallet Service Team, Catalog Service Team, Fulfillment Service Team, QA Engineers, OJT / Kỹ sư Phần mềm  
> **Cập nhật lần cuối:** 2026-11-05  
> **Trạng thái:** ✅ ĐÃ CHUẨN HÓA 100% VỚI CODE THỰC THI (115/115 TESTS PASSING)  

---

## MỤC LỤC BỐ CỤC TÀI LIỆU

1. [Phần 1: Quy chuẩn Kiến trúc & Giao thức Chung](#phần-1-quy-chuẩn-kiến-trúc--giao-thức-chung)
2. [Phần 2: Bounded Context 1 — Quản lý Giỏ hàng (`CART-01` .. `CART-14`)](#phần-2-bounded-context-1--quản-lý-giỏ-hàng-cart-01--cart-14)
   * *Nhóm 1: Xem & Khám phá Giỏ hàng (`CART-01` .. `CART-03`)*
   * *Nhóm 2: Thao tác Dòng Sản phẩm (`CART-04` .. `CART-09`)*
   * *Nhóm 3: Tính giá, Thẩm định & Gộp giỏ (`CART-10` .. `CART-12`)*
   * *Nhóm 4: Hậu Đặt hàng & Quản trị (`CART-13` .. `CART-14`)*
3. [Phần 3: Bounded Context 2 — Báo giá & Đặt hàng 2 Bước (`CHK-01` .. `CHK-02`)](#phần-3-bounded-context-2--báo-giá--đặt-hàng-2-bước-chk-01--chk-02)
   * *Bước 1: Báo giá Giỏ hàng Đa Shop (`CHK-01` / `R75`)*
   * *Bước 2: Tạo Đơn hàng Chính thức (`CHK-02` / `R76`)*
4. [Phần 4: Bounded Context 3 — Vòng đời Đơn hàng & Vận chuyển (`ORD-01` .. `ORD-13`)](#phần-4-bounded-context-3--vòng-đời-đơn-hàng--vận-chuyển-ord-01--ord-13)
   * *Tra cứu Đơn hàng (`ORD-01`, `ORD-02`, `ORD-02B`, `LEGACY`)*
   * *Xử lý & Chuyển trạng thái Đơn (`ORD-04`, `ORD-05`, `ORD-06`)*
   * *Vận chuyển & Hoàn tiền (`ORD-08`, `ORD-13`)*
5. [Phần 5: Webhook Tiếp Nhận Trạng Thái Giao Vận (Fulfillment ➔ Commerce)](#phần-5-webhook-tiếp-nhận-trạng-thái-giao-vận)
6. [Phần 6: Hợp đồng Outbound Typed REST Clients (Gọi Liên Dịch Vụ)](#phần-6-hợp-đồng-outbound-typed-rest-clients-gọi-liên-dịch-vụ)
   * *Catalog Service (`:8002`)*
   * *Fulfillment Service (`:8006`)*
   * *Wallet Service (`:8008`)*
7. [Phần 7: Bảng Tra cứu Mã Lỗi Hệ thống Chuẩn RFC 9110](#phần-7-bảng-tra-cứu-mã-lỗi-hệ-thống-chuẩn-rfc-9110)
8. [Phần 8: Bộ TypeScript Type Definitions Hoàn chỉnh (Frontend DTOs)](#phần-8-bộ-typescript-type-definitions-hoàn-chỉnh-frontend-dtos)

---

# PHẦN 1: QUY CHUẨN KIẾN TRÚC & GIAO THỨC CHUNG

### 1.1. Bounded Context & Phân định Ranh giới
Commerce Service chịu trách nhiệm độc quyền:
1. **Giỏ hàng (`Carts`, `CartItems`)**: Mỗi Người mua (Buyer) sở hữu tối đa 1 Giỏ hàng `Active`. Giới hạn tối đa 50 món (distinct items) và tối đa 99 đơn vị/món.
2. **Đơn hàng cha & Đơn con theo Shop (`Orders`, `SubOrders`, `SubOrderItems`)**: Đơn cha quản lý tổng thanh toán toàn phiên; Đơn con quản lý từng gói hàng độc lập của từng Người bán (Seller).
3. **Snapshot Bất biến (Immutable Snapshots)**:
   * Thông tin sản phẩm lúc mua: `ProductNameSnapshot`, `UnitPriceSnapshot`, `SkuCodeSnapshot`.
   * Địa chỉ giao hàng: `ShippingAddressSnapshot` (dạng PostgreSQL `jsonb` qua EF Core 8 `.ToJson()`).
4. **Các phân hệ KHÔNG thuộc Commerce Service (External Collaborations)**:
   * **Số dư tiền, Trừ tiền ví & Hoàn tiền ví** ➔ Do **Wallet Service** (`:8008`) sở hữu (gọi qua `IWalletClient`).
   * **Thông tin sản phẩm niêm yết (Master Data, Tên, Giá bán, Trạng thái bán)** ➔ Do **Catalog Service** (`:8002`) sở hữu (gọi qua `ICatalogClient`).
   * **Tồn kho vật lý, Giữ hàng 15m, Trừ/Nhả kho & Vận chuyển GHN** ➔ Do **Fulfillment Service** (`:8006`) sở hữu (gọi qua `IFulfillmentClient`).

---

### 1.2. Cơ chế Identity Propagation & Headers từ API Gateway
Mọi HTTP request từ Frontend (Web/Mobile) gửi qua API Gateway được middleware tiếp nhận và chuyển tiếp:

```
[ API Gateway ]
       │ Headers: x-user-id, x-user-roles, x-user-email, x-user-permissions
       ▼
[ UserContextMiddleware ] ──▶ Khởi tạo ClaimsPrincipal trên HttpContext.User
       │
[ Commerce Controllers ]  ──▶ CartsController / OrdersController
       │
[ IdentityPropagationHandler ] ──▶ Tự động chèn Headers sang Outbound HttpClients
       ▼
[ External Microservices ] (Catalog :8002, Fulfillment :8006, Wallet :8008)
```

| Header Name | Kiểu | Bắt buộc | Diễn giải | Ví dụ |
| - | - | :-: | - | - |
| `x-user-id` | UUID (v4) | **Có** | ID người dùng xác thực từ Claim `sub` | `11111111-1111-1111-1111-000000000004` |
| `x-user-roles` | String | **Có** | Vai trò người dùng (`buyer`, `seller`, `admin`, `staff`) | `buyer` hoặc `buyer,seller` |
| `x-user-email` | String | Không | Email người dùng | `buyer@nexus.local` |
| `x-user-permissions` | String | Không | Danh sách permissions chi tiết | `ORDER_READ,ORDER_WRITE` |
| `x-client-ip` | String | Không | IP của client thực tế | `192.168.1.100` |
| `x-trace-id` | String | Không | W3C / OpenTelemetry Trace ID | `00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01` |
| `X-Internal-Service-Key` | String | Nội bộ | Khóa bảo mật giao tiếp giữa các services nội bộ | `<secret-key>` |

---

### 1.3. Quy tắc Đóng gói Phong bì (Response Envelope Conventions)
* **Tại tầng Controller C# (Nội bộ Commerce Service)**: Controller **CHỈ TRẢ VỀ PURE DTO** (DTO thuần túy) kèm HTTP Status chuẩn. Tuyệt đối không bọc lặp `ApiResponse<T>`.
* **Tại tầng API Gateway**: Gateway tự động bọc vỏ phong bì trước khi truyền ra ngoài Internet:
  ```json
  {
    "success": true,
    "data": { ... },
    "error": null,
    "timestamp": "2026-11-05T10:00:00Z",
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
  }
  ```
* **Khi xảy ra lỗi (RFC 9110 Problem Details)**:
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

---

### 1.4. Quy tắc Vàng về Casing & Định danh Trường Tài chính
1. **Mã lỗi hệ thống (`errorCode`)**: 100% `SCREAMING_SNAKE_CASE` (ví dụ: `CART_ITEM_NOT_FOUND`, `ORDER_NOT_FOUND`, `VAL_INVALID_INPUT`, `PRICE_CHANGED`).
2. **Enum Dữ liệu nghiệp vụ**: 100% `PascalCase` (ví dụ: `"PendingPayment"`, `"SellerRejected"`, `"Confirmed"`, `"Wallet"`). Cấm chuỗi in hoa toàn bộ và cấm số nguyên.
3. **Thuộc tính JSON**: 100% `camelCase` (ví dụ: `cartItemId`, `subOrderNo`, `totalAmount`).
4. **Khóa cứng Trường Tiền tệ**: 100% sử dụng **`shippingFeeAmount`**. CẤM tuyệt đối từ khóa `shippingFee`.
   * $\text{totalAmount} = \text{itemsAmount} - \text{discountAmount} + \text{shippingFeeAmount}$
   * $\text{discountAmount} = \text{productDiscountAmount} + \text{shopDiscountAmount} + \text{systemDiscountAmount}$

---

# PHẦN 2: BOUNDED CONTEXT 1 — QUẢN LÝ GIỎ HÀNG (`CART-01` .. `CART-14`)

### Bảng tra cứu 14 API Giỏ hàng (`CartsController` — `/api/v1/carts`):

| Mã | Method | Path | Tên nghiệp vụ | Quyền |
| :-: | :-: | - | - | :-: |
| **`CART-01`** | `GET` | `/api/v1/carts/me` | Lấy hoặc tự động tạo giỏ hàng người mua | `buyer` |
| **`CART-02`** | `GET` | `/api/v1/carts/me/summary` | Badge số lượng & tổng tiền tạm tính cho Header | `buyer` |
| **`CART-03`** | `GET` | `/api/v1/carts/me/grouped` | Giỏ hàng gom nhóm theo Shop (Multi-Vendor) | `buyer` |
| **`CART-04`** | `POST` | `/api/v1/carts/me/items` | Thêm sản phẩm (SKU) vào giỏ hàng | `buyer` |
| **`CART-05`** | `PATCH` | `/api/v1/carts/me/items/{cartItemId}` | Cập nhật số lượng của dòng sản phẩm (1–99) | `buyer` |
| **`CART-06`** | `PATCH` | `/api/v1/carts/me/items/{cartItemId}/variant` | Đổi phân loại hàng trực tiếp trong giỏ (Smart Merge) | `buyer` |
| **`CART-07`** | `DELETE` | `/api/v1/carts/me/items/{cartItemId}` | Xóa một món khỏi giỏ hàng | `buyer` |
| **`CART-08`** | `POST` | `/api/v1/carts/me/items:bulk-remove` | Xóa hàng loạt món được tick chọn | `buyer` |
| **`CART-09`** | `DELETE` | `/api/v1/carts/me/clear` | Dọn sạch toàn bộ giỏ hàng | `buyer` |
| **`CART-10`** | `POST` | `/api/v1/carts/me/calculate` | Tính tiền tạm tính các món tick chọn (Shopee Checkbox) | `buyer` |
| **`CART-11`** | `POST` | `/api/v1/carts/me/validate` | Kiểm tra tồn kho & biến động giá trước checkout | `buyer` |
| **`CART-12`** | `POST` | `/api/v1/carts/me/merge` | Gộp giỏ hàng khách vãng lai sau khi đăng nhập | `buyer` |
| **`CART-13`** | `POST` | `/api/v1/carts/me/cleanup-checked-out` | Dọn dẹp món đã mua sau khi thanh toán thành công | `buyer` |
| **`CART-14`** | `GET` | `/api/v1/carts/active?buyerId={guid}` | Quản trị tra cứu giỏ hàng đang hoạt động | `admin`, `staff`, `buyer` |

---

### Chi tiết Đặc tả 14 Endpoint Giỏ hàng:

#### `CART-01`: Lấy hoặc Tự Động Tạo Giỏ Hàng
* **Endpoint:** `GET /api/v1/carts/me`
* **Quyền:** `buyer`
* **Response (HTTP 200 OK):**
  ```json
  {
    "id": "c1111111-1111-1111-1111-000000000001",
    "buyerId": "11111111-1111-1111-1111-000000000004",
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

#### `CART-02`: Lấy Badge Tóm Tắt Giỏ Hàng
* **Endpoint:** `GET /api/v1/carts/me/summary`
* **Response (HTTP 200 OK):**
  ```json
  {
    "cartId": "c1111111-1111-1111-1111-000000000001",
    "totalItemCount": 2,
    "subtotalAmount": 500000.0,
    "hasUnavailableItems": false
  }
  ```

#### `CART-03`: Giỏ Hàng Gom Nhóm Theo Shop
* **Endpoint:** `GET /api/v1/carts/me/grouped`
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

#### `CART-04`: Thêm Sản Phẩm Vào Giỏ Hàng
* **Endpoint:** `POST /api/v1/carts/me/items`
* **Request Body:**
  ```json
  {
    "skuId": "s1111111-1111-1111-1111-000000000001",
    "quantity": 2,
    "sellerId": "shop1111-1111-1111-1111-000000000001"
  }
  ```
* **Lỗi có thể trả về:**
  * `INSUFFICIENT_STOCK` (400): Tồn kho khả dụng nhỏ hơn số lượng yêu cầu.
  * `MAX_QUANTITY_EXCEEDED` (400): Số lượng dòng món vượt quá 99.
  * `MAX_ITEMS_REACHED` (400): Giỏ hàng đã đạt giới hạn tối đa 50 món khác nhau.
  * `CATALOG_SKU_NOT_FOUND` (404): Không tìm thấy SKU trong Catalog.
  * `CATALOG_SKU_INACTIVE` (400): Sản phẩm bị ẩn hoặc ngừng kinh doanh.
* **Response (HTTP 200 OK):** Trả về `CartResponse` mới nhất.

#### `CART-05`: Cập Nhật Số Lượng Dòng Sản Phẩm
* **Endpoint:** `PATCH /api/v1/carts/me/items/{cartItemId}`
* **Request Body:** `{ "quantity": 3 }`
* **Response (HTTP 200 OK):** Trả về `CartResponse` cập nhật.

#### `CART-06`: Đổi Biến Thể Sản Phẩm Trong Giỏ
* **Endpoint:** `PATCH /api/v1/carts/me/items/{cartItemId}/variant`
* **Request Body:** `{ "newSkuId": "s2222222-2222-2222-2222-000000000002" }`
* **Cơ chế:** Kiểm tra SKU mới thuộc cùng sản phẩm và cùng người bán qua Catalog. Tự động merge số lượng nếu biến thể mới đã có sẵn một dòng khác trong giỏ.
* **Response (HTTP 200 OK):** Trả về `CartResponse` cập nhật.

#### `CART-07`: Xóa Một Món Khỏi Giỏ Hàng
* **Endpoint:** `DELETE /api/v1/carts/me/items/{cartItemId}`
* **Response (HTTP 200 OK):** Trả về `CartResponse` mới nhất sau khi xóa.

#### `CART-08`: Xóa Hàng Loạt Món Được Tick Chọn
* **Endpoint:** `POST /api/v1/carts/me/items:bulk-remove`
* **Request Body:**
  ```json
  {
    "cartItemIds": ["i1111111-1111-...", "i2222222-2222-..."],
    "clearAll": false
  }
  ```
* **Response (HTTP 200 OK):** Trả về `CartResponse` cập nhật.

#### `CART-09`: Dọn Sạch Toàn Bộ Giỏ Hàng
* **Endpoint:** `DELETE /api/v1/carts/me/clear`
* **Response (HTTP 200 OK):** Trả về `CartResponse` với `itemCount: 0`, `subtotalAmount: 0`.

#### `CART-10`: Tính Tiền Tạm Tính Các Món Tick Chọn (Shopee Checkbox)
* **Endpoint:** `POST /api/v1/carts/me/calculate`
* **Request Body:**
  ```json
  {
    "selectedItemIds": ["i1111111-1111-1111-1111-000000000001"]
  }
  ```
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

#### `CART-11`: Kiểm Tra Tồn Kho & Biến Động Giá Trước Checkout
* **Endpoint:** `POST /api/v1/carts/me/validate`
* **Request Body:**
  ```json
  {
    "selectedItemIds": ["i1111111-1111-1111-1111-000000000001"]
  }
  ```
* **Response (HTTP 200 OK):**
  ```json
  {
    "isValid": true,
    "issues": [],
    "totalPriceChange": 0.0
  }
  ```

#### `CART-12`: Gộp Giỏ Khách Vãng Lai Sau Khi Đăng Nhập
* **Endpoint:** `POST /api/v1/carts/me/merge`
* **Request Body:**
  ```json
  {
    "guestItems": [
      {
        "skuId": "s1111111-1111-1111-1111-000000000001",
        "quantity": 1,
        "sellerId": "shop1111-1111-1111-1111-000000000001"
      }
    ]
  }
  ```
* **Response (HTTP 200 OK):** Trả về `CartResponse` gộp.

#### `CART-13`: Dọn Dẹp Món Đã Mua Sau Khi Thanh Toán Thành Công
* **Endpoint:** `POST /api/v1/carts/me/cleanup-checked-out`
* **Request Body:** `{ "purchasedItemIds": ["i1111111-1111-..."] }`
* **Response (HTTP 200 OK):** Trả về `CartResponse` sau dọn dẹp.

#### `CART-14`: Quản Trị Tra Cứu Giỏ Hàng Đang Hoạt Động
* **Endpoint:** `GET /api/v1/carts/active?buyerId={guid}`
* **Quyền:** `admin`, `staff` (cho bất kỳ buyer nào) hoặc `buyer` (cho chính mình qua ownership check).
* **Response (HTTP 200 OK):** Trả về `CartResponse`.

---

# PHẦN 3: BOUNDED CONTEXT 2 — BÁO GIÁ & ĐẶT HÀNG 2 BƯỚC (`CHK-01` .. `CHK-02`)

```
┌────────────────────────────────────────────────────────┐
│ BƯỚC 1: XEM TRƯỚC BÁO GIÁ (CHK-01 / R75)               │
│ POST /api/v1/checkouts/quote                           │
│ Input: Món hàng chọn + Địa chỉ nhận + Voucher          │
│ Output: Chi tiết từng Shop + Phí ship GHN + quoteId    │
└──────────────────────────┬─────────────────────────────┘
                           │ (Người mua bấm "Đặt hàng")
                           ▼
┌────────────────────────────────────────────────────────┐
│ BƯỚC 2: TẠO ĐƠN HÀNG CHÍNH THỨC (CHK-02 / R76)         │
│ POST /api/v1/orders                                    │
│ Input: quoteId + Phương thức thanh toán + Địa chỉ      │
│ Output: orderId + Danh sách subOrderIds                │
└────────────────────────────────────────────────────────┘
```

---

### `CHK-01 / R75`: Khảo Giá Thanh Toán & Tính Cước Ship GHN
* **Endpoint:** `POST /api/v1/checkouts/quote`
* **Quyền:** `buyer`
* **Request Body:**
  ```json
  {
    "cartItemIds": [
      "i1111111-1111-1111-1111-000000000001"
    ],
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
* **Cơ chế kỹ thuật:**
  1. Phân nhóm các món theo `SellerId`.
  2. Với mỗi shop, gọi Fulfillment `:8006` (`POST /api/v1/internal/shipping/quote`) để tính cước GHN động.
  3. Gọi Wallet `:8008` (`GET /api/v1/wallets/me`) lấy số dư ví khả dụng hiện tại.
  4. Lưu cache vào `IDistributedCache` với key `checkout_quote:{quoteId}` và `TTL = 15 phút`.
* **Response (HTTP 200 OK):**
  ```json
  {
    "quoteId": "e2a1c4b8-9999-4d8e-b123-5a6b7c8d9e0f",
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

---

### `CHK-02 / R76`: Tạo Đơn Hàng Chính Thức
* **Endpoint:** `POST /api/v1/orders`
* **Quyền:** `buyer`
* **Request Body:**
  ```json
  {
    "quoteId": "e2a1c4b8-9999-4d8e-b123-5a6b7c8d9e0f",
    "paymentMethod": "Wallet",
    "shippingAddress": "123 Le Loi, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh",
    "contactPhone": "0901234567"
  }
  ```
* **Các chốt chặn bảo vệ sống còn (Resilience Shield):**
  1. **Atomic Pop**: Đọc và xóa tức thì key `checkout_quote:{quoteId}` khỏi Cache để triệt tiêu Race Condition và lỗi trừ tiền 2 lần khi bấm liên tiếp. Nếu không tìm thấy $\rightarrow$ Báo lỗi `QUOTE_EXPIRED` (400).
  2. **Asymmetric Price Drift Policy**: Quét lại giá Catalog `:8002` qua `POST /api/v1/internal/variants`. Nếu sản phẩm **Tăng giá** $\rightarrow$ Hard block HTTP 409 `PRICE_CHANGED` và cập nhật giá mới vào Cart. Nếu sản phẩm **Giảm giá** $\rightarrow$ Tự động áp dụng giá rẻ hơn (`appliedPriceDrops`).
  3. **Giữ kho 15 phút**: Gọi Fulfillment `:8006` (`POST /api/v1/internal/inventories/reservations`).
  4. **Thanh toán Ví**: Gọi Wallet `:8008` (`POST /api/v1/internal/wallet/orders/{orderId}/payment`). Thành công $\rightarrow$ Ghi nhận `TransactionId`, gọi Fulfillment trừ kho vĩnh viễn (`CommitStockAsync`), đơn chuyển `Paid`.
  5. **Compensation Circuit Breaker**: Nếu `SaveChangesAsync()` phát sinh lỗi kết nối DB $\rightarrow$ Tự động gọi Fulfillment nhả kho và gọi Wallet hoàn tiền ví 100% với idempotency key `compensation-db-{orderId}`.
* **Response (HTTP 201 Created):**
  ```json
  {
    "orderId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
    "orderNo": "ORD-20261105-9A0B1C",
    "totalAmount": 530000.0,
    "status": "Paid",
    "paymentDeadlineAt": "2026-11-05T10:15:00Z",
    "subOrderIds": [
      "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08"
    ],
    "appliedPriceDrops": null
  }
  ```

---

# PHẦN 4: BOUNDED CONTEXT 3 — VÒNG ĐỜI ĐƠN HÀNG & VẬN CHUYỂN (`ORD-01` .. `ORD-13`)

### Bảng tra cứu API Đơn hàng & Vận chuyển (`OrdersController`):

| Mã | Method | Path | Tên nghiệp vụ | Quyền |
| :-: | :-: | - | - | :-: |
| **`ORD-01 / R78`** | `GET` | `/api/v1/orders` | Danh sách lịch sử đơn hàng của tôi (phân trang DB) | `buyer` |
| **`ORD-02 / R77`** | `GET` | `/api/v1/orders/{orderId}` | Chi tiết đơn hàng cha (kèm Passive Guard) | `buyer`, `admin` |
| **`ORD-02B / R79`**| `GET` | `/api/v1/sub-orders/{subOrderId}` | Chi tiết đơn con của Shop (Single DTO) | `buyer`, `seller`, `admin` |
| **`ORD-02C`** | `GET` | `/api/v1/sub-orders` | Danh sách đơn con của Shop (phân trang DB) | `seller` |
| **`ORD-04 / R83`** | `POST` | `/api/v1/orders/{orderId}/accept` | Shop tiếp nhận đơn con (`Confirmed` + tạo vận đơn GHN) | `seller` |
| **`ORD-05 / R84`** | `POST` | `/api/v1/orders/{orderId}/reject` | Shop từ chối đơn con (Nhả kho mục tiêu + hoàn tiền ví tự động) | `seller` |
| **`ORD-06 / R80`** | `POST` | `/api/v1/orders/{orderId}/cancel` | Người mua hủy đơn đang chờ thanh toán | `buyer` |
| **`ORD-07`** | `POST` | `/api/v1/sub-orders/{subOrderId}/confirm-receipt` | Người mua xác nhận nhận hàng & giải ngân ví Seller | `buyer` |
| **`ORD-08 / R87`** | `GET` | `/api/v1/shipments/order/{orderId}` | Tra cứu danh sách vận đơn và tiến độ giao hàng | `buyer`, `seller` |
| **`ORD-13 / R99`** | `POST` | `/api/v1/sub-orders/{subOrderId}/refund` | Khởi tạo yêu cầu hoàn tiền cho đơn con | `buyer` |
| **`LEGACY`** | `GET` | `/api/v1/orders/buyer/{buyerId}` | Tra cứu đơn hàng theo `buyerId` (Tương thích ngược) | `buyer`, `admin` |

---

### Chi tiết Đặc tả Các Endpoint Đơn Hàng:

#### `ORD-01 / R78`: Danh Sách Lịch Sử Đơn Hàng (Phân Trang DB)
* **Endpoint:** `GET /api/v1/orders?page=1&pageSize=20&status=Paid`
* **Response (HTTP 200 OK):**
  ```json
  {
    "items": [
      {
        "id": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
        "orderNo": "ORD-6A1B2C3D",
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

#### `ORD-02 / R77`: Chi Tiết Đơn Hàng Cha (Master Order)
* **Endpoint:** `GET /api/v1/orders/{orderId}`
* **Cơ chế:** Kích hoạt Passive Guard tự động hủy đơn, nhả kho và giải phóng voucher nếu đơn `PendingPayment` quá hạn 15 phút. Thẩm định quyền sở hữu `order.BuyerId == currentUserId`.

#### `ORD-02B / R79`: Chi Tiết Đơn Con Của Shop (Single DTO)
* **Endpoint:** `GET /api/v1/sub-orders/{subOrderId}`
* **Response (HTTP 200 OK):**
  ```json
  {
    "id": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "subOrderNo": "SUB-20261105-8B0C2D",
    "orderId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
    "buyerId": "11111111-1111-1111-1111-000000000004",
    "sellerId": "shop1111-1111-1111-1111-000000000001",
    "shopName": "Shop shop1111",
    "status": "Paid",
    "paymentMethod": "Wallet",
    "shippingAddress": {
      "receiverName": "Nguyen Van A",
      "contactPhone": "0901234567",
      "street": "123 Le Loi",
      "ward": "Phường Bến Nghé",
      "district": "Quận 1",
      "province": "TP. Hồ Chí Minh",
      "ghnDistrictId": 1442,
      "ghnWardCode": "20101",
      "fullAddressText": "123 Le Loi, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh"
    },
    "contactPhone": "0901234567",
    "itemsAmount": 500000.0,
    "productDiscountAmount": 0.0,
    "shopDiscountAmount": 0.0,
    "systemDiscountAmount": 0.0,
    "discountAmount": 0.0,
    "shippingFeeAmount": 30000.0,
    "totalAmount": 530000.0,
    "createdAt": "2026-11-05T10:00:00Z",
    "items": [
      {
        "id": "c2d3e4f5-9999-4a0b-9c1d-3e4f5a6b7c09",
        "productId": "p1111111-1111-1111-1111-000000000001",
        "skuId": "s1111111-1111-1111-1111-000000000001",
        "productName": "Áo Thun Cotton Nexus",
        "skuCode": "AT-BLK-L",
        "quantity": 2,
        "unitPrice": 250000.0,
        "lineTotal": 500000.0
      }
    ]
  }
  ```

#### `ORD-04 / R83`: Shop Tiếp Nhận Đơn Con
* **Endpoint:** `POST /api/v1/orders/{orderId}/accept`
* **Request Body:** `{ "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08" }`
* **Quy tắc:** Chuyển `SubOrder.Status = Confirmed`, tự động gọi Fulfillment `:8006` (`POST /api/v1/internal/shipments`) tạo vận đơn GHN.
* **Response (HTTP 200 OK):**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "status": "Confirmed",
    "acceptedAt": "2026-11-05T10:05:00Z"
  }
  ```

#### `ORD-05 / R84`: Shop Từ Chối Đơn Con
* **Endpoint:** `POST /api/v1/orders/{orderId}/reject`
* **Request Body:**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "reasonCode": "OutOfStock",
    "reasonText": "Hết màu đen size L"
  }
  ```
* **Cơ chế kỹ thuật:**
  1. Chuyển `SubOrder.Status = Cancelled`. Đơn của các shop khác vẫn giữ nguyên vẹn.
  2. Nếu đơn đã thanh toán, tự động tạo bản ghi `OrderRefund` (Status: `Requested`).
  3. **Targeted Stock Release**: Gọi Fulfillment Service:
     `POST /api/v1/internal/inventories/reservations/{orderId}/release-items`
     Payload: `{ "items": [{ "skuId": "s1111111-...", "quantity": 2 }], "reason": "SubOrderRejected" }`.
* **Response (HTTP 200 OK):**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "status": "Cancelled",
    "cancellationReason": "OutOfStock",
    "rejectedAt": "2026-11-05T10:05:00Z"
  }
  ```

#### `ORD-06 / R80`: Người Mua Hủy Đơn Chờ Thanh Toán
* **Endpoint:** `POST /api/v1/orders/{orderId}/cancel`
* **Request Body:** `{ "reasonCode": "BuyerChangedMind", "reasonText": "Muốn đổi địa chỉ khác" }`
* **Response (HTTP 200 OK):** `{ "orderId": "...", "status": "Cancelled", "cancelledAt": "..." }`

#### `ORD-08 / R87`: Theo Dõi Vận Chuyển Đơn Hàng
* **Endpoint:** `GET /api/v1/shipments/order/{orderId}`
* **Response (HTTP 200 OK):**
  ```json
  {
    "orderId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
    "orderNo": "ORD-6A1B2C3D",
    "shipments": [
      {
        "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
        "subOrderNo": "SUB-20261105-8B0C2D",
        "sellerId": "shop1111-1111-1111-1111-000000000001",
        "shopName": "Shop shop1111",
        "status": "Shipping",
        "carrierCode": "GHN",
        "trackingCode": "GHN-SUB-20261105-8B0C2D",
        "shippedAt": "2026-11-05T11:00:00Z",
        "deliveredAt": null
      }
    ]
  }
  ```

#### `ORD-13 / R99`: Khởi Tạo Yêu Cầu Hoàn Tiền Đơn Con
* **Endpoint:** `POST /api/v1/sub-orders/{subOrderId}/refund`
* **Request Body:** `{ "reason": "DamagedItem", "note": "Sản phẩm bị lỗi mép" }`
* **Response (HTTP 200 OK):** `{ "subOrderId": "...", "status": "RefundRequested", "requestedAt": "..." }`

---

# PHẦN 5: WEBHOOK TIẾP NHẬN TRẠNG THÁI GIAO VẬN

### Endpoint: `POST /api/v1/sub-orders/{subOrderId}/delivery-status`
* **Bảo mật:** Bắt buộc Header `X-Internal-Service-Key: <secret>`. Nếu thiếu hoặc không khớp $\rightarrow$ Trả HTTP 403 `FORBIDDEN`.
* **Request Body:**
  ```json
  {
    "status": "Delivered",
    "deliveredAt": "2026-11-05T14:30:00Z"
  }
  ```
* **Ma trận 9 trạng thái giao vận từ Carrier:**
  1. `ReadyToPick`, `Picking`, `Packed`: Cập nhật timestamp chuẩn bị hàng (Idempotent 200 OK).
  2. `HandedOver`, `InTransit`, `Shipped`: Chuyển `SubOrderStatus.Shipped`, ghi nhận `ShippedAt`.
  3. `Delivered`: Chuyển `SubOrderStatus.Delivered`, ghi nhận `DeliveredAt`.
  4. `Cancelled`: Chuyển `SubOrderStatus.Cancelled`, ghi nhận `CancelledAt`.
  5. `DeliveryFailed`, `Returned`: Chuyển `SubOrderStatus.Cancelled`, tự động ghi nhận bản ghi `OrderCancellation` (Lý do: `DeliveryFailed` hoặc `PackageReturned`) và tự động tạo `OrderRefund` (Status: `Requested`) nếu đơn đã thanh toán.
* **Response (HTTP 200 OK):**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "status": "Delivered",
    "updatedAt": "2026-11-05T14:30:00Z"
  }
  ```

---

# PHẦN 6: HỢP ĐỒNG OUTBOUND TYPED REST CLIENTS (GỌI LIÊN DỊCH VỤ)

Commerce Service gọi ra bên ngoài thông qua các Typed HTTP Clients được cấu hình tại `Commerce.Infrastructure/DependencyInjection.cs`:

### 6.1. Catalog Service (`:8002` REST) — `ICatalogClient`
1. **Batch tra cứu thông tin biến thể SKU (`POST /api/v1/internal/variants`):**
   * Payload: `{ "variantIds": ["guid-1", "guid-2"] }` (Tối đa 100 IDs/chunk).
   * Response: `{ "items": [{ "variantId", "productId", "sellerId", "productName", "price", "isPurchasable" }] }`.
2. **Kiểm tra tính hợp lệ khi đổi biến thể (`POST /api/v1/internal/catalog/skus/validate-variant-switch`):**
   * Payload: `{ "currentSkuId": "guid", "newSkuId": "guid" }`.
   * Response: `{ "isValid": true, "productId": "guid", "newUnitPrice": 250000.0, "isPurchasable": true }`.

### 6.2. Fulfillment Service (`:8006` REST) — `IFulfillmentClient`
1. **Kiểm tra tồn kho đơn lẻ:** `GET /api/v1/inventories/{skuId}`
2. **Batch kiểm tra tồn kho giỏ hàng:** `POST /api/v1/inventories/by-skus` (Payload: `["skuId1", "skuId2"]`)
3. **Giữ kho tạm thời 15 phút:** `POST /api/v1/internal/inventories/reservations`
   * Payload: `{ "referenceId": "orderId", "holdMinutes": 15, "items": [{ "skuId", "quantity" }] }`
4. **Trừ kho vĩnh viễn (Commit):** `POST /api/v1/internal/inventories/reservations/{orderId}/commit`
5. **Nhả tồn kho khi hủy đơn cha:** `POST /api/v1/internal/inventories/reservations/{orderId}/release?reason=OrderCancelled`
6. **Batch nhả kho cho nhiều đơn quá hạn:** `POST /api/v1/internal/inventories/reservations/batch-release`
   * Payload: `{ "orderIds": ["orderId1", "orderId2"] }`
7. **Nhả kho có mục tiêu cho SubOrder bị từ chối:** `POST /api/v1/internal/inventories/reservations/{orderId}/release-items`
   * Payload: `{ "items": [{ "skuId", "quantity" }], "reason": "SubOrderRejected" }`
8. **Tính cước vận chuyển GHN động:** `POST /api/v1/internal/shipping/quote`
   * Payload: `{ "sellerId": "guid", "toDistrictId": 1442, "toWardCode": "20101", "items": [{ "skuId", "quantity", "weightGrams": 200 }] }`
   * Response: `{ "feeAmount": 30000.0 }`
9. **Tạo vận đơn GHN tự động:** `POST /api/v1/internal/shipments`
   * Payload: `{ "subOrderId": "guid" }`

### 6.3. Wallet Service (`:8008` REST) — `IWalletClient`
1. **Trừ tiền ví trực tiếp theo đơn hàng:** `POST /api/v1/internal/wallet/orders/{orderId}/payment`
   * Headers: `Authorization: Bearer <token>`, `x-user-id: <guid>`
   * Payload: `{ "amount": 530000.0 }`
   * Response: `{ "id": "tx-guid", "balanceAfter": 970000.0, "status": "Success" }`
2. **Lấy số dư ví thời gian thực:** `GET /api/v1/wallets/me`
   * Response: `{ "balance": 1500000.0 }`

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

# PHẦN 8: BỘ TYPESCRIPT TYPE DEFINITIONS HOÀN CHỈNH (FRONTEND DTOs)

Dành cho Frontend Web/Mobile và SDK Client:

```typescript
// =============================================================================
// COMMERCE SERVICE FRONTEND CONTRACT TYPES
// =============================================================================

export type PaymentMethod = 'Cod' | 'Wallet' | 'VNPay';
export type OrderStatus = 'PendingPayment' | 'Paid' | 'Processing' | 'Completed' | 'Cancelled';
export type SubOrderStatus = 'PendingPayment' | 'Paid' | 'Confirmed' | 'Shipping' | 'Delivered' | 'Completed' | 'Cancelled';
export type CancellationReasonCode = 'BuyerChangedMind' | 'OutOfStock' | 'PaymentTimeout' | 'SellerRejected' | 'DeliveryFailed' | 'PackageReturned' | 'Other';

// -----------------------------------------------------------------------------
// 1. Giỏ hàng (Cart)
// -----------------------------------------------------------------------------
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

export interface CartSummaryResponse {
  cartId: string;
  totalItemCount: number;
  subtotalAmount: number;
  hasUnavailableItems: boolean;
}

export interface ShopCartGroupResponse {
  sellerId: string;
  shopName: string;
  shopSubtotal: number;
  items: CartItemDto[];
}

export interface GroupedCartResponse {
  cartId: string;
  shops: ShopCartGroupResponse[];
  grandSubtotal: number;
  totalItemCount: number;
}

// -----------------------------------------------------------------------------
// 2. Báo giá & Đặt hàng (Checkout)
// -----------------------------------------------------------------------------
export interface ShippingAddressInput {
  receiverName: string;
  contactPhone: string;
  street: string;
  ward: string;
  district: string;
  province: string;
  ghnDistrictId?: number | null;
  ghnWardCode?: string | null;
}

export interface CheckoutQuoteRequest {
  cartItemIds: string[];
  shippingAddress: ShippingAddressInput;
  contactPhone: string;
  paymentMethod: PaymentMethod;
  voucherCodes?: string[];
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

export interface PriceDropDto {
  skuId: string;
  productName: string;
  quotedUnitPrice: number;
  currentUnitPrice: number;
  savingsAmount: number;
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

// -----------------------------------------------------------------------------
// 3. Đơn hàng & Vận chuyển (Orders & Shipments)
// -----------------------------------------------------------------------------
export interface ShippingAddressSnapshot {
  receiverName: string;
  contactPhone: string;
  street: string;
  ward: string;
  district: string;
  province: string;
  ghnDistrictId: number;
  ghnWardCode: string;
  fullAddressText: string;
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

export interface SubOrderDetailResponse {
  id: string;
  subOrderNo: string;
  orderId: string;
  buyerId: string;
  sellerId: string;
  shopName: string;
  status: SubOrderStatus;
  paymentMethod: PaymentMethod;
  shippingAddress: ShippingAddressSnapshot;
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

export interface OrderSummaryResponse {
  id: string;
  orderNo: string;
  totalAmount: number;
  status: string;
  paymentDeadlineAt: string;
  subOrderCount: number;
  createdAt: string;
}

export interface SubOrderShipmentInfo {
  subOrderId: string;
  subOrderNo: string;
  sellerId: string;
  shopName: string;
  status: string;
  carrierCode?: string;
  trackingCode?: string;
  shippedAt?: string | null;
  deliveredAt?: string | null;
}

export interface ShipmentTrackingResponse {
  orderId: string;
  orderNo: string;
  shipments: SubOrderShipmentInfo[];
}

export interface DeliveryStatusWebhookRequest {
  status: string;
  deliveredAt?: string | null;
}
```
