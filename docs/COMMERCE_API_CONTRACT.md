# COMMERCE SERVICE — MASTER REST API CONTRACT & INTEGRATION SPECIFICATION
## (Hợp Đồng Giao Tiếp RESTful Toàn Diện: Giỏ Hàng, Báo Giá, Đơn Hàng, Vận Chuyển & Liên Dịch Vụ)

> **Phiên bản:** v3.3.0 (Master Unified REST Contract)  
> **Phân hệ:** Commerce Service (`Commerce.Api`)  
> **Cổng dịch vụ (Port):** `8003` (Container & Localhost)  
> **Cơ sở dữ liệu:** PostgreSQL port `5435` (`commerce_db`)  
> **Giao thức:** HTTP/1.1 & HTTP/2 RESTful Web API  
> **Đối tượng sử dụng:** Frontend (Web & Mobile), API Gateway Team, Wallet Service Team, Catalog Service Team, Fulfillment Service Team, QA Engineers, DevOps & Software Engineers  
> **Cập nhật lần cuối:** 2026-11-09  
> **Trạng thái:** ✅ ĐÃ CHUẨN HÓA 100% VỚI CODE THỰC THI (100% RESTFUL, PURE DTOS, PASS 100% TESTS)  

---

## MỤC LỤC BỐ CỤC TÀI LIỆU

1. [Phần 1: Quy chuẩn Kiến trúc & Giao thức Chung](#phần-1-quy-chuẩn-kiến-trúc--giao-thức-chung)
   * *1.1 Bounded Context & Phân định Ranh giới*
   * *1.2 Cơ chế Identity Propagation & Headers từ API Gateway*
   * *1.3 Quy tắc Đóng gói Phong bì (Response Envelope Conventions)*
   * *1.4 Quy tắc Vàng về Casing & Định danh Trường Tài chính*
2. [Phần 2: Bounded Context 1 — Quản lý Giỏ hàng (`CART-01` .. `CART-14`)](#phần-2-bounded-context-1--quản-lý-giỏ-hàng-cart-01--cart-14)
   * *Nhóm 1: Xem & Khám phá Giỏ hàng (`CART-01` .. `CART-03`)*
   * *Nhóm 2: Thao tác Dòng Sản phẩm (`CART-04` .. `CART-09`)*
   * *Nhóm 3: Tính giá, Thẩm định & Gộp giỏ (`CART-10` .. `CART-12`)*
   * *Nhóm 4: Hậu Đặt hàng & Quản trị (`CART-13` .. `CART-14`)*
3. [Phần 3: Bounded Context 2 — Báo giá & Đặt hàng 2 Bước (`CHK-01` .. `CHK-02`)](#phần-3-bounded-context-2--báo-giá--đặt-hàng-2-bước-chk-01--chk-02)
   * *Bước 1: Báo giá Giỏ hàng Đa Shop (`CHK-01` / `R75`)*
   * *Bước 2: Tạo Đơn hàng Chính thức (`CHK-02` / `R76`)*
4. [Phần 4: Bounded Context 3 — Vòng đời Đơn hàng & Vận chuyển (`ORD-01` .. `ORD-13`)](#phần-4-bounded-context-3--vòng-đời-đơn-hàng--vận-chuyển-ord-01--ord-13)
   * *Tra cứu Đơn hàng (`ORD-01`, `ORD-02`, `ORD-02B`, `ORD-02C`, `LEGACY`)*
   * *Xử lý & Chuyển trạng thái Đơn (`ORD-04`, `ORD-05`, `ORD-06`)*
   * *Xác nhận nhận hàng & Giải ngân Escrow (`ORD-07`)*
   * *Vận chuyển & Hoàn tiền (`ORD-08`, `ORD-13`)*
5. [Phần 5: Webhook Tiếp Nhận Trạng Thái Giao Vận (Carrier ➔ Fulfillment ➔ Commerce)](#phần-5-webhook-tiếp-nhận-trạng-thái-giao-vận)
6. [Phần 6: Hợp đồng Outbound Typed REST Clients (Gọi Liên Dịch Vụ)](#phần-6-hợp-đồng-outbound-typed-rest-clients-gọi-liên-dịch-vụ)
   * *6.1 Catalog Service (`:8002` REST)*
   * *6.2 Fulfillment Service (`:8006` REST)*
   * *6.3 Wallet Service (`:8008` REST)*
7. [Phần 7: Bảng Tra cứu Mã Lỗi Hệ thống Chuẩn RFC 9110](#phần-7-bảng-tra-cứu-mã-lỗi-hệ-thống-chuẩn-rfc-9110)
8. [Phần 8: Bộ TypeScript Type Definitions Hoàn chỉnh (Frontend & SDK DTOs)](#phần-8-bộ-typescript-type-definitions-hoàn-chỉnh-frontend--sdk-dtos)

---

# PHẦN 1: QUY CHUẨN KIẾN TRÚC & GIAO THỨC CHUNG

### 1.1. Bounded Context & Phân Định Ranh Giới

Commerce Service chịu trách nhiệm độc quyền:
1. **Quản lý Giỏ hàng (`Carts`, `CartItems`)**: Mỗi Người mua (Buyer) sở hữu tối đa 1 Giỏ hàng `Active`. Giới hạn tối đa **50 món** (distinct items) và tối đa **99 đơn vị/món**.
2. **Đơn hàng cha & Đơn con theo Shop (`Orders`, `SubOrders`, `SubOrderItems`)**:
   - **Đơn hàng cha (Master Order)** quản lý tổng thanh toán, phương thức thanh toán, thời hạn thanh toán (TTL 15 phút hoặc 24 giờ cho đấu giá) và địa chỉ nhận hàng.
   - **Đơn hàng con (Shop Sub-Order)** quản lý gói hàng riêng của từng Người bán (Seller), cước vận chuyển GHN riêng, vận đơn GHN riêng, và quy trình xử lý độc lập.
3. **Snapshot Bất biến (Immutable Snapshots)**:
   - `UnitPriceSnapshot`: Ghi nhận đơn giá tại thời điểm thêm vào giỏ để đối chiếu biến động giá (`Price Drift`).
   - `ShippingAddressSnapshot`: Lưu trữ thông tin người nhận (Họ tên, SĐT, Địa chỉ, Mã bưu cục GHN) dưới dạng cột PostgreSQL Native `JSONB` trong bảng `Orders`. Khi người dùng chỉnh sửa hoặc xóa sổ địa chỉ tài khoản cá nhân, thông tin lịch sử của đơn hàng đã tạo **tuyệt đối không bị thay đổi**.
4. **Các phân hệ KHÔNG thuộc sở hữu của Commerce Service (External Collaborations)**:
   - **Số dư tiền, Trừ tiền ví, Hoàn tiền ví & Giải ngân Escrow** ➔ Do **Wallet Service** (`:8008`) sở hữu (gọi qua `IWalletClient`).
   - **Thông tin sản phẩm niêm yết (Master Data, Tên, Giá bán, Trạng thái bán)** ➔ Do **Catalog Service** (`:8002`) sở hữu (gọi qua `ICatalogClient`).
   - **Tồn kho vật lý, Giữ hàng 15m, Trừ/Nhả kho & Vận chuyển GHN** ➔ Do **Fulfillment Service** (`:8006`) sở hữu (gọi qua `IFulfillmentClient`).

---

### 1.2. Cơ Chế Identity Propagation & Headers từ API Gateway

Toàn bộ các yêu cầu từ Frontend (Web/Mobile) gửi qua API Gateway được middleware tiếp nhận, trích xuất và chuyển tiếp:

```
[ Frontend Client ] 
       │ HTTPS Request (Bearer JWT)
       ▼
[ API Gateway (:80 / :443) ]
       │ Headers: x-user-id, x-user-roles, Authorization
       ▼
[ UserContextMiddleware (:8003) ] ──▶ Thiết lập ClaimsPrincipal trên HttpContext.User
       │
[ Commerce Controllers ]          ──▶ CartsController / OrdersController (Thin Controllers)
       │
[ IdentityPropagationHandler ]    ──▶ Tự động chèn Headers sang Outbound HttpClients
       ▼
[ External Microservices ] (Catalog :8002, Fulfillment :8006, Wallet :8008)
```

| Header Name | Kiểu Dữ Liệu | Bắt Buộc | Diễn Giải & Nguồn Trích Xuất | Ví Dụ |
| - | - | :-: | - | - |
| `x-user-id` | UUID (v4) | **Có** | ID người dùng xác thực từ Claim `sub` | `11111111-1111-1111-1111-000000000004` |
| `x-user-roles` | String | **Có** | Danh sách vai trò người dùng (`buyer`, `seller`, `admin`, `staff`) | `buyer` hoặc `buyer,seller` |
| `Authorization` | String | Không | JWT Bearer token truyền từ Gateway | `Bearer eyJhbGciOi...` |
| `x-user-email` | String | Không | Email người dùng phục vụ log kiểm toán | `buyer@nexus.local` |
| `x-trace-id` | String | Không | W3C / OpenTelemetry Distributed Trace ID | `00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01` |
| `X-Internal-Service-Key` | String | Nội bộ | Khóa xác thực liên dịch vụ nội bộ (Webhook) | `<secret-key>` |

---

### 1.3. Quy Tắc Đóng Gói Phong Bì (Response Envelope Conventions)

* **Tại tầng Controller C# (Nội bộ Commerce Service)**: Controller **CHỈ TRẢ VỀ PURE DTO** (DTO thuần túy) kèm HTTP Status Code chuẩn RFC. Tuyệt đối không bọc lặp `ApiResponse<T>` trong code backend.
* **Tại tầng API Gateway**: Gateway tự động bọc vỏ phong bì chuẩn hóa trước khi trả về cho Frontend Internet:
  ```json
  {
    "success": true,
    "data": { ... },
    "error": null,
    "timestamp": "2026-11-09T10:00:00Z",
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
  }
  ```
* **Khi xảy ra lỗi nghiệp vụ / hệ thống (RFC 9110 Problem Details)**:
  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    "title": "Bad Request",
    "status": 400,
    "errorCode": "QUOTE_EXPIRED",
    "detail": "Checkout quote has expired, is invalid, or is currently being processed.",
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
  }
  ```

---

### 1.4. Quy Tắc Vàng về Casing & Định Danh Trường Tài Chính

1. **Mã lỗi hệ thống (`errorCode`)**: 100% `SCREAMING_SNAKE_CASE` (ví dụ: `CART_ITEM_NOT_FOUND`, `ORDER_NOT_FOUND`, `VAL_INVALID_INPUT`, `PRICE_CHANGED`, `INSUFFICIENT_STOCK`). Tuyệt đối cấm tiền tố `ERR_`. Tiền tố `VAL_` chỉ dành riêng cho lỗi validation thuộc tính request.
2. **Enum Dữ liệu nghiệp vụ**: 100% `PascalCase` (ví dụ: `"PendingPayment"`, `"Confirmed"`, `"Shipped"`, `"Completed"`, `"Cancelled"`, `"Wallet"`, `"VnPay"`, `"Cod"`). Cấm chuỗi in hoa toàn bộ và cấm trả về số nguyên.
3. **Thuộc tính JSON**: 100% `camelCase` (ví dụ: `cartItemId`, `subOrderNo`, `totalAmount`, `sellerReceiveAmount`).
4. **Khóa cứng Trường Tiền tệ**: 100% sử dụng **`shippingFeeAmount`** trong các DTO thanh toán chi tiết.
   * $\text{totalAmount} = \text{itemsAmount} - \text{discountAmount} + \text{shippingFeeAmount}$
   * $\text{discountAmount} = \text{productDiscountAmount} + \text{shopDiscountAmount} + \text{systemDiscountAmount}$
   * $\text{sellerReceiveAmount} = \text{totalAmount} - \text{platformFee} + \text{shippingFeeAmount}$

---

# PHẦN 2: BOUNDED CONTEXT 1 — QUẢN LÝ GIỎ HÀNG (`CART-01` .. `CART-14`)

### Bảng Tra Cứu 14 API Giỏ Hàng (`CartsController` — `/api/v1/carts`):

| Mã | Method | Path | Tên Nghiệp Vụ | Quyền (Role) |
| :-: | :-: | - | - | :-: |
| **`CART-01`** | `GET` | `/api/v1/carts/me` | Lấy hoặc tự động tạo giỏ hàng người mua | `buyer` |
| **`CART-02`** | `GET` | `/api/v1/carts/me/summary` | Badge số lượng & tổng tiền tạm tính cho Header | `buyer` |
| **`CART-03`** | `GET` | `/api/v1/carts/me/grouped` | Giỏ hàng gom nhóm theo Shop (Multi-Vendor Marketplace) | `buyer` |
| **`CART-04`** | `POST` | `/api/v1/carts/me/items` | Thêm sản phẩm (SKU) vào giỏ hàng | `buyer` |
| **`CART-05`** | `PATCH` | `/api/v1/carts/me/items/{cartItemId}` | Cập nhật số lượng của dòng sản phẩm (1–99) | `buyer` |
| **`CART-06`** | `PATCH` | `/api/v1/carts/me/items/{cartItemId}/variant` | Đổi phân loại hàng trực tiếp trong giỏ (Smart Merge Invariant) | `buyer` |
| **`CART-07`** | `DELETE` | `/api/v1/carts/me/items/{cartItemId}` | Xóa một dòng sản phẩm khỏi giỏ hàng | `buyer` |
| **`CART-08`** | `POST` | `/api/v1/carts/me/items:bulk-remove` | Xóa hàng loạt món được tick chọn trong giỏ | `buyer` |
| **`CART-09`** | `DELETE` | `/api/v1/carts/me/clear` | Dọn sạch toàn bộ giỏ hàng | `buyer` |
| **`CART-10`** | `POST` | `/api/v1/carts/me/calculate` | Tính tiền tạm tính các món tick chọn (Shopee Checkbox) | `buyer` |
| **`CART-11`** | `POST` | `/api/v1/carts/me/validate` | Kiểm tra tồn kho & biến động giá trước checkout | `buyer` |
| **`CART-12`** | `POST` | `/api/v1/carts/me/merge` | Gộp giỏ hàng khách vãng lai sau khi đăng nhập | `buyer` |
| **`CART-13`** | `POST` | `/api/v1/carts/me/cleanup-checked-out` | Dọn dẹp món đã mua sau khi thanh toán thành công | `buyer` |
| **`CART-14`** | `GET` | `/api/v1/carts/active?buyerId={guid}` | Quản trị tra cứu giỏ hàng đang hoạt động của Buyer | `admin`, `staff`, `buyer` |

---

### Chi Tiết Đặc Tả Kỹ Thuật 14 Endpoints Giỏ Hàng:

#### `CART-01`: Lấy hoặc Tự Động Tạo Giỏ Hàng Người Mua
* **Endpoint:** `GET /api/v1/carts/me`
* **Quyền:** `buyer`, `admin`, `staff`
* **Mô tả:** Nếu người mua chưa có giỏ hàng, hệ thống tự động khởi tạo mới giỏ hàng ở trạng thái `Active`. Đồng thời batch kiểm tra tồn kho tại Fulfillment để cập nhật cờ `isAvailable`.
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
        "productId": "p1111111-1111-1111-1111-000000000001",
        "skuId": "s1111111-1111-1111-1111-000000000001",
        "sellerId": "shop1111-1111-1111-1111-000000000001",
        "productName": "Áo Thun Cotton Nexus",
        "quantity": 2,
        "unitPriceSnapshot": 250000.0,
        "lineTotal": 500000.0,
        "addedAt": "2026-11-09T08:00:00Z",
        "isAvailable": true,
        "disabledReason": null
      }
    ],
    "lastActivityAt": "2026-11-09T08:00:00Z"
  }
  ```

#### `CART-02`: Lấy Badge Tóm Tắt Giỏ Hàng
* **Endpoint:** `GET /api/v1/carts/me/summary`
* **Quyền:** `buyer`
* **Response (HTTP 200 OK):**
  ```json
  {
    "cartId": "c1111111-1111-1111-1111-000000000001",
    "totalDistinctItems": 1,
    "totalQuantity": 2,
    "estimatedSubtotal": 500000.0
  }
  ```

#### `CART-03`: Giỏ Hàng Gom Nhóm Theo Shop
* **Endpoint:** `GET /api/v1/carts/me/grouped`
* **Quyền:** `buyer`
* **Response (HTTP 200 OK):**
  ```json
  {
    "cartId": "c1111111-1111-1111-1111-000000000001",
    "buyerId": "11111111-1111-1111-1111-000000000004",
    "totalDistinctItems": 1,
    "totalQuantity": 2,
    "subtotalAmount": 500000.0,
    "shops": [
      {
        "sellerId": "shop1111-1111-1111-1111-000000000001",
        "shopName": "Nexus Official Store",
        "distinctItemCount": 1,
        "totalQuantity": 2,
        "shopSubtotal": 500000.0,
        "items": [
          {
            "id": "i1111111-1111-1111-1111-000000000001",
            "productId": "p1111111-1111-1111-1111-000000000001",
            "skuId": "s1111111-1111-1111-1111-000000000001",
            "sellerId": "shop1111-1111-1111-1111-000000000001",
            "productName": "Áo Thun Cotton Nexus",
            "quantity": 2,
            "unitPriceSnapshot": 250000.0,
            "lineTotal": 500000.0,
            "addedAt": "2026-11-09T08:00:00Z",
            "isAvailable": true,
            "disabledReason": null
          }
        ]
      }
    ]
  }
  ```

#### `CART-04`: Thêm Sản Phẩm Vào Giỏ Hàng
* **Endpoint:** `POST /api/v1/carts/me/items`
* **Request Body:**
  ```json
  {
    "skuId": "s1111111-1111-1111-1111-000000000001",
    "quantity": 2,
    "productId": "p1111111-1111-1111-1111-000000000001"
  }
  ```
* **Quy tắc & Ràng buộc:**
  1. Gọi `CatalogClient.GetSkuDetailsAsync` để kiểm tra SKU có tồn tại, lấy `UnitPrice` hiện hành, xác thực `isPurchasable == true`.
  2. Gọi `FulfillmentClient.CheckStockAvailabilityAsync` xác thực tồn kho khả dụng `availableStock >= quantity`.
  3. Nếu SKU đã có trong giỏ, tự động cộng dồn số lượng. Ràng buộc tối đa **99 chiếc/món** (`MAX_QUANTITY_EXCEEDED`) và tối đa **50 món khác nhau trong giỏ** (`MAX_ITEMS_REACHED`).
* **Response (HTTP 200 OK):** Trả về `CartDto` mới nhất.

#### `CART-05`: Cập Nhật Số Lượng Dòng Sản Phẩm
* **Endpoint:** `PATCH /api/v1/carts/me/items/{cartItemId}`
* **Request Body:**
  ```json
  {
    "cartItemId": "i1111111-1111-1111-1111-000000000001",
    "quantity": 3
  }
  ```
* **Response (HTTP 200 OK):** Trả về `CartDto` cập nhật.

#### `CART-06`: Đổi Biến Thể Sản Phẩm Trong Giỏ (Smart Merge)
* **Endpoint:** `PATCH /api/v1/carts/me/items/{cartItemId}/variant`
* **Request Body:**
  ```json
  {
    "newSkuId": "s2222222-2222-2222-2222-000000000002"
  }
  ```
* **Cơ chế Smart Merge Invariant:**
  - Gọi Catalog Service `POST /api/v1/internal/catalog/skus/validate-variant-switch` xác nhận biến thể mới thuộc cùng sản phẩm cha và cùng người bán.
  - Nếu biến thể mới đã có sẵn một dòng khác trong giỏ, hệ thống tự động **gộp số lượng** vào dòng mới (tối đa 99 chiếc) và **xóa bỏ dòng cũ**.
* **Response (HTTP 200 OK):** Trả về `CartDto` cập nhật.

#### `CART-07`: Xóa Một Món Khỏi Giỏ Hàng
* **Endpoint:** `DELETE /api/v1/carts/me/items/{cartItemId}`
* **Response (HTTP 200 OK):** Trả về `CartDto` mới nhất sau khi xóa.

#### `CART-08`: Xóa Hàng Loạt Món Được Tick Chọn
* **Endpoint:** `POST /api/v1/carts/me/items:bulk-remove`
* **Request Body:**
  ```json
  {
    "cartItemIds": [
      "i1111111-1111-1111-1111-000000000001"
    ],
    "clearAll": false
  }
  ```
* **Response (HTTP 200 OK):** Trả về `CartDto` sau khi xóa.

#### `CART-09`: Dọn Sạch Toàn Bộ Giỏ Hàng
* **Endpoint:** `DELETE /api/v1/carts/me/clear`
* **Response (HTTP 200 OK):** Trả về `CartDto` với `itemCount = 0`, `subtotalAmount = 0.0`.

#### `CART-10`: Tính Tiền Tạm Tính Các Món Tick Chọn (Shopee Checkbox)
* **Endpoint:** `POST /api/v1/carts/me/calculate`
* **Request Body:**
  ```json
  {
    "selectedItemIds": [
      "i1111111-1111-1111-1111-000000000001"
    ]
  }
  ```
* **Response (HTTP 200 OK):**
  ```json
  {
    "selectedItemCount": 1,
    "totalQuantity": 2,
    "subtotalAmount": 500000.0,
    "items": [
      {
        "cartItemId": "i1111111-1111-1111-1111-000000000001",
        "skuId": "s1111111-1111-1111-1111-000000000001",
        "productName": "Áo Thun Cotton Nexus",
        "quantity": 2,
        "unitPrice": 250000.0,
        "lineTotal": 500000.0,
        "isAvailable": true,
        "availableStock": 50,
        "stockWarning": null
      }
    ]
  }
  ```

#### `CART-11`: Kiểm Tra Tồn Kho & Biến Động Giá Trước Checkout
* **Endpoint:** `POST /api/v1/carts/me/validate`
* **Request Body:**
  ```json
  {
    "selectedItemIds": [
      "i1111111-1111-1111-1111-000000000001"
    ]
  }
  ```
* **Response (HTTP 200 OK):**
  ```json
  {
    "isValid": true,
    "hasPriceChanges": false,
    "hasStockIssues": false,
    "issues": []
  }
  ```

#### `CART-12`: Gộp Giỏ Hàng Khách Vãng Lai Sau Khi Đăng Nhập
* **Endpoint:** `POST /api/v1/carts/me/merge`
* **Request Body:**
  ```json
  {
    "guestItems": [
      {
        "skuId": "s1111111-1111-1111-1111-000000000001",
        "quantity": 1,
        "productId": "p1111111-1111-1111-1111-000000000001"
      }
    ]
  }
  ```
* **Response (HTTP 200 OK):** Trả về `CartDto` sau gộp.

#### `CART-13`: Dọn Dẹp Món Đã Mua Sau Khi Thanh Toán Thành Công
* **Endpoint:** `POST /api/v1/carts/me/cleanup-checked-out`
* **Request Body:**
  ```json
  {
    "purchasedCartItemIds": [
      "i1111111-1111-1111-1111-000000000001"
    ]
  }
  ```
* **Response (HTTP 200 OK):** Trả về `CartDto` sạch.

#### `CART-14`: Quản Trị Tra Cứu Giỏ Hàng Đang Hoạt Động Của Buyer
* **Endpoint:** `GET /api/v1/carts/active?buyerId={guid}`
* **Quyền:** `admin`, `staff` (hoặc `buyer` tra cứu giỏ của chính mình).
* **Response (HTTP 200 OK):** Trả về `CartDto`.

---

# PHẦN 3: BOUNDED CONTEXT 2 — BÁO GIÁ & ĐẶT HÀNG 2 BƯỚC (`CHK-01` .. `CHK-02`)

```
┌────────────────────────────────────────────────────────────────────────┐
│ BƯỚC 1: XEM TRƯỚC BÁO GIÁ & TÍNH CƯỚC SHIP (CHK-01 / R75)               │
│ POST /api/v1/checkouts/quote                                           │
│ Input: CartItemIds + Địa chỉ giao nhận (GHN) + Phương thức thanh toán  │
│ Output: Chi tiết từng Shop + Phí ship GHN + Số dư ví + quoteId (TTL 15m)│
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ (Người mua bấm "Đặt hàng ngay")
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ BƯỚC 2: TẠO ĐƠN HÀNG CHÍNH THỨC & THANH TOÁN (CHK-02 / R76)             │
│ POST /api/v1/orders                                                    │
│ Input: quoteId + PaymentMethod (Wallet / VnPay / Cod) + Địa chỉ        │
│ Output: orderId + orderNo + subOrderIds + trạng thái Paid             │
└────────────────────────────────────────────────────────────────────────┘
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
    "carrierCode": "GHN",
    "voucherCodes": []
  }
  ```
* **Cơ chế kỹ thuật thực thi:**
  1. Phân nhóm các món theo từng `SellerId` (Shop).
  2. Với mỗi shop, gọi Fulfillment `:8006` (`POST /api/v1/internal/shipping/quote`) để tính cước GHN động. Nếu lỗi mạng, áp dụng Graceful Fallback `30.000đ/Shop`.
  3. Nếu `paymentMethod == "Wallet"`, gọi Wallet `:8008` (`GET /api/v1/wallets/me`) lấy số dư ví khả dụng. Nếu lỗi mạng, fallback về `0đ`.
  4. Lưu cache vào Redis qua `IDistributedCache` với key `checkout_quote:{quoteId}` và `TTL = 15 phút`.
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
    "warnings": null,
    "expiredAt": "2026-11-09T10:15:00Z"
  }
  ```

---

### `CHK-02 / R76`: Tạo Đơn Hàng Chính Thức & Thanh Toán

* **Endpoint:** `POST /api/v1/orders`
* **Quyền:** `buyer`
* **Request Body:**
  ```json
  {
    "quoteId": "e2a1c4b8-9999-4d8e-b123-5a6b7c8d9e0f",
    "paymentMethod": "Wallet",
    "shippingAddress": "123 Le Loi, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh",
    "contactPhone": "0901234567",
    "note": "Giao hàng giờ hành chính"
  }
  ```
* **5 Chốt chặn bảo vệ sống còn (Resilience Shield):**
  1. **Atomic Pop**: Đọc và xóa tức thì key `checkout_quote:{quoteId}` khỏi Cache để triệt tiêu Race Condition và lỗi trừ tiền 2 lần khi khách bấm đúp. Nếu không tìm thấy $\rightarrow$ Báo lỗi `QUOTE_EXPIRED` (400 Bad Request).
  2. **Asymmetric Price Drift Policy**: Batch quét lại giá Catalog `:8002` qua `POST /api/v1/internal/variants`.
     - Nếu sản phẩm **Tăng giá** $\rightarrow$ Hard block HTTP 409 `PRICE_CHANGED` và cập nhật giá mới vào Cart.
     - Nếu sản phẩm **Giảm giá hoặc bằng** $\rightarrow$ Tự động áp dụng giá rẻ hơn và thông báo qua `appliedPriceDrops`.
  3. **Giữ kho tạm thời 15 phút**: Gọi Fulfillment `:8006` (`POST /api/v1/internal/inventories/reservations`).
  4. **Thanh toán Ví trực tiếp**: Gọi Wallet `:8008` (`POST /api/v1/wallets/orders/{orderId}/payment`). Thành công $\rightarrow$ Ghi nhận `TransactionId`, gọi Fulfillment trừ kho vĩnh viễn (`CommitStockAsync`), đơn chuyển trạng thái `Paid`.
  5. **Circuit Breaker Compensation Rollback**: Nếu `SaveChangesAsync()` phát sinh lỗi kết nối cơ sở dữ liệu sau khi đã trừ ví và giữ kho $\rightarrow$ Kích hoạt chuỗi bù trừ: Gọi Fulfillment nhả kho (`ReleaseStockAsync`) + Gọi Wallet hoàn tiền ví 100% (`RefundOrderPaymentAsync` với idempotency key `compensation-db-{orderId}`).
* **Response (HTTP 201 Created):**
  ```json
  {
    "orderId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
    "orderNo": "ORD-20261109-9A0B1C",
    "totalAmount": 530000.0,
    "status": "Paid",
    "paymentDeadlineAt": "2026-11-09T10:15:00Z",
    "paymentUrl": null,
    "subOrderIds": [
      "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08"
    ],
    "createdAt": "2026-11-09T10:00:00Z",
    "appliedPriceDrops": null
  }
  ```

---

# PHẦN 4: BOUNDED CONTEXT 3 — VÒNG ĐỜI ĐƠN HÀNG & VẬN CHUYỂN (`ORD-01` .. `ORD-13`)

### Bảng Tra Cứu Toàn Bộ API Đơn Hàng (`OrdersController`):

| Mã | Method | Path | Tên Nghiệp Vụ | Quyền (Role) |
| :-: | :-: | - | - | :-: |
| **`ORD-01 / R78`** | `GET` | `/api/v1/orders` | Danh sách lịch sử đơn hàng của tôi (phân trang DB) | `buyer` |
| **`ORD-02 / R77`** | `GET` | `/api/v1/orders/{orderId}` | Chi tiết đơn hàng cha (Master Order kèm Passive Guard) | `buyer`, `seller`, `admin` |
| **`ORD-02B / R79`**| `GET` | `/api/v1/sub-orders/{subOrderId}` | Chi tiết đơn con của Shop (Single DTO bóc tách discount) | `buyer`, `seller`, `admin` |
| **`ORD-02C`** | `GET` | `/api/v1/sub-orders` | Người bán tra cứu danh sách đơn con của Shop mình (phân trang DB) | `seller` |
| **`ORD-04 / R83`** | `POST` | `/api/v1/orders/{orderId}/accept` | Shop tiếp nhận đơn con (`Confirmed` + tạo vận đơn GHN) | `seller` |
| **`ORD-05 / R84`** | `POST` | `/api/v1/orders/{orderId}/reject` | Shop từ chối đơn con (Nhả kho mục tiêu + hoàn tiền ví tự động) | `seller` |
| **`ORD-06 / R80`** | `POST` | `/api/v1/orders/{orderId}/cancel` | Người mua hủy đơn đang chờ thanh toán | `buyer` |
| **`ORD-07`** | `POST` | `/api/v1/sub-orders/{subOrderId}/confirm-receipt` | Người mua xác nhận đã nhận hàng (`Completed` & giải ngân Escrow) | `buyer` |
| **`ORD-08 / R87`** | `GET` | `/api/v1/shipments/order/{orderId}` | Tra cứu danh sách vận đơn và tiến độ giao hàng | `buyer`, `seller` |
| **`ORD-13 / R99`** | `POST` | `/api/v1/sub-orders/{subOrderId}/refund` | Khởi tạo yêu cầu hoàn tiền cho đơn con | `buyer` |
| **`LEGACY`** | `GET` | `/api/v1/orders/buyer/{buyerId}` | Tra cứu đơn hàng theo `buyerId` (Tương thích ngược) | `buyer`, `admin` |

---

### Chi Tiết Đặc Tả Kỹ Thuật Các Endpoints Đơn Hàng:

#### `ORD-01 / R78`: Danh Sách Lịch Sử Đơn Hàng (Phân Trang DB)
* **Endpoint:** `GET /api/v1/orders?page=1&pageSize=20&status=Paid`
* **Quyền:** `buyer`
* **Response (HTTP 200 OK):**
  ```json
  {
    "items": [
      {
        "orderId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
        "orderNo": "ORD-20261109-9A0B1C",
        "totalAmount": 530000.0,
        "status": "Paid",
        "paymentDeadlineAt": "2026-11-09T10:15:00Z",
        "subOrderCount": 1,
        "createdAt": "2026-11-09T10:00:00Z"
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
* **Quyền:** `buyer`, `seller`, `admin`
* **Cơ chế:** Kích hoạt **Passive Guard** tự động hủy đơn, nhả kho Fulfillment và khôi phục voucher nếu đơn `PendingPayment` quá hạn 15 phút. Xác thực quyền sở hữu `order.BuyerId == currentUserId`.
* **Response (HTTP 200 OK):**
  ```json
  {
    "id": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
    "orderNo": "ORD-20261109-9A0B1C",
    "buyerId": "11111111-1111-1111-1111-000000000004",
    "status": "Paid",
    "totalAmount": 530000.0,
    "paymentDeadlineAt": "2026-11-09T10:15:00Z",
    "createdAt": "2026-11-09T10:00:00Z",
    "subOrders": [
      {
        "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
        "subOrderNo": "ORD-20261109-9A0B1C-01",
        "orderId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
        "buyerId": "11111111-1111-1111-1111-000000000004",
        "sellerId": "shop1111-1111-1111-1111-000000000001",
        "shopName": "Shop shop1111",
        "status": "Paid",
        "paymentMethod": "Wallet",
        "shippingAddress": {
          "receiverName": "Nguyen Van A",
          "contactPhone": "0901234567",
          "province": "TP. Hồ Chí Minh",
          "district": "Quận 1",
          "ward": "Phường Bến Nghé",
          "ghnDistrictId": 1442,
          "ghnWardCode": "20101",
          "detailAddress": "123 Le Loi"
        },
        "contactPhone": "0901234567",
        "itemsAmount": 500000.0,
        "productDiscountAmount": 0.0,
        "shopDiscountAmount": 0.0,
        "systemDiscountAmount": 0.0,
        "discountAmount": 0.0,
        "shippingFeeAmount": 30000.0,
        "totalAmount": 530000.0,
        "createdAt": "2026-11-09T10:00:00Z",
        "items": [
          {
            "orderItemId": "c2d3e4f5-9999-4a0b-9c1d-3e4f5a6b7c09",
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
    ]
  }
  ```

#### `ORD-02B / R79`: Chi Tiết Đơn Con Của Shop (Single DTO)
* **Endpoint:** `GET /api/v1/sub-orders/{subOrderId}`
* **Quyền:** `buyer`, `seller`, `admin`
* **Response (HTTP 200 OK):** Trả về `SubOrderDetailResponse`.

#### `ORD-02C`: Người Bán Tra Cứu Danh Sách Đơn Con Của Shop Mình
* **Endpoint:** `GET /api/v1/sub-orders?page=1&pageSize=20&status=Paid`
* **Quyền:** `seller`
* **Cơ chế:** Tự động lọc theo `so.SellerId == currentUserId` trích xuất từ header Gateway `x-user-id`. Phân trang trực tiếp tại database qua `.Skip().Take()`.
* **Response (HTTP 200 OK):**
  ```json
  {
    "items": [
      {
        "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
        "subOrderNo": "ORD-20261109-9A0B1C-01",
        "orderId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
        "buyerId": "11111111-1111-1111-1111-000000000004",
        "sellerId": "shop1111-1111-1111-1111-000000000001",
        "status": "Paid",
        "paymentMethod": "Wallet",
        "itemsAmount": 500000.0,
        "shippingFeeAmount": 30000.0,
        "totalAmount": 530000.0,
        "sellerReceiveAmount": 503500.0,
        "itemCount": 2,
        "createdAt": "2026-11-09T10:00:00Z",
        "sellerAcceptedAt": null,
        "shippedAt": null,
        "deliveredAt": null,
        "completedAt": null,
        "cancelledAt": null
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

#### `ORD-04 / R83`: Shop Tiếp Nhận Đơn Con
* **Endpoint:** `POST /api/v1/orders/{orderId}/accept`
* **Quyền:** `seller`
* **Request Body:**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "note": "Hàng sẵn sàng xuất kho"
  }
  ```
* **Quy tắc:** Chuyển `SubOrder.Status = Confirmed`, ghi nhận `SellerAcceptedAt = UtcNow`, tự động gọi Fulfillment `:8006` (`POST /api/v1/internal/shipments`) tạo vận đơn GHN tự động.
* **Response (HTTP 200 OK):**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "status": "Confirmed",
    "acceptedAt": "2026-11-09T10:05:00Z"
  }
  ```

#### `ORD-05 / R84`: Shop Từ Chối Đơn Con
* **Endpoint:** `POST /api/v1/orders/{orderId}/reject`
* **Quyền:** `seller`
* **Request Body:**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "reasonCode": "OutOfStock",
    "reasonText": "Hết màu đen size L"
  }
  ```
* **Cơ chế kỹ thuật:**
  1. Chuyển `SubOrder.Status = Cancelled`. Đơn của các shop khác trong cùng Master Order vẫn bảo toàn 100%.
  2. **Targeted Stock Release**: Gọi Fulfillment Service:
     `POST /api/v1/internal/inventories/reservations/{orderId}/release-items`
     Payload: `{ "items": [{ "skuId": "s1111111-...", "quantity": 2, "sellerId": "..." }], "reason": "SubOrderRejected" }`.
  3. **Tự động hoàn tiền Ví**: Nếu đơn đã thanh toán trước (`wasPaid == true`), hệ thống gọi Wallet Service:
     `POST /api/v1/refunds` với Idempotency Key `refund-sub-{subOrderId}`. Nếu hoàn tiền thành công $\rightarrow$ Tạo `OrderRefund` (Status: `Completed`). Nếu ví bận $\rightarrow$ Tạo `OrderRefund` (Status: `Requested`) để đối soát.
* **Response (HTTP 200 OK):**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "status": "Cancelled",
    "cancellationReason": "OutOfStock",
    "rejectedAt": "2026-11-09T10:05:00Z"
  }
  ```

#### `ORD-06 / R80`: Người Mua Hủy Đơn Chờ Thanh Toán
* **Endpoint:** `POST /api/v1/orders/{orderId}/cancel`
* **Quyền:** `buyer`
* **Request Body:**
  ```json
  {
    "reasonCode": "BuyerChangedMind",
    "reasonText": "Muốn đổi phương thức thanh toán khác"
  }
  ```
* **Response (HTTP 200 OK):**
  ```json
  {
    "orderId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
    "status": "Cancelled",
    "cancelledAt": "2026-11-09T10:05:00Z"
  }
  ```

#### `ORD-07`: Người Mua Xác Nhận Nhận Hàng & Giải Ngân Escrow
* **Endpoint:** `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt`
* **Quyền:** `buyer`
* **Mô tả:** Khách hàng bấm xác nhận khi hàng đã giao tới nơi (`Delivered` hoặc `Shipped`). SubOrder chuyển sang `Completed`. Hệ thống kích hoạt giải ngân quỹ Escrow sang ví người bán.
* **Cơ chế kỹ thuật:**
  - Kiểm tra quyền sở hữu (`so.BuyerId == currentUserId`).
  - Cập nhật `SubOrder.Status = Completed`, `CompletedAt = UtcNow`.
  - Gọi Wallet Service: `POST /api/v1/wallets/sub-orders/{subOrderId}/settle` chuyển số tiền `SellerReceiveAmount` sang ví Seller với Idempotency Key `settle-{subOrderId}`.
* **Response (HTTP 200 OK):**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "status": "Completed",
    "completedAt": "2026-11-09T14:00:00Z",
    "escrowSettled": true,
    "amountSettled": 503500.0
  }
  ```

#### `ORD-08 / R87`: Theo Dõi Vận Chuyển Đơn Hàng
* **Endpoint:** `GET /api/v1/shipments/order/{orderId}`
* **Quyền:** `buyer`, `seller`
* **Response (HTTP 200 OK):**
  ```json
  {
    "orderId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
    "orderNo": "ORD-20261109-9A0B1C",
    "shipments": [
      {
        "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
        "subOrderNo": "ORD-20261109-9A0B1C-01",
        "sellerId": "shop1111-1111-1111-1111-000000000001",
        "shopName": "Shop shop1111",
        "status": "Shipping",
        "carrierCode": "GHN",
        "trackingCode": "GHN998877VN",
        "shippedAt": "2026-11-09T11:00:00Z",
        "deliveredAt": null
      }
    ]
  }
  ```

#### `ORD-13 / R99`: Khởi Tạo Yêu Cầu Hoàn Tiền Đơn Con
* **Endpoint:** `POST /api/v1/sub-orders/{subOrderId}/refund`
* **Quyền:** `buyer`
* **Request Body:**
  ```json
  {
    "reason": "DamagedItem",
    "note": "Sản phẩm bị rách bao bì"
  }
  ```
* **Response (HTTP 200 OK):**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "status": "RefundRequested",
    "requestedAt": "2026-11-09T14:30:00Z"
  }
  ```

#### `LEGACY`: Tra Cứu Đơn Hàng Theo Buyer ID (Tương Thích Ngược)
* **Endpoint:** `GET /api/v1/orders/buyer/{buyerId}?page=1&pageSize=20`
* **Quyền:** `buyer` (cho chính mình), `admin`
* **Response (HTTP 200 OK):**
  ```json
  {
    "items": [
      {
        "id": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
        "buyerId": "11111111-1111-1111-1111-000000000004",
        "status": "Paid",
        "totalAmount": 530000.0,
        "paymentDeadlineAt": "2026-11-09T10:15:00Z"
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

---

# PHẦN 5: WEBHOOK TIẾP NHẬN TRẠNG THÁI GIAO VẬN

### Endpoint: `POST /api/v1/sub-orders/{subOrderId}/delivery-status`
* **Bảo mật:** Webhook nội bộ từ Fulfillment Service.
* **Request Body:**
  ```json
  {
    "status": "Delivered",
    "deliveredAt": "2026-11-09T14:30:00Z"
  }
  ```
* **Ma trận 9 trạng thái giao vận từ Carrier (GHN):**
  1. `ReadyToPick`, `Picking`, `Packed`: Cập nhật timestamp chuẩn bị hàng (Idempotent 200 OK).
  2. `HandedOver`, `InTransit`, `Shipped`: Chuyển `SubOrderStatus.Shipped`, ghi nhận `ShippedAt`.
  3. `Delivered`: Chuyển `SubOrderStatus.Delivered`, ghi nhận `DeliveredAt`.
  4. `Cancelled`: Chuyển `SubOrderStatus.Cancelled`, ghi nhận `CancelledAt`.
  5. `DeliveryFailed`, `Returned`: Chuyển `SubOrderStatus.Cancelled`, tự động ghi nhận bản ghi `OrderCancellation` (Lý do: `DeliveryFailed` hoặc `PackageReturned`) và tự động tạo `OrderRefund` (Status: `Requested`) nếu đơn đã thanh toán trước đó.
* **Response (HTTP 200 OK):**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "status": "Delivered",
    "updatedAt": "2026-11-09T14:30:00Z"
  }
  ```

---

# PHẦN 6: HỢP ĐỒNG OUTBOUND TYPED REST CLIENTS (GỌI LIÊN DỊCH VỤ)

Commerce Service giao tiếp với 3 dịch vụ vệ tinh qua các Typed HTTP Clients cấu hình tại `Commerce.Infrastructure/DependencyInjection.cs`:

### 6.1. Catalog Service (`:8002` REST) — `ICatalogClient`
1. **Tra cứu đơn lẻ thông tin SKU:** `GET /api/v1/internal/variants/{skuId}`
2. **Batch tra cứu thông tin biến thể SKU (`POST /api/v1/internal/variants`):**
   * Payload: `[ "guid-1", "guid-2" ]`
   * Response:
     ```json
     [
       {
         "skuId": "s1111111-1111-1111-1111-000000000001",
         "productId": "p1111111-1111-1111-1111-000000000001",
         "sellerId": "shop1111-1111-1111-1111-000000000001",
         "productName": "Áo Thun Cotton Nexus",
         "unitPrice": 250000.0,
         "isPurchasable": true,
         "stockQuantity": 100
       }
     ]
     ```
3. **Kiểm tra tính hợp lệ khi đổi biến thể (`POST /api/v1/internal/catalog/skus/validate-variant-switch`):**
   * Payload: `{ "currentSkuId": "guid-old", "newSkuId": "guid-new" }`
   * Response:
     ```json
     {
       "isValid": true,
       "productId": "guid-product",
       "sellerId": "guid-seller",
       "newSkuId": "guid-new",
       "newSkuName": "Áo Thun - Trắng L",
       "newUnitPrice": 260000.0,
       "isPurchasable": true,
       "weightGrams": 200
     }
     ```

---

### 6.2. Fulfillment Service (`:8006` REST) — `IFulfillmentClient`
1. **Kiểm tra tồn kho đơn lẻ:** `GET /api/v1/inventories/{skuId}`
2. **Batch kiểm tra tồn kho giỏ hàng:** `POST /api/v1/inventories/by-skus` (Payload: `["skuId1", "skuId2"]`)
3. **Giữ kho tạm thời 15 phút:** `POST /api/v1/internal/inventories/reservations`
   * Payload:
     ```json
     {
       "referenceId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
       "holdMinutes": 15,
       "items": [
         { "skuId": "s1111111-...", "quantity": 2, "sellerId": "shop1111-..." }
       ]
     }
     ```
4. **Trừ kho vĩnh viễn (Commit):** `POST /api/v1/internal/inventories/reservations/{orderId}/commit`
5. **Nhả tồn kho khi hủy đơn cha:** `POST /api/v1/internal/inventories/reservations/{orderId}/release?reason=OrderCancelled`
6. **Batch nhả kho cho nhiều đơn quá hạn:** `POST /api/v1/internal/inventories/reservations/batch-release`
   * Payload: `{ "orderIds": ["orderId1", "orderId2"] }`
   * Response: `{ "succeededOrderIds": [...], "failedOrderIds": [], "isSuccess": true }`
7. **Nhả kho có mục tiêu cho SubOrder bị từ chối:** `POST /api/v1/internal/inventories/reservations/{orderId}/release-items`
   * Payload:
     ```json
     {
       "items": [
         { "skuId": "s1111111-...", "quantity": 2, "sellerId": "shop1111-..." }
       ],
       "reason": "SubOrderRejected"
     }
     ```
8. **Tính cước vận chuyển GHN động:** `POST /api/v1/internal/shipping/quote`
   * Payload:
     ```json
     {
       "sellerId": "shop1111-1111-1111-1111-000000000001",
       "toDistrictId": 1442,
       "toWardCode": "20101",
       "items": [{ "skuId": "s1111111-...", "quantity": 2, "weightGrams": 200 }]
     }
     ```
   * Response: `{ "feeAmount": 30000.0, "shippingFee": 30000.0 }`
9. **Tạo vận đơn GHN tự động:** `POST /api/v1/internal/shipments`
   * Payload: `{ "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08" }`
   * Response:
     ```json
     {
       "trackingCode": "GHN998877VN",
       "carrierCode": "GHN",
       "labelPrintUrl": "https://carrier.ghn.vn/print/GHN998877VN"
     }
     ```

---

### 6.3. Wallet Service (`:8008` REST) — `IWalletClient`
1. **Trừ tiền ví trực tiếp theo đơn hàng:** `POST /api/v1/wallets/orders/{orderId}/payment`
   * Headers: `Authorization: Bearer <token>`, `x-user-id: <guid>`
   * Payload: `{ "amount": 530000.0 }`
   * Response:
     ```json
     {
       "success": true,
       "transactionId": "TXN-20261109-001122",
       "remainingBalance": 970000.0
     }
     ```
2. **Lấy số dư ví thời gian thực:** `GET /api/v1/wallets/me`
   * Response: `{ "balance": 1500000.0 }`
3. **Hoàn tiền ví tự động:** `POST /api/v1/refunds`
   * Headers: `Idempotency-Key: refund-sub-{subOrderId}`
   * Payload:
     ```json
     {
       "userId": "11111111-1111-1111-1111-000000000004",
       "amount": 530000.0,
       "reason": "SubOrderRejected"
     }
     ```
4. **Giải ngân Escrow sang ví Shop:** `POST /api/v1/wallets/sub-orders/{subOrderId}/settle`
   * Headers: `Idempotency-Key: settle-{subOrderId}`
   * Payload:
     ```json
     {
       "sellerId": "shop1111-1111-1111-1111-000000000001",
       "amount": 503500.0
     }
     ```

---

# PHẦN 7: BẢNG TRA CỨU MÃ LỖI HỆ THỐNG CHUẨN RFC 9110

| HTTP Status | Mã Lỗi (`errorCode`) | Ngữ Cảnh Kích Hoạt | Câu Thông Báo Tiếng Anh Chuẩn (Sentence Case) |
| :-: | - | - | - |
| **400** | `VAL_INVALID_INPUT` | Body thiếu trường hoặc dữ liệu sai định dạng | `"Shipping address fields cannot be empty."` |
| **400** | `CART_EMPTY` | Giỏ hàng rỗng hoặc không có món hợp lệ khi checkout | `"No valid items selected for checkout."` |
| **400** | `QUOTE_EXPIRED` | Báo giá hết hạn 15m hoặc bấm đúp nút Đặt hàng | `"Checkout quote has expired, is invalid, or is currently being processed."` |
| **400** | `INSUFFICIENT_STOCK` | Kho không đủ số lượng tồn khả dụng | `"One or more items in the order have insufficient stock."` |
| **400** | `MAX_QUANTITY_EXCEEDED` | Mua vượt quá 99 chiếc/món | `"Quantity per item cannot exceed 99."` |
| **400** | `MAX_ITEMS_REACHED` | Vượt quá 50 món khác nhau trong giỏ | `"Cart cannot contain more than 50 distinct items."` |
| **400** | `INVALID_STATUS` | Chuyển trạng thái đơn không hợp lệ | `"Sub-order cannot be confirmed in its current status."` |
| **400** | `ORDER_CANNOT_BE_CANCELLED` | Hủy đơn không ở trạng thái PendingPayment | `"Order cannot be cancelled in its current status."` |
| **400** | `CATALOG_SKU_INACTIVE` | Sản phẩm đã ngừng bán trên sàn | `"Product is no longer available for purchase."` |
| **400** | `INVALID_DELIVERY_STATUS` | Webhook gửi status giao vận lạ | `"The provided delivery status is not supported."` |
| **403** | `FORBIDDEN` | Truy cập đơn hàng/giỏ hàng của người khác | `"You do not have permission to access this order."` |
| **403** | `FORBIDDEN` | Xác nhận đơn con không thuộc quyền sở hữu | `"You do not have permission to confirm this sub-order."` |
| **404** | `CART_ITEM_NOT_FOUND` | Không tìm thấy dòng sản phẩm trong giỏ | `"Cart item not found."` |
| **404** | `ORDER_NOT_FOUND` | Không tìm thấy đơn hàng cha | `"Order was not found."` |
| **404** | `SUB_ORDER_NOT_FOUND` | Không tìm thấy đơn hàng con | `"SubOrder was not found."` |
| **404** | `CATALOG_SKU_NOT_FOUND` | SKU không tồn tại trong Catalog Service | `"Product SKU was not found in catalog."` |
| **409** | `PRICE_CHANGED` | Sản phẩm bị tăng giá lúc bấm Đặt hàng | `"Price of 'Áo Thun Cotton Nexus' has increased from 240000.00 to 250000.00. Cart prices have been refreshed."` |
| **500** | `ORDER_CREATION_FAILED` | Sập DB lúc tạo đơn, đã kích hoạt hoàn tiền bù trừ | `"An error occurred while saving the order. All inventory allocations and payments have been safely rolled back."` |
| **503** | `STOCK_CHECK_UNAVAILABLE` | Kho Fulfillment tạm thời không phản hồi | `"Warehouse inventory service is temporarily unreachable."` |
| **503** | `FULFILLMENT_SERVICE_UNAVAILABLE` | Tính cước GHN hoặc tạo vận đơn lỗi | `"Fulfillment service is temporarily unreachable."` |
| **503** | `CATALOG_SERVICE_UNAVAILABLE` | Catalog Service tạm thời mất kết nối | `"Product catalog service is temporarily unreachable."` |

---

# PHẦN 8: BỘ TYPESCRIPT TYPE DEFINITIONS HOÀN CHỈNH (FRONTEND & SDK DTOs)

Dành cho Frontend Web/Mobile, API Gateway và Client SDKs, khớp 100% với DTO trong C# code:

```typescript
// =============================================================================
// COMMERCE SERVICE FRONTEND CONTRACT TYPES (100% C# RECORD COMPATIBLE)
// =============================================================================

export type PaymentMethod = 'Wallet' | 'VnPay' | 'Cod';
export type OrderStatus = 'PendingPayment' | 'PaymentProcessing' | 'Paid' | 'Cancelled' | 'Expired';
export type SubOrderStatus = 'PendingPayment' | 'Paid' | 'Confirmed' | 'Shipped' | 'Delivered' | 'Completed' | 'Cancelled' | 'Refunded';
export type CancellationReasonCode = 'BuyerChangedMind' | 'OutOfStock' | 'PaymentTimeout' | 'SellerRejected' | 'Other' | 'DeliveryFailed' | 'PackageReturned';

export interface PaginationMetadata {
  pageIndex: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface PagedData<T> {
  items: T[];
  pagination: PaginationMetadata;
}

// -----------------------------------------------------------------------------
// 1. Bounded Context 1: Giỏ hàng (Cart)
// -----------------------------------------------------------------------------

export interface CartItemResponse {
  id: string;
  productId: string;
  skuId: string;
  sellerId: string;
  productName: string;
  quantity: number;
  unitPriceSnapshot: number;
  lineTotal: number;
  addedAt: string;
  isAvailable: boolean;
  disabledReason?: string | null;
}

export interface CartResponse {
  id: string;
  buyerId: string;
  status: string;
  itemCount: number;
  subtotalAmount: number;
  items: CartItemResponse[];
  lastActivityAt: string;
}

export interface CartSummaryResponse {
  cartId: string;
  totalDistinctItems: number;
  totalQuantity: number;
  estimatedSubtotal: number;
}

export interface ShopCartGroupResponse {
  sellerId: string;
  shopName: string;
  distinctItemCount: number;
  totalQuantity: number;
  shopSubtotal: number;
  items: CartItemResponse[];
}

export interface GroupedCartResponse {
  cartId: string;
  buyerId: string;
  totalDistinctItems: number;
  totalQuantity: number;
  subtotalAmount: number;
  shops: ShopCartGroupResponse[];
}

export interface CalculatedCartItem {
  cartItemId: string;
  skuId: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
  isAvailable: boolean;
  availableStock?: number | null;
  stockWarning?: string | null;
}

export interface CalculateSelectedCartResponse {
  selectedItemCount: number;
  totalQuantity: number;
  subtotalAmount: number;
  items: CalculatedCartItem[];
}

export interface CartItemIssue {
  cartItemId: string;
  skuId: string;
  issueType: string;
  message: string;
  oldPrice?: number | null;
  newPrice?: number | null;
  availableStock?: number | null;
  requestedQuantity?: number | null;
  suggestedQuantity?: number | null;
}

export interface ValidateCartResponse {
  isValid: boolean;
  hasPriceChanges: boolean;
  hasStockIssues: boolean;
  issues: CartItemIssue[];
}

export interface AddCartItemRequest {
  skuId: string;
  quantity: number;
  productId?: string | null;
}

export interface UpdateCartItemQuantityRequest {
  cartItemId: string;
  quantity: number;
}

export interface ChangeCartItemVariantRequest {
  newSkuId: string;
}

export interface BulkRemoveCartItemsRequest {
  cartItemIds?: string[] | null;
  clearAll?: boolean;
}

export interface CalculateSelectedCartItemsRequest {
  selectedItemIds: string[];
}

export interface ValidateCartRequest {
  selectedItemIds?: string[] | null;
}

export interface GuestCartItem {
  skuId: string;
  quantity: number;
  productId?: string | null;
}

export interface MergeGuestCartRequest {
  guestItems: GuestCartItem[];
}

export interface CleanupCheckedOutItemsRequest {
  purchasedCartItemIds: string[];
}

// -----------------------------------------------------------------------------
// 2. Bounded Context 2: Báo giá & Đặt hàng 2 Bước (Checkout)
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
  paymentMethod?: PaymentMethod;
  carrierCode?: string | null;
  voucherCodes?: string[] | null;
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

export interface CheckoutQuoteResponse {
  quoteId: string;
  grandTotal: number;
  walletBalance: number;
  shops: ShopOrderQuote[];
  appliedVouchers: string[];
  warnings?: string[] | null;
  expiredAt: string;
}

export interface PriceDropDto {
  skuId: string;
  productName: string;
  oldPrice: number;
  newPrice: number;
  savedAmount: number;
}

export interface CreateOrderRequest {
  quoteId: string;
  paymentMethod: PaymentMethod;
  shippingAddress: string;
  contactPhone: string;
  note?: string | null;
}

export interface CreateOrderResponse {
  orderId: string;
  orderNo: string;
  totalAmount: number;
  status: string;
  paymentDeadlineAt: string;
  paymentUrl?: string | null;
  subOrderIds: string[];
  createdAt: string;
  appliedPriceDrops?: PriceDropDto[] | null;
}

// -----------------------------------------------------------------------------
// 3. Bounded Context 3: Đơn hàng & Vận chuyển (Orders & Shipments)
// -----------------------------------------------------------------------------

export interface ShippingAddressSnapshot {
  receiverName: string;
  contactPhone: string;
  province: string;
  district: string;
  ward: string;
  ghnDistrictId?: number;
  ghnWardCode?: string;
  detailAddress: string;
}

export interface SubOrderItemResponse {
  orderItemId: string;
  productId: string;
  skuId: string;
  productName: string;
  skuCode?: string | null;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface SubOrderDetailResponse {
  subOrderId: string;
  subOrderNo: string;
  orderId: string;
  buyerId: string;
  sellerId: string;
  shopName: string;
  status: string;
  paymentMethod: string;
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
  orderId: string;
  orderNo: string;
  totalAmount: number;
  status: string;
  paymentDeadlineAt: string;
  subOrderCount: number;
  createdAt: string;
}

export interface OrderResponse {
  id: string;
  orderNo: string;
  buyerId: string;
  status: string;
  totalAmount: number;
  paymentDeadlineAt: string;
  createdAt: string;
  subOrders: SubOrderDetailResponse[];
}

export interface SubOrderSummaryResponse {
  subOrderId: string;
  subOrderNo: string;
  orderId: string;
  buyerId: string;
  sellerId: string;
  status: string;
  paymentMethod: string;
  itemsAmount: number;
  shippingFeeAmount: number;
  totalAmount: number;
  sellerReceiveAmount: number;
  itemCount: number;
  createdAt: string;
  sellerAcceptedAt?: string | null;
  shippedAt?: string | null;
  deliveredAt?: string | null;
  completedAt?: string | null;
  cancelledAt?: string | null;
}

export interface ConfirmReceiptResponse {
  subOrderId: string;
  status: string;
  completedAt: string;
  escrowSettled: boolean;
  amountSettled: number;
}

export interface SubOrderShipmentInfo {
  subOrderId: string;
  subOrderNo: string;
  sellerId: string;
  shopName: string;
  status: string;
  carrierCode?: string | null;
  trackingCode?: string | null;
  shippedAt?: string | null;
  deliveredAt?: string | null;
}

export interface ShipmentTrackingResponse {
  orderId: string;
  orderNo: string;
  shipments: SubOrderShipmentInfo[];
}

export interface CancelOrderRequest {
  reasonCode?: CancellationReasonCode;
  reasonText?: string | null;
}

export interface SellerAcceptOrderRequest {
  subOrderId: string;
  note?: string | null;
}

export interface SellerRejectOrderRequest {
  subOrderId: string;
  reasonCode?: CancellationReasonCode;
  reasonText?: string | null;
}

export interface SubOrderRefundRequest {
  reason: string;
  note?: string | null;
}

export interface UpdateDeliveryStatusRequest {
  status: string;
  deliveredAt?: string | null;
}
```
