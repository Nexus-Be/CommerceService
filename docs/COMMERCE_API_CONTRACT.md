# COMMERCE SERVICE — MASTER REST API CONTRACT & INTEGRATION SPECIFICATION
## (Hợp đồng Giao tiếp RESTful Toàn diện: Giỏ hàng, Báo giá, Đơn hàng & Vận chuyển)

> **Phiên bản:** v3.0.0 (Master Unified Contract)  
> **Phân hệ:** Commerce Service (`Commerce.Api`)  
> **Cổng dịch vụ (Port):** `8003` (Container & Localhost)  
> **Cơ sở dữ liệu:** PostgreSQL on port `5435` (`commerce_db`)  
> **Giao thức:** HTTP/1.1 & HTTP/2 RESTful Web API  
> **Đối tượng sử dụng:** Frontend (Web & Mobile), API Gateway Team, Wallet Service Team, Catalog Service Team, OJT / Kỹ sư Phần mềm  
> **Cập nhật lần cuối:** 2026-11-01  
> **Trạng thái:** ✅ ĐÃ CHUẨN HÓA & KHÓA NGUYÊN TẮC (LOCKED)  

---

## MỤC LỤC BỐ CỤC TÀI LIỆU

1. [Phần 1: Quy chuẩn Kiến trúc & Giao thức Chung](#phần-1-quy-chuẩn-kiến-trúc--giao-thức-chung)
2. [Phần 2: Bounded Context 1 — Quản lý Giỏ hàng (`CART-01` .. `CART-14`)](#phần-2-bounded-context-1--quản-lý-giỏ-hàng-cart-01--cart-14)
   * *Nhóm 1: Xem & Khám phá Giỏ hàng (`CART-01` .. `CART-03`)*
   * *Nhóm 2: Thao tác Dòng Sản phẩm (`CART-04` .. `CART-09`)*
   * *Nhóm 3: Tính giá & Chuẩn bị Đặt hàng (`CART-10` .. `CART-12`)*
   * *Nhóm 4: Hậu Đặt hàng & Quản trị (`CART-13` .. `CART-14`)*
3. [Phần 3: Bounded Context 2 — Báo giá & Đặt hàng 2 Bước (`CHK-01` .. `CHK-02`)](#phần-3-bounded-context-2--báo-giá--đặt-hàng-2-bước-chk-01--chk-02)
   * *Bước 1: Báo giá Giỏ hàng Đa Shop (`CHK-01` / `R75`)*
   * *Bước 2: Tạo Đơn hàng Chính thức (`CHK-02` / `R76`)*
4. [Phần 4: Bounded Context 3 — Vòng đời Đơn hàng & Vận chuyển (`ORD-01` .. `ORD-13`)](#phần-4-bounded-context-3--vòng-đời-đơn-hàng--vận-chuyển-ord-01--ord-13)
   * *Tra cứu Đơn hàng (`ORD-01`, `ORD-02`, `ORD-02B`)*
   * *Xử lý & Chuyển trạng thái Đơn (`ORD-04`, `ORD-05`, `ORD-06`)*
   * *Vận chuyển & Hoàn tiền (`ORD-08`, `ORD-13`)*
5. [Phần 5: Hợp đồng Gọi Liên dịch vụ (Outbound Contract `R181`)](#phần-5-hợp-đồng-gọi-liên-dịch-vụ-outbound-contract-r181)
6. [Phần 6: Bảng Tra cứu Mã Lỗi Hệ thống Chuẩn RFC 9110](#phần-6-bảng-tra-cứu-mã-lỗi-hệ-thống-chuẩn-rfc-9110)
7. [Phần 7: Bộ TypeScript Type Definitions Hoàn chỉnh (Frontend DTOs)](#phần-7-bộ-typescript-type-definitions-hoàn-chỉnh-frontend-dtos)

---

# PHẦN 1: QUY CHUẨN KIẾN TRÚC & GIAO THỨC CHUNG

### 1.1. Bounded Context & Phân định Ranh giới
Commerce Service chịu trách nhiệm độc quyền:
1. **Giỏ hàng (`Carts`, `CartItems`)**: Mỗi Người mua (Buyer) sở hữu tối đa 1 Giỏ hàng `ACTIVE`. Giới hạn tối đa 50 món (distinct items) và tối đa 99 đơn vị/món.
2. **Đơn hàng cha & Đơn con theo Shop (`Orders`, `SubOrders`, `SubOrderItems`)**: Đơn cha quản lý tổng thanh toán toàn phiên, Đơn con quản lý từng gói hàng độc lập của từng Người bán (Seller).
3. **Snapshot Bất biến (Immutable Snapshots)**:
   - Thông tin sản phẩm lúc mua: Lưu `ProductNameSnapshot`, `UnitPriceSnapshot`, `SkuCodeSnapshot`.
   - Địa chỉ giao hàng: Lưu `ShippingAddressSnapshot` (dạng PostgreSQL `jsonb` qua EF Core 8).
4. **Những gì Commerce Service KHÔNG sở hữu (External Collaborations)**:
   - **Số dư tiền, trừ tiền ví & hoàn tiền** $\rightarrow$ Do **Wallet Service** (`:8008`) sở hữu (gọi qua `IWalletClient`).
   - **Thông tin sản phẩm niêm yết (Master Data, Tên, Giá bán, Trạng thái bán)** $\rightarrow$ Do **Catalog Service** (`:8002`) sở hữu (gọi qua `ICatalogClient`).
   - **Tồn kho vật lý, Giữ hàng (15m TTL), Trừ/Nhả kho & Vận chuyển GHN** $\rightarrow$ Do **Fulfillment Service** (`:8005`) sở hữu (gọi qua `IFulfillmentClient`).

---

### 1.2. Quy chuẩn Headers từ API Gateway

Mọi HTTP request từ Frontend (Web/Mobile) gửi qua API Gateway bắt buộc phải có các Headers:

| Header Name | Kiểu | Bắt buộc | Diễn giải | Ví dụ |
| :--- | :--- | :---: | :--- | :--- |
| `X-User-Id` | UUID (v4) | **Có** | ID người dùng xác thực từ Claim `sub` | `11111111-1111-1111-1111-000000000004` |
| `X-User-Roles` | String | **Có** | Vai trò người dùng (`buyer`, `seller`, `admin`) | `buyer` hoặc `buyer,seller` |
| `Content-Type` | String | **Có** | MIME type (cho POST/PATCH) | `application/json` |
| `Accept` | String | **Có** | Định dạng client mong muốn nhận | `application/json` |

---

### 1.3. Quy tắc Đóng gói Phong bì (Response Envelope Conventions)

* **Tại tầng Controller C# (Nội bộ Commerce Service)**: Controller **CHỈ TRẢ VỀ PURE DTO** (DTO thuần túy) kèm HTTP Status chuẩn. Tuyệt đối không bọc lặp `ApiResponse<T>`.
* **Tại tầng API Gateway**: Gateway tự động bọc vỏ phong bì trước khi truyền ra Internet:
  ```json
  {
    "success": true,
    "data": { ... },
    "error": null,
    "timestamp": "2026-11-01T10:00:00Z",
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
    "detail": "Checkout quote has expired.",
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
  }
  ```

---

### 1.4. Quy tắc Vàng về Casing & Định danh Trường Tài chính

1. **Mã lỗi hệ thống (`errorCode`)**: 100% `SCREAMING_SNAKE_CASE` (ví dụ: `CART_ITEM_NOT_FOUND`, `ORDER_NOT_FOUND`, `VAL_INVALID_INPUT`).
2. **Enum Dữ liệu nghiệp vụ**: 100% `PascalCase` (ví dụ: `"PendingPayment"`, `"SellerRejected"`, `"Confirmed"`, `"Wallet"`).
3. **Thuộc tính JSON**: 100% `camelCase` (ví dụ: `cartItemId`, `subOrderNo`, `totalAmount`).
4. **Khóa cứng Trường Tiền tệ**: 100% sử dụng **`shippingFeeAmount`**. CẤM tuyệt đối từ khóa `shippingFee`.
   - $\text{totalAmount} = \text{itemsAmount} - \text{discountAmount} + \text{shippingFeeAmount}$
   - $\text{discountAmount} = \text{productDiscountAmount} + \text{shopDiscountAmount} + \text{systemDiscountAmount}$

---

# PHẦN 2: BOUNDED CONTEXT 1 — QUẢN LÝ GIỎ HÀNG (`CART-01` .. `CART-14`)

### Bảng tra cứu nhanh 14 API Giỏ hàng:

| Mã | Method | Path | Tên nghiệp vụ | Quyền |
| :---: | :---: | :--- | :--- | :---: |
| **`CART-01`** | `GET` | `/api/v1/carts/me` | Lấy hoặc tự động tạo giỏ hàng người mua | `Buyer` |
| **`CART-02`** | `GET` | `/api/v1/carts/me/summary` | Badge số lượng & tổng tiền tạm tính cho Header | `Buyer` |
| **`CART-03`** | `GET` | `/api/v1/carts/me/grouped` | Giỏ hàng gom nhóm theo Shop (Multi-Vendor) | `Buyer` |
| **`CART-04`** | `POST` | `/api/v1/carts/me/items` | Thêm sản phẩm (SKU) vào giỏ hàng | `Buyer` |
| **`CART-05`** | `PATCH` | `/api/v1/carts/me/items/{cartItemId}` | Cập nhật số lượng của dòng sản phẩm (1–99) | `Buyer` |
| **`CART-06`** | `PATCH` | `/api/v1/carts/me/items/{cartItemId}/variant` | Đổi phân loại hàng trực tiếp trong giỏ (Smart Merge) | `Buyer` |
| **`CART-07`** | `DELETE`| `/api/v1/carts/me/items/{cartItemId}` | Xóa một món khỏi giỏ hàng | `Buyer` |
| **`CART-08`** | `POST` | `/api/v1/carts/me/items:bulk-remove` | Xóa hàng loạt món được tick chọn | `Buyer` |
| **`CART-09`** | `DELETE`| `/api/v1/carts/me/clear` | Dọn sạch toàn bộ giỏ hàng | `Buyer` |
| **`CART-10`** | `POST` | `/api/v1/carts/me/calculate` | Tính tiền tạm tính các món tick chọn (Shopee Checkbox) | `Buyer` |
| **`CART-11`** | `POST` | `/api/v1/carts/me/validate` | Kiểm tra tồn kho & biến động giá trước checkout | `Buyer` |
| **`CART-12`** | `POST` | `/api/v1/carts/me/merge` | Gộp giỏ hàng khách vãng lai sau khi đăng nhập | `Buyer` |
| **`CART-13`** | `POST` | `/api/v1/carts/me/cleanup-checked-out` | Dọn dẹp món đã mua sau khi thanh toán thành công | `Buyer, System` |
| **`CART-14`** | `GET` | `/api/v1/carts/active?buyerId={guid}` | Quản trị tra cứu giỏ hàng đang hoạt động | `Admin, Staff` |

---

### Chi tiết các Endpoint Giỏ hàng tiêu biểu:

#### `CART-01`: GET `/api/v1/carts/me`
* **Mô tả:** Lấy thông tin giỏ hàng của chính người mua đang đăng nhập. Nếu chưa có giỏ nào, hệ thống tự tạo giỏ mới trạng thái `Active`.
* **Response (200 OK):**
  ```json
  {
    "cartId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "buyerId": "11111111-1111-1111-1111-000000000004",
    "itemCount": 2,
    "subtotalAmount": 1250000.00,
    "items": [
      {
        "cartItemId": "4fa85f64-5717-4562-b3fc-2c963f66afa7",
        "productId": "9f8e7d6c-2222-4b3c-8d4e-5f6a7b8c9d02",
        "skuId": "7c6b5a49-3333-4c4d-9e5f-6a7b8c9d0e03",
        "sellerId": "5e9d1c2b-7f3a-4d8e-9c1b-2a4f6d8e0b12",
        "productName": "Bàn phím cơ không dây",
        "quantity": 1,
        "unitPrice": 750000.00,
        "lineTotal": 750000.00
      }
    ]
  }
  ```

#### `CART-04`: POST `/api/v1/carts/me/items`
* **Mô tả:** Thêm sản phẩm vào giỏ. Hệ thống tự động kiểm tra tồn kho vật lý tại Fulfillment Service trước khi thêm. Nếu sản phẩm đã có, tự động cộng dồn số lượng.
* **Lỗi có thể trả về:** `INSUFFICIENT_STOCK` (400) nếu kho hết hàng; `CATALOG_SKU_INACTIVE` (400) nếu sản phẩm bị gỡ bỏ.
* **Request Body:**
  ```json
  {
    "skuId": "7c6b5a49-3333-4c4d-9e5f-6a7b8c9d0e03",
    "quantity": 2
  }
  ```
* **Response (200 OK):** Trả về CartResponse cập nhật mới nhất.

#### `CART-05`: PATCH `/api/v1/carts/me/items/{cartItemId}`
* **Mô tả:** Cập nhật số lượng của dòng sản phẩm (khi bấm `+` / `-` trong giỏ hàng). Bắt buộc kiểm tra tồn kho khả dụng từ Fulfillment Service trước khi cập nhật.
* **Lỗi có thể trả về:** `INSUFFICIENT_STOCK` (400) nếu số lượng yêu cầu vượt quá tồn kho còn lại; `CART_ITEM_NOT_FOUND` (404) nếu món không tồn tại.
* **Request Body:**
  ```json
  {
    "quantity": 5
  }
  ```
* **Response (200 OK):** Trả về CartResponse cập nhật mới nhất.

#### `CART-10`: POST `/api/v1/carts/me/calculate`
* **Mô tả:** Cơ chế tính tiền tạm tính khi người mua tick chọn các checkbox món hàng trong giỏ.
* **Request Body:**
  ```json
  {
    "selectedItemIds": [
      "4fa85f64-5717-4562-b3fc-2c963f66afa7"
    ]
  }
  ```
* **Response (200 OK):**
  ```json
  {
    "selectedItemCount": 1,
    "selectedSubtotalAmount": 750000.00
  }
  ```

---

# PHẦN 3: BOUNDED CONTEXT 2 — BÁO GIÁ & ĐẶT HÀNG 2 BƯỚC (`CHK-01` .. `CHK-02`)

Trong thương mại điện tử đa người bán (Multi-Vendor Marketplace), giỏ hàng có thể chứa sản phẩm từ nhiều Shop khác nhau ở các vị trí địa lý khác nhau. Để tránh tranh chấp giá cước vận chuyển, hệ thống áp dụng quy trình **Quote-Based Checkout 2 Bước**:

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
│ Input: quoteId + Phương thức thanh toán + Ghi chú      │
│ Output: orderId + Danh sách subOrderIds                │
└────────────────────────────────────────────────────────┘
```

---

### `CHK-01`: POST `/api/v1/checkouts/quote` (Xem trước báo giá)
* **Quyền:** `Buyer`
* **Request Body:**
  ```json
  {
    "cartItemIds": [
      "4fa85f64-5717-4562-b3fc-2c963f66afa7"
    ],
    "shippingAddress": {
      "receiverName": "Nguyen Van A",
      "contactPhone": "0901234567",
      "street": "123 Le Loi",
      "ward": "Ben Nghe",
      "district": "Quan 1",
      "province": "TP.HCM",
      "ghnDistrictId": 1442,
      "ghnWardCode": "20101"
    },
    "contactPhone": "0901234567",
    "paymentMethod": "Wallet",
    "voucherCodes": ["FREESHIP2026"]
  }
  ```
* **Response (200 OK):**
  ```json
  {
    "quoteId": "e2a1c4b8-9999-4d8e-b123-5a6b7c8d9e0f",
    "grandTotal": 780000.00,
    "walletBalance": 1500000.00,
    "shops": [
      {
        "sellerId": "5e9d1c2b-7f3a-4d8e-9c1b-2a4f6d8e0b12",
        "shopName": "Shop Công Nghệ",
        "items": [
          {
            "cartItemId": "4fa85f64-5717-4562-b3fc-2c963f66afa7",
            "productId": "9f8e7d6c-2222-4b3c-8d4e-5f6a7b8c9d02",
            "skuId": "7c6b5a49-3333-4c4d-9e5f-6a7b8c9d0e03",
            "productName": "Bàn phím cơ không dây",
            "quantity": 1,
            "unitPrice": 750000.00,
            "lineTotal": 750000.00
          }
        ],
        "itemsAmount": 750000.00,
        "productDiscountAmount": 0.00,
        "shopDiscountAmount": 0.00,
        "systemDiscountAmount": 0.00,
        "discountAmount": 0.00,
        "shippingFeeAmount": 30000.00,
        "totalAmount": 780000.00
      }
    ],
    "appliedVouchers": ["FREESHIP2026"],
    "warnings": null,
    "expiredAt": "2026-11-01T10:15:00Z"
  }
  ```

---

### `CHK-02`: POST `/api/v1/orders` (Tạo đơn hàng chính thức)
* **Quyền:** `Buyer`
* **Cơ chế phòng vệ:**
  - **Atomic Pop**: Xóa ngay phiên `quoteId` khỏi `IDistributedCache` để triệt tiêu Race Condition và lỗi trừ tiền 2 lần khi người dùng bấm liên tiếp.
  - **Asymmetric Price Drift Policy**: Kiểm tra đối soát giá với `CatalogService`. Nếu có sản phẩm **Tăng giá**: Chặn đơn và ném `PRICE_CHANGED` (409) kèm cập nhật giá mới vào Cart để lần bấm sau không bị chặn. Nếu sản phẩm **Giảm giá**: Tự động áp dụng mức giá rẻ hơn (`appliedPriceDrops`), không chặn đơn. Đóng băng giá sau khi tạo đơn (`Post-Checkout Price Lock`).
  - **Hybrid Payment Expiration**: Áp dụng Passive Guard kiểm tra quá hạn khi truy vấn API và Active Sweeper nền quét định kỳ. Tự động hủy đơn, giải phóng voucher và nhả kho Fulfillment.
  - **Khóa giữ kho 15 phút**: Gọi `ReserveStockAsync` tại `FulfillmentService`. Ném lỗi `INSUFFICIENT_STOCK` (400) nếu hết hàng.
  - **Idempotency Key**: Gọi `_walletClient.CheckoutDirectPaymentAsync` kèm `idempotencyKey = "quote-wallet-{quoteId}"`.
  - **Compensation Circuit Breaker**: Nếu `_dbContext.SaveChangesAsync()` ném ngoại lệ (DB crash, constraint failure), hệ thống tự động gọi `_fulfillmentClient.ReleaseStockAsync` nhả kho và `_walletClient.RefundOrderPaymentAsync` hoàn lại 100% tiền ví với `idempotencyKey = "compensation-db-{orderId}"`. Ném lỗi `ORDER_CREATION_FAILED` (500).
* **Request Body:**
  ```json
  {
    "quoteId": "e2a1c4b8-9999-4d8e-b123-5a6b7c8d9e0f",
    "paymentMethod": "Wallet",
    "shippingAddress": "123 Le Loi, Phuong Ben Nghe, Quan 1, TP.HCM",
    "contactPhone": "0901234567",
    "note": "Giao giờ hành chính giúp em"
  }
  ```
* **Response (201 Created):**
  ```json
  {
    "orderId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
    "orderNo": "ORD-20261101-9A0B1C",
    "totalAmount": 780000.00,
    "status": "Paid",
    "paymentDeadlineAt": "2026-11-01T10:15:00Z",
    "paymentUrl": null,
    "subOrderIds": [
      "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08"
    ],
    "createdAt": "2026-11-01T10:00:00Z"
  }
  ```

---

# PHẦN 4: BOUNDED CONTEXT 3 — VÒNG ĐỜI ĐƠN HÀNG & VẬN CHUYỂN (`ORD-01` .. `ORD-13`)

### Bảng tra cứu 8 API Quản lý Đơn hàng:

| Mã | Method | Path | Tên nghiệp vụ | Quyền |
| :---: | :---: | :--- | :--- | :---: |
| **`ORD-01`** | `GET` | `/api/v1/orders` | Danh sách lịch sử đơn hàng của tôi (phân trang) | `Buyer` |
| **`ORD-02`** | `GET` | `/api/v1/orders/{orderId}` | Chi tiết đơn hàng cha (Master Order) | `Buyer, Admin` |
| **`ORD-02B`**| `GET` | `/api/v1/sub-orders/{subOrderId}` | Chi tiết đơn con của Shop (Single DTO) | `Buyer, Seller, Admin` |
| **`ORD-04`** | `POST`| `/api/v1/orders/{orderId}/accept` | Người bán tiếp nhận đơn con (`Confirmed`) | `Seller` |
| **`ORD-05`** | `POST`| `/api/v1/orders/{orderId}/reject` | Người bán từ chối đơn con (`Cancelled` + `SellerRejected`) | `Seller` |
| **`ORD-06`** | `POST`| `/api/v1/orders/{orderId}/cancel` | Người mua hủy đơn hàng (`PendingPayment`) | `Buyer, Admin` |
| **`ORD-08`** | `GET` | `/api/v1/shipments/order/{orderId}` | Theo dõi tiến độ vận chuyển đơn hàng | `Buyer, Seller` |
| **`ORD-13`** | `POST`| `/api/v1/sub-orders/{subOrderId}/refund` | Khởi tạo yêu cầu hoàn tiền cho đơn con | `Buyer, Admin` |

---

### Chi tiết Endpoint Đơn hàng tiêu biểu:

#### `ORD-02B`: GET `/api/v1/sub-orders/{subOrderId}` (Chi tiết Đơn con của Shop)
* **Response (200 OK):** Trả về đối tượng đơn **`SubOrderDetailResponse`** (Single DTO, không bọc phân trang `PagedData`):
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "subOrderNo": "SUB-20261101-8B0C2D",
    "orderId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
    "buyerId": "11111111-1111-1111-1111-000000000004",
    "sellerId": "5e9d1c2b-7f3a-4d8e-9c1b-2a4f6d8e0b12",
    "shopName": "Shop Công Nghệ",
    "status": "Paid",
    "paymentMethod": "Wallet",
    "shippingAddress": {
      "receiverName": "Nguyen Van A",
      "contactPhone": "0901234567",
      "street": "123 Le Loi",
      "ward": "Ben Nghe",
      "district": "Quan 1",
      "province": "TP.HCM",
      "ghnDistrictId": 1442,
      "ghnWardCode": "20101",
      "fullAddressText": "123 Le Loi, Phuong Ben Nghe, Quan 1, TP.HCM"
    },
    "contactPhone": "0901234567",
    "itemsAmount": 750000.00,
    "productDiscountAmount": 0.00,
    "shopDiscountAmount": 0.00,
    "systemDiscountAmount": 0.00,
    "discountAmount": 0.00,
    "shippingFeeAmount": 30000.00,
    "totalAmount": 780000.00,
    "createdAt": "2026-11-01T10:00:00Z",
    "items": [
      {
        "orderItemId": "c2d3e4f5-9999-4a0b-9c1d-3e4f5a6b7c09",
        "productId": "9f8e7d6c-2222-4b3c-8d4e-5f6a7b8c9d02",
        "skuId": "7c6b5a49-3333-4c4d-9e5f-6a7b8c9d0e03",
        "productName": "Bàn phím cơ không dây",
        "skuCode": "KB-WL-01",
        "quantity": 1,
        "unitPrice": 750000.00,
        "lineTotal": 750000.00
      }
    ]
  }
  ```

#### `ORD-05`: POST `/api/v1/orders/{orderId}/reject` (Shop từ chối đơn)
* **Request Body:**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "reasonCode": "OutOfStock",
    "reasonText": "Sản phẩm tạm thời hết hàng trong kho."
  }
  ```
* **Response (200 OK):**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "status": "Cancelled",
    "cancellationReason": "OutOfStock",
    "rejectedAt": "2026-11-01T10:05:00Z"
  }
  ```

---

# PHẦN 5: HỢP ĐỒNG GỌI LIÊN DỊCH VỤ (OUTBOUND TYPED REST CONTRACTS)

Commerce Service giao tiếp đồng bộ với 3 external microservices qua Typed HTTP Clients:

### 5.1. Wallet Service (`:8008` REST) — `IWalletClient`
1. **Trừ tiền ví trực tiếp (`R181`):**
   * **Endpoint:** `POST /api/v1/wallets/checkout/direct`
   * **Headers:** `x-user-id`, `Idempotency-Key: quote-wallet-{quoteId}`
   * **Payload:** `{ "orderId": "guid", "userId": "guid", "amount": 780000.00 }`
2. **Lấy số dư khả dụng thời gian thực:**
   * **Endpoint:** `GET /api/v1/wallets/me/balance`
   * **Headers:** `x-user-id`
   * **Response:** `{ "balance": 1500000.00 }`
3. **Hoàn tiền ví tự động:**
   * **Endpoint:** `POST /api/v1/wallets/refund`
   * **Headers:** `x-user-id`, `Idempotency-Key: refund-suborder-{subOrderId}` hoặc `compensation-db-{orderId}`
   * **Payload:** `{ "subOrderId": "guid", "orderId": "guid", "userId": "guid", "amount": 250000.00, "reason": "string" }`

### 5.2. Fulfillment Service (`:8005` REST) — `IFulfillmentClient`
1. **Kiểm tra tồn kho đơn lẻ (CART-04, CART-05):** `POST /api/v1/fulfillment/inventory/check` $\rightarrow$ `{ "skuId": "guid", "quantity": 2 }`
2. **Quét tồn kho hàng loạt cho giỏ hàng (CART-01, CART-03):** `POST /api/v1/fulfillment/inventory/batch-check` $\rightarrow$ `{ "skuIds": ["guid-1", "guid-2"] }`
3. **Giữ kho tạm thời 15 phút (CHK-02):** `POST /api/v1/fulfillment/inventory/reserve` $\rightarrow$ `{ "orderId": "guid", "ttlMinutes": 15, "items": [...] }`
4. **Trừ kho vĩnh viễn sau thanh toán / COD:** `POST /api/v1/fulfillment/inventory/commit` $\rightarrow$ `{ "orderId": "guid" }`
5. **Nhả tồn kho khi hủy đơn / timeout:** `POST /api/v1/fulfillment/inventory/release` $\rightarrow$ `{ "orderId": "guid" }`
6. **Nhả tồn kho có chủ đích của 1 Shop bị từ chối:** `POST /api/v1/fulfillment/inventory/suborder/release` $\rightarrow$ `{ "subOrderId": "guid", "items": [...] }`
7. **Tính cước vận chuyển GHN động (CHK-01):** `POST /api/v1/fulfillment/shipping/quote` $\rightarrow$ `{ "sellerId": "guid", "toDistrictId": 1442, "toWardCode": "20101" }`
8. **Tạo vận đơn GHN tự động khi Shop xác nhận đơn:** `POST /api/v1/fulfillment/shipments` $\rightarrow$ `{ "subOrderId": "guid" }`

### 5.3. Catalog Service (`:8002` REST) — `ICatalogClient`
1. **Lấy metadata & giá niêm yết thời gian thực:** `GET /api/v1/catalog/skus/{skuId}`
   * Phục vụ kiểm tra biến động giá bất đối xứng (`Asymmetric Price Drift`) và kiểm tra sản phẩm còn bán (`IsPurchasable`).

> **Lưu ý:** Commerce Service **tuyệt đối không tạo Controller** cho các route trên vì chúng thuộc quyền sở hữu của các External Services tương ứng.

---

# PHẦN 6: BẢNG TRA CỨU MÃ LỖI HỆ THỐNG CHUẨN RFC 9110

Frontend bắt lỗi dựa trên trường `errorCode` để xử lý giao diện người dùng:

| HTTP Status | errorCode (`SCREAMING_SNAKE`) | Message mẫu chuẩn Sentence Case | Hướng dẫn xử lý trên giao diện Frontend |
| :---: | :--- | :--- | :--- |
| `400` | `CART_EMPTY` | `"No valid items selected for checkout."` | Giỏ hàng rỗng. Disable nút thanh toán. |
| `400` | `INSUFFICIENT_STOCK` | `"Insufficient stock available in warehouse."` | Hết tồn kho vật lý tại kho Fulfillment. Hiển thị thông báo hoặc làm xám món hàng. |
| `409` | `PRICE_CHANGED` | `"One or more item prices have increased. Cart prices have been refreshed. Please review your order before proceeding."` | Giá sản phẩm tăng (Flash Sale kết thúc). Giỏ đã cập nhật giá mới, yêu cầu xác nhận trước khi đặt lại. |
| `400` | `CATALOG_SKU_INACTIVE` | `"The specified product SKU is unavailable or out of stock."` | Sản phẩm đã bị Shop ngưng bán hoặc ẩn khỏi Catalog. |
| `400` | `QUOTE_EXPIRED` | `"Checkout quote has expired, is invalid, or is currently being processed."` | Báo giá quá hạn 15 phút hoặc đang được xử lý. Yêu cầu tính lại tiền. |
| `400` | `VAL_INVALID_INPUT` | `"Shipping address fields cannot be empty."` | Thiếu địa chỉ/SĐT. Focus vào ô nhập liệu lỗi. |
| `400` | `CART_ITEM_LIMIT_EXCEEDED` | `"Maximum quantity allowed per item is 99."` | Vượt quá 99 món. Chặn nút tăng số lượng. |
| `400` | `ORDER_CANNOT_BE_CANCELLED` | `"Order cannot be cancelled in its current status."` | Đơn đã thanh toán hoặc đã giao. Ẩn nút "Hủy đơn". |
| `400` | `INVALID_STATUS` | `"Sub-order cannot be processed in its current status."` | Thao tác không hợp lệ với trạng thái hiện tại của đơn hàng. |
| `401` | `UNAUTHORIZED` | `"Authentication credentials were not provided or invalid."` | Hết hạn phiên đăng nhập. Chuyển hướng về màn hình Login. |
| `403` | `FORBIDDEN` | `"You do not have permission to access this resource."` | Cố tình truy cập đơn của người khác. Báo cấm quyền. |
| `404` | `CART_NOT_FOUND` | `"Shopping cart was not found."` | Không tìm thấy giỏ hàng của người dùng. |
| `404` | `CART_ITEM_NOT_FOUND` | `"Cart item was not found."` | Sản phẩm đã bị xóa trước đó. Reload lại giỏ. |
| `404` | `CATALOG_SKU_NOT_FOUND` | `"The specified product SKU was not found in catalog."` | Không tìm thấy thông tin sản phẩm trong Catalog. |
| `404` | `ORDER_NOT_FOUND` | `"Order was not found."` | Đơn hàng không tồn tại. Hiển thị màn hình 404 Empty. |
| `404` | `SUB_ORDER_NOT_FOUND` | `"Sub-order was not found."` | Đơn con của Shop không tồn tại. |
| `500` | `ORDER_CREATION_FAILED` | `"An error occurred while saving the order. All inventory allocations and payments have been safely rolled back."` | Lỗi lưu trữ DB; hệ thống đã tự động hoàn tiền và hoàn kho. Mời khách đặt lại. |

---

# PHẦN 7: BỘ TYPESCRIPT TYPE DEFINITIONS HOÀN CHỈNH (FRONTEND DTOs)

Dành cho lập trình viên Frontend (React, Vue, React Native, Flutter) copy dùng ngay:

```typescript
// =============================================================================
// COMMERCE SERVICE FRONTEND CONTRACT TYPES
// =============================================================================

export type PaymentMethod = 'Wallet' | 'VnPay' | 'Cod';
export type OrderStatus = 'PendingPayment' | 'PaymentProcessing' | 'Paid' | 'Cancelled' | 'Expired';
export type SubOrderStatus = 'PendingPayment' | 'Paid' | 'Confirmed' | 'Shipping' | 'Delivered' | 'Completed' | 'Cancelled' | 'Refunded';
export type CancellationReasonCode = 'BuyerChangedMind' | 'OutOfStock' | 'PaymentTimeout' | 'SellerRejected' | 'Other';

// -----------------------------------------------------------------------------
// 1. Giỏ hàng (Cart)
// -----------------------------------------------------------------------------
export interface CartItemResponse {
  cartItemId: string;
  productId: string;
  skuId: string;
  sellerId: string;
  productName: string;
  skuAttributes?: string;
  imageUrl?: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
  isAvailable: boolean;
  disabledReason?: 'OUT_OF_STOCK' | 'PRODUCT_INACTIVE' | string | null;
}

export interface CartResponse {
  cartId: string;
  buyerId: string;
  itemCount: number;
  subtotalAmount: number;
  items: CartItemResponse[];
}

export interface CartSummaryResponse {
  cartId: string;
  itemCount: number;
  subtotalAmount: number;
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
  ghnDistrictId?: number;
  ghnWardCode?: string;
}

export interface CheckoutQuoteRequest {
  cartItemIds: string[];
  shippingAddress: ShippingAddressInput;
  contactPhone: string;
  paymentMethod: PaymentMethod;
  carrierCode?: string;
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

export interface CheckoutQuoteResponse {
  quoteId: string;
  grandTotal: number;
  walletBalance: number;
  shops: ShopOrderQuote[];
  appliedVouchers: string[];
  warnings?: string[];
  expiredAt: string;
}

export interface CreateOrderRequest {
  quoteId: string;
  paymentMethod: PaymentMethod;
  shippingAddress: string;
  contactPhone: string;
  note?: string;
}

export interface PriceDropDto {
  skuId: string;
  productName: string;
  oldPrice: number;
  newPrice: number;
  savedAmount: number;
}

export interface CreateOrderResponse {
  orderId: string;
  orderNo: string;
  totalAmount: number;
  status: OrderStatus;
  paymentDeadlineAt: string;
  paymentUrl?: string | null;
  subOrderIds: string[];
  createdAt: string;
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
  orderItemId: string;
  productId: string;
  skuId: string;
  productName: string;
  skuCode?: string;
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
  orderId: string;
  orderNo: string;
  totalAmount: number;
  status: OrderStatus;
  paymentDeadlineAt: string;
  subOrderCount: number;
  createdAt: string;
}

export interface OrderResponse {
  id: string;
  orderNo: string;
  buyerId: string;
  status: OrderStatus;
  totalAmount: number;
  paymentDeadlineAt: string;
  createdAt: string;
  subOrders: SubOrderDetailResponse[];
}

export interface SubOrderShipmentInfo {
  subOrderId: string;
  subOrderNo: string;
  sellerId: string;
  shopName: string;
  status: string;
  carrierCode?: string;
  trackingCode?: string;
  shippedAt?: string;
  deliveredAt?: string;
}

export interface ShipmentTrackingResponse {
  orderId: string;
  orderNo: string;
  shipments: SubOrderShipmentInfo[];
}
```
