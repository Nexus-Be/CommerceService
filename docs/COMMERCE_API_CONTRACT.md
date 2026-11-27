# COMMERCE SERVICE — MASTER REST API CONTRACT & INTEGRATION SPECIFICATION
## (Hợp Đồng Giao Tiếp RESTful Toàn Diện: Giỏ Hàng, Báo Giá, Đơn Hàng, Khuyến Mãi, Vận Chuyển, Đấu Giá & Liên Dịch Vụ)

> **Phiên bản:** v3.6.0 (Master Unified REST Contract — Sub-Order Lifecycle, Self-Healing Reconciliation, Vouchers & Platform Fee)  
> **Phân hệ:** Commerce Service (`Commerce.Api`)  
> **Cổng dịch vụ (Port):** `8003` (Container & Localhost)  
> **Cơ sở dữ liệu:** PostgreSQL port `5435` (`commerce_db`)  
> **Giao thức:** HTTP/1.1 & HTTP/2 RESTful Web API  
> **Đối tượng sử dụng:** Frontend (Web & Mobile Apps), API Gateway Team, Wallet Service Team, Catalog Service Team, Fulfillment Service Team, Auction Service Team, User Service Team, QA Automation & DevOps  
> **Cập nhật lần cuối:** 2026-11-18  
> **Trạng thái:** ✅ ĐÃ CHUẨN HÓA 100% VỚI CODE THỰC THI (100% RESTFUL, PURE DTOS, 262 UNIT TESTS PASS 100%, 0 WARNINGS, 0 ERRORS)  

---

## MỤC LỤC BỐ CỤC TÀI LIỆU

1. [Phần 1: Quy chuẩn Kiến trúc & Giao thức Chung](#phần-1-quy-chuẩn-kiến-trúc--giao-thức-chung)
   * *1.1 Bounded Context & Phân định Ranh giới*
   * *1.2 Cơ chế Identity Propagation & Headers từ API Gateway*
   * *1.3 Quy tắc Đóng gói Phong bì (Response Envelope Conventions)*
   * *1.4 Quy tắc Vàng về Casing, Tiền tệ VND & Bất biến Kế toán*
2. [Phần 2: Bounded Context 1 — Quản lý Giỏ hàng (`CART-01` .. `CART-14`)](#phần-2-bounded-context-1--quản-lý-giỏ-hàng-cart-01--cart-14)
   * *Nhóm 1: Xem & Khám phá Giỏ hàng (`CART-01` .. `CART-03`)*
   * *Nhóm 2: Thao tác Dòng Sản phẩm (`CART-04` .. `CART-09`)*
   * *Nhóm 3: Tính giá, Thẩm định & Gộp giỏ (`CART-10` .. `CART-12`)*
   * *Nhóm 4: Hậu Đặt hàng & Quản trị (`CART-13` .. `CART-14`)*
3. [Phần 3: Bounded Context 2 — Báo giá & Đặt hàng 2 Bước (`CHK-01` .. `CHK-02`)](#phần-3-bounded-context-2--báo-giá--đặt-hàng-2-bước-chk-01--chk-02)
   * *Bước 1: Báo giá Giỏ hàng Đa Shop (`CHK-01` / `R75`)*
   * *Bước 2: Tạo Đơn hàng Chính thức (`CHK-02` / `R76`)*
4. [Phần 4: Bounded Context 3 — Vòng đời Đơn hàng, Vận chuyển & Phân rã Tài chính (`ORD-01` .. `ORD-19`)](#phần-4-bounded-context-3--vòng-đời-đơn-hàng-vận-chuyển--phân-rã-tài-chính-ord-01--ord-19)
   * *Tra cứu & Lịch sử Đơn hàng (`ORD-01`, `ORD-02`, `ORD-02B`, `ORD-02C`, `ORD-19`)*
   * *Tiếp nhận, Từ chối & Hủy Đơn (`ORD-04`, `ORD-05`, `ORD-06`, `ORD-14`)*
   * *Xác nhận Nhận hàng, Hoàn tất & Giải ngân Escrow (`ORD-07`, `ORD-07B`)*
   * *Thanh toán lại Đơn hàng & Đối soát Hoàn tiền (`ORD-15`, `ORD-16`)*
   * *Bảng kê Phân rã Tài chính cho Người bán (`ORD-17`)*
   * *Theo dõi & Thao tác Vận chuyển (`ORD-08`, `ORD-08B`, `ORD-04B`, `ORD-13`)*
5. [Phần 5: Bounded Context 4 — Phân hệ Quản trị Khuyến mãi & Voucher (`VOUCHER-01` .. `VOUCHER-09`)](#phần-5-bounded-context-4--phân-hệ-quản-trị-khuyến-mãi--voucher-voucher-01--voucher-09)
   * *Tra cứu, Thẩm định & Đề xuất Voucher cho Người mua (`VOUCHER-01` .. `VOUCHER-03`)*
   * *Quản trị & Vận hành Voucher cho Seller & Admin (`VOUCHER-04` .. `VOUCHER-09`)*
6. [Phần 6: Bounded Context 5 — Phân hệ Quản trị Phí sàn (`FEE-01` .. `FEE-02`)](#phần-6-bounded-context-5--phân-hệ-quản-trị-phí-sàn-fee-01--fee-02)
7. [Phần 7: Bounded Context 6 — Phân hệ Đơn hàng Đấu giá (`AUC-01` .. `AUC-09`)](#phần-7-bounded-context-6--phân-hệ-đơn-hàng-đấu-giá-auc-01--auc-09)
8. [Phần 8: Bounded Context 7 — Giao tiếp Nội bộ & Đánh giá Đơn hàng (`INTERNAL-01`)](#phần-8-bounded-context-7--giao-tiếp-nội-bộ--đánh-giá-đơn-hàng-internal-01)
9. [Phần 9: Webhook Tiếp nhận Trạng thái Giao vận (Carrier ➔ Fulfillment ➔ Commerce)](#phần-9-webhook-tiếp-nhận-trạng-thái-giao-vận)
10. [Phần 10: Hợp đồng Outbound Typed REST Clients (Gọi Liên Dịch Vụ)](#phần-10-hợp-đồng-outbound-typed-rest-clients-gọi-liên-dịch-vụ)
11. [Phần 11: Bảng Tra cứu Mã Lỗi Hệ thống Chuẩn RFC 9110 Toàn Diện](#phần-11-bảng-tra-cứu-mã-lỗi-hệ-thống-chuẩn-rfc-9110-toàn-diện)
12. [Phần 12: Bộ TypeScript Type Definitions Hoàn chỉnh (Frontend & SDK DTOs)](#phần-12-bộ-typescript-type-definitions-hoàn-chỉnh-frontend--sdk-dtos)

---

# PHẦN 1: QUY CHUẨN KIẾN TRÚC & GIAO THỨC CHUNG

### 1.1. Bounded Context & Phân Định Ranh Giới

Commerce Service là **Hạt nhân Điều phối Giao dịch (Transactional Core)** sở hữu độc quyền:
1. **Quản lý Giỏ hàng (`Carts`, `CartItems`)**: Mỗi Người mua (Buyer) sở hữu tối đa 1 Giỏ hàng `Active`. Giới hạn tối đa **50 món** (distinct items) và tối đa **99 đơn vị/món**. Hỗ trợ checkbox lựa chọn tính toán (`CalculateSelected`), đổi biến thể trực tiếp (`ChangeVariant`), và gộp giỏ vãng lai (`MergeGuestCart`).
2. **Đơn hàng cha & Đơn con theo Shop (`Orders`, `SubOrders`, `SubOrderItems`)**:
   - **Đơn hàng cha (Master Order)** quản lý tổng thanh toán, phương thức thanh toán, hạn thanh toán (TTL 5 phút) và địa chỉ nhận hàng.
   - **Đơn hàng con (Shop Sub-Order)** quản lý gói hàng riêng của từng Người bán (Seller), cước vận chuyển GHN riêng, vận đơn GHN riêng, voucher riêng, và giải ngân Escrow độc lập.
3. **Phân hệ Khuyến mãi & Voucher (`DiscountCodes`, `DiscountCodeScopes`, `DiscountCodeUsages`)**:
   - Hệ thống voucher đa tầng: Product Voucher, Shop Voucher, và System Voucher.
   - Vòng đời giữ chỗ nguyên tử (`Reserved` $\rightarrow$ `Used` hoặc `Released`).
4. **Quản trị Phí sàn Động (`PlatformFee`)**: Cấu hình tỷ lệ % phí sàn phân phối qua Redis cache, hỗ trợ Staff/Admin điều chỉnh linh hoạt.
5. **Đơn hàng Đấu giá (`AuctionOrders`)**: Vòng đời đơn đấu giá tiếp nhận từ Auction Service, hỗ trợ khấu trừ cọc, thanh toán ví, seller duyệt, và bồi hoàn độc lập.
6. **Snapshot Bất biến (Immutable Snapshots)**:
   - `UnitPriceSnapshot`: Ghi nhận đơn giá tại thời điểm thêm vào giỏ.
   - `ShippingAddressSnapshot`: Lưu trữ thông tin người nhận (Họ tên, SĐT, Địa chỉ, Mã bưu cục GHN) dưới dạng cột PostgreSQL Native `JSONB` trong bảng `Orders` và `SubOrders`. Khi người dùng chỉnh sửa thông tin cá nhân, đơn hàng lịch sử **tuyệt đối không bị thay đổi**.
7. **Ranh giới KHÔNG thuộc sở hữu của Commerce Service (External Collaborations)**:
   - **Số dư tiền, Trừ tiền ví, Hoàn tiền ví & Giải ngân Escrow** ➔ Do **Wallet Service** (`:8008`) sở hữu (gọi qua `IWalletClient`).
   - **Thông tin sản phẩm niêm yết (Master Data, Tên, Giá bán, Trạng thái bán)** ➔ Do **Catalog Service** (`:8002`) sở hữu (gọi qua `ICatalogClient`).
   - **Tồn kho vật lý, Giữ hàng 15m, Trừ/Nhả kho & Vận đơn GHN** ➔ Do **Fulfillment Service** (`:8006`) sở hữu (gọi qua `IFulfillmentClient`).
   - **Xác thực danh tính & Điểm uy tín** ➔ Do **User Service** (`:8001`) sở hữu (gọi qua `IUserClient`).
   - **Thông báo sự kiện đấu giá** ➔ Do **Auction Service** (`:8005`) sở hữu (gọi qua `IAuctionClient`).

---

### 1.2. Cơ Chế Identity Propagation & Headers từ API Gateway

Toàn bộ yêu cầu từ Frontend (Web/Mobile) gửi qua API Gateway được middleware tiếp nhận, trích xuất và chuyển tiếp:

```
[ Frontend Client ] 
       │ HTTPS Request (Bearer JWT)
       ▼
[ API Gateway (:80 / :443) ]
       │ Headers: x-user-id, x-user-roles, Authorization
       ▼
[ UserContextMiddleware (:8003) ] ──▶ Thiết lập ClaimsPrincipal trên HttpContext.User
       │
[ Commerce Controllers ]          ──▶ CartsController / OrdersController / DiscountsController ...
       │
[ IdentityPropagationHandler ]    ──▶ Tự động chèn Headers sang Outbound HttpClients
       ▼
[ External Microservices ] (Catalog :8002, Fulfillment :8006, Wallet :8008, User :8001, Auction :8005)
```

| Header Name | Kiểu Dữ Liệu | Bắt Buộc | Diễn Giải & Nguồn Trích Xuất | Ví Dụ |
| - | - | :-: | - | - |
| `x-user-id` | UUID (v4) | **Có** | ID người dùng xác thực từ Claim `sub` | `11111111-1111-1111-1111-000000000004` |
| `x-user-roles` | String | **Có** | Danh sách vai trò người dùng (`buyer`, `seller`, `admin`, `staff`) | `buyer` hoặc `buyer,seller` |
| `Authorization` | String | Tùy chọn | JWT Bearer token truyền từ Gateway | `Bearer eyJhbGciOi...` |
| `x-user-email` | String | Tùy chọn | Email người dùng phục vụ log kiểm toán | `buyer@nexus.local` |
| `x-trace-id` | String | Tùy chọn | W3C / OpenTelemetry Distributed Trace ID | `00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01` |
| `Idempotency-Key`| UUID (v4) | Tùy chọn | Khóa chống trùng lặp giao dịch (Header) | `9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d` |
| `X-Internal-Service-Key` | String | Nội bộ | Khóa xác thực liên microservice (Webhook/Internal) | `<secret-key>` |

---

### 1.3. Quy Tắc Đóng Gói Phong Bì (Response Envelope Conventions)

* **Tại tầng Controller C# (Nội bộ Commerce Service)**: Controller **CHỈ TRẢ VỀ PURE DTO** (DTO thuần túy) kèm HTTP Status Code chuẩn RFC 9110. Tuyệt đối không bọc lặp `ApiResponse<T>` trong mã nguồn C#.
* **Tại tầng API Gateway**: Gateway tự động bọc vỏ phong bì chuẩn hóa trước khi trả về cho Frontend Internet:
  ```json
  {
    "success": true,
    "data": { ... Pure DTO ... },
    "error": null,
    "timestamp": "2026-11-18T00:00:00.000Z",
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
  }
  ```
* **Khi có lỗi (RFC 9110 Problem Details)**:
  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    "title": "Bad Request",
    "status": 400,
    "errorCode": "VAL_INVALID_INPUT",
    "detail": "Quantity must be greater than zero.",
    "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
  }
  ```

---

### 1.4. Quy Tắc Vàng về Casing, Tiền Tệ VND & Bất Biến Kế Toán

1. **Quy chuẩn Casing:**
   - URL Route: 100% `kebab-case` và số nhiều (`/api/v1/carts`, `/api/v1/orders`, `/api/v1/sub-orders`, `/api/v1/discounts`, `/api/v1/auction-orders`).
   - JSON Body: 100% `camelCase` (ví dụ: `cartItemId`, `sellerReceiveAmount`, `paymentDeadlineAt`).
   - JSON Enums: 100% chuỗi `PascalCase` (ví dụ: `"PendingPayment"`, `"Paid"`, `"Delivered"`, `"Completed"`, `"Cancelled"`).
   - Mã lỗi: 100% `SCREAMING_SNAKE_CASE` (ví dụ: `CART_ITEM_NOT_FOUND`, `INSUFFICIENT_WALLET_BALANCE`).
   - Thông báo lỗi (`detail`): 100% tiếng Anh, viết chuẩn Sentence case (ví dụ: `"Sub-order cannot be confirmed until it is delivered."`).
2. **Quy chuẩn Tiền tệ VND:**
   - Toàn bộ số tiền tại thị trường Việt Nam (VND) phải làm tròn về **số nguyên đồng (0 chữ số thập phân)** bằng `Math.Round(x, 0, MidpointRounding.AwayFromZero)`.
3. **Bất biến Kế toán Tài chính Hai Vế (Financial Accounting Invariant):**
   Mọi đơn hàng con (`SubOrder`) bắt buộc phải thỏa mãn phương trình kế toán bất biến:
   $$\text{TotalAmount} = \text{SellerReceiveAmount} + (\text{PlatformFeeAmount} - \text{SystemDiscountAmount}) + \text{ShippingFeeAmount}$$
   Trong đó:
   - $\text{NetSeller} = \max(0, \text{ItemsAmount} - \text{ProductDiscountAmount} - \text{ShopDiscountAmount})$
   - $\text{PlatformFeeAmount} = \text{Round}(\text{NetSeller} \times \frac{\text{PlatformMarkupPercent}}{100})$
   - $\text{SellerReceiveAmount} = \max(0, \text{NetSeller} - \text{PlatformFeeAmount})$
   - $\text{TotalAmount} = \max(0, \text{NetSeller} - \text{SystemDiscountAmount}) + \text{ShippingFeeAmount}$
4. **Phương thức Thanh toán:** Chỉ hỗ trợ duy nhất 2 phương thức thanh toán:
   - `PaymentMethod.Wallet` (`0` / `"Wallet"`): Thanh toán qua số dư ví số Nexus.
   - `PaymentMethod.Cod` (`2` / `"Cod"`): Thanh toán tiền mặt khi nhận hàng.
   *(VNPay đã bãi bỏ hoàn toàn, trả về HTTP 400 nếu gửi `VnPay`)*.

---

# PHẦN 2: BOUNDED CONTEXT 1 — QUẢN LÝ GIỎ HÀNG (`CART-01` .. `CART-14`)

> **Bộ điều khiển:** `CartsController` (`api/v1/carts`)  
> **Quyền truy cập:** `buyer`, `admin`, `staff` (Tự động xác thực quyền sở hữu qua `HttpContext.GetUserId()`)

### 2.1. Danh mục Endpoint Giỏ hàng

| Mã | Method | Endpoint | Tên / Nghiệp Vụ | DTO Đầu Vào | DTO Đầu Ra |
| - | :-: | - | - | - | - |
| `CART-01` | `GET` | `/api/v1/carts/me` | Lấy hoặc tự tạo giỏ hàng Active | Không | `CartDto` |
| `CART-02` | `GET` | `/api/v1/carts/me/summary` | Badge số lượng món & tổng tiền tạm tính | Không | `CartSummaryResponse` |
| `CART-03` | `GET` | `/api/v1/carts/me/grouped` | Giỏ hàng phân nhóm theo từng Shop | Không | `GroupedCartResponse` |
| `CART-04` | `POST` | `/api/v1/carts/me/items` | Thêm SKU vào giỏ hàng (Check tồn & giá) | `AddCartItemRequest` | `CartDto` |
| `CART-05` | `PATCH` | `/api/v1/carts/me/items/{cartItemId}` | Cập nhật số lượng món (1..99) | `UpdateCartItemQuantityRequest` | `CartDto` |
| `CART-06` | `PATCH` | `/api/v1/carts/me/items/{cartItemId}/variant` | Đổi biến thể SKU trực tiếp trong giỏ | `ChangeCartItemVariantRequest` | `CartDto` |
| `CART-07` | `DELETE` | `/api/v1/carts/me/items/{cartItemId}` | Xóa 1 dòng sản phẩm khỏi giỏ | Không | `CartDto` |
| `CART-08` | `POST` | `/api/v1/carts/me/items:bulk-remove` | Xóa hàng loạt các món được chọn | `BulkRemoveCartItemsRequest` | `CartDto` |
| `CART-09` | `DELETE` | `/api/v1/carts/me/clear` | Dọn sạch toàn bộ giỏ hàng | Không | `CartDto` |
| `CART-10` | `POST` | `/api/v1/carts/me/calculate` | Tính tổng tiền các món tick chọn (Shopee checkbox) | `CalculateSelectedCartItemsRequest` | `CalculateSelectedCartResponse` |
| `CART-11` | `POST` | `/api/v1/carts/me/validate` | Kiểm định tồn kho & giá tươi trước checkout | `ValidateCartRequest` | `ValidateCartResponse` |
| `CART-12` | `POST` | `/api/v1/carts/me/merge` | Gộp giỏ hàng khách vãng lai sau khi đăng nhập | `MergeGuestCartRequest` | `CartDto` |
| `CART-13` | `POST` | `/api/v1/carts/me/cleanup-checked-out` | Dọn dẹp các món đã thanh toán thành công | `CleanupCheckedOutItemsRequest` | `CartDto` |
| `CART-14` | `GET` | `/api/v1/carts/active` | Tra cứu giỏ hàng Active theo Buyer ID (Admin/Staff) | Query: `buyerId` | `CartDto` |

---

### 2.2. Chi Tiết Một Số Endpoint Giỏ Hàng Quan Trọng

#### `CART-03`: Giỏ Hàng Phân Nhóm Theo Shop (Grouped Cart)
* **Endpoint:** `GET /api/v1/carts/me/grouped`
* **Response (HTTP 200 OK):**
  ```json
  {
    "cartId": "a1b2c3d4-1111-4a5b-8c9d-1e2f3a4b5c6d",
    "totalItemCount": 3,
    "totalUniqueItems": 2,
    "subtotalAmount": 750000,
    "shops": [
      {
        "sellerId": "shop1111-1111-1111-1111-000000000001",
        "shopName": "Nexus Official Store",
        "itemCount": 2,
        "shopSubtotal": 500000,
        "items": [
          {
            "id": "c1111111-1111-1111-1111-000000000001",
            "productId": "p1111111-1111-1111-1111-000000000001",
            "skuId": "s1111111-1111-1111-1111-000000000001",
            "productName": "Áo Thun Cotton Nexus - Đen M",
            "quantity": 2,
            "unitPrice": 250000,
            "lineTotal": 500000,
            "isAvailable": true,
            "currentStock": 50,
            "hasPriceChanged": false,
            "originalPrice": 250000
          }
        ]
      }
    ]
  }
  ```

#### `CART-10`: Tính Tổng Tiền Cho Các Món Tick Chọn (Shopee Checkbox)
* **Endpoint:** `POST /api/v1/carts/me/calculate`
* **Request Body:**
  ```json
  {
    "selectedItemIds": [
      "c1111111-1111-1111-1111-000000000001"
    ]
  }
  ```
* **Response (HTTP 200 OK):**
  ```json
  {
    "selectedItemCount": 2,
    "selectedSubtotalAmount": 500000,
    "hasInvalidItems": false,
    "invalidItemIds": []
  }
  ```

---

# PHẦN 3: BOUNDED CONTEXT 2 — BÁO GIÁ & ĐẶT HÀNG 2 BƯỚC (`CHK-01` .. `CHK-02`)

Commerce Service áp dụng **Quy trình Thanh toán 2 Bước (Two-Phase Checkout)** để đảm bảo tính bất biến về giá, phí vận chuyển GHN và hạn mức voucher:

```
[ Step 1: POST /api/v1/checkouts/quote ] ──▶ Sinh QuoteId (Redis TTL 5m) kèm cước ship GHN & Voucher
                                                    │
                                                    ▼
[ Step 2: POST /api/v1/orders ]          ──▶ Gửi QuoteId, kiểm tra ví, giữ kho & chốt đơn
```

---

### `CHK-01 / R75`: Báo Giá Thanh Toán Xem Trước (Checkout Quote)
* **Endpoint:** `POST /api/v1/checkouts/quote`
* **Vai trò:** `buyer`, `admin`, `staff`
* **Header:** `Authorization: Bearer <token>`
* **Request Body:**
  ```json
  {
    "cartItemIds": [
      "c1111111-1111-1111-1111-000000000001"
    ],
    "shippingAddress": {
      "receiverName": "Nguyen Van A",
      "contactPhone": "0901234567",
      "street": "123 Le Loi",
      "ward": "Ben Nghe Ward",
      "district": "District 1",
      "province": "Ho Chi Minh City",
      "provinceId": 202,
      "ghnDistrictId": 1442,
      "ghnWardCode": "20101"
    },
    "contactPhone": "0901234567",
    "paymentMethod": "Wallet",
    "carrierCode": "GHN",
    "voucherCodes": ["NEXUSWELCOME", "FREESHIP50"]
  }
  ```
* **Response (HTTP 200 OK):**
  ```json
  {
    "quoteId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
    "grandTotal": 525000,
    "walletBalance": 1000000,
    "shops": [
      {
        "sellerId": "shop1111-1111-1111-1111-000000000001",
        "shopName": "Nexus Official Store",
        "items": [
          {
            "cartItemId": "c1111111-1111-1111-1111-000000000001",
            "productId": "p1111111-1111-1111-1111-000000000001",
            "skuId": "s1111111-1111-1111-1111-000000000001",
            "productName": "Áo Thun Cotton Nexus - Đen M",
            "quantity": 2,
            "unitPrice": 250000,
            "lineTotal": 500000
          }
        ],
        "itemsAmount": 500000,
        "productDiscountAmount": 0,
        "shopDiscountAmount": 25000,
        "systemDiscountAmount": 0,
        "discountAmount": 25000,
        "shippingFeeAmount": 50000,
        "totalAmount": 525000
      }
    ],
    "appliedVouchers": ["NEXUSWELCOME"],
    "warnings": [],
    "expiredAt": "2026-11-18T00:15:00Z"
  }
  ```

---

### `CHK-01B`: Tính Lại Phí Vận Chuyển GHN Khi Đổi Địa Chỉ (Recalculate Checkout Quote)
* **Endpoint:** `POST /api/v1/checkouts/quote/{quoteId}/recalculate`
* **Vai trò:** `buyer`, `admin`, `staff`
* **Header:** `Authorization: Bearer <token>`
* **Request Body:**
  ```json
  {
    "addressId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "shippingAddress": null,
    "contactPhone": null
  }
  ```
* **Response (HTTP 200 OK):**
  Trả về `CheckoutQuoteResponse` mới với cước phí GHN đã cập nhật theo địa chỉ mới, bảo lưu voucher và giỏ hàng.
* **Lỗi (HTTP 404 Not Found):**
  - `QUOTE_NOT_FOUND`: Báo giá không tồn tại hoặc đã quá hạn 5 phút.
  - `ADDRESS_NOT_FOUND`: Địa chỉ theo `addressId` không tồn tại trong sổ địa chỉ.
* **Lỗi (HTTP 403 Forbidden):**
  - `FORBIDDEN`: Người dùng không phải chủ sở hữu báo giá.

---

### `CHK-02 / R76`: Tạo Đơn Hàng Chính Thức (Place Order)
* **Endpoint:** `POST /api/v1/orders`
* **Vai trò:** `buyer`, `admin`, `staff`
* **Header:** `Authorization: Bearer <token>`, `Idempotency-Key: <UUID>`
* **Request Body:**
  ```json
  {
    "quoteId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
    "paymentMethod": "Wallet",
    "shippingAddress": "123 Le Loi, Ben Nghe Ward, District 1, Ho Chi Minh City",
    "contactPhone": "0901234567",
    "note": "Giao hang gio hanh chinh"
  }
  ```
* **Xử lý An Toàn (Safe Checkout & Compensation):**
  1. Nếu thanh toán qua `Wallet` thành công $\rightarrow$ Chốt đơn `Paid`, trừ kho vĩnh viễn, xóa món đã mua khỏi giỏ.
  2. Nếu số dư ví không đủ $\rightarrow$ Trả về HTTP 400 `INSUFFICIENT_WALLET_BALANCE`, tự động hoàn trả kho dự lưu và **giữ nguyên giỏ hàng của khách**.
  3. Nếu lỗi mạng hoặc sự cố DB sau khi trừ ví $\rightarrow$ Kích hoạt chuỗi bồi hoàn bù trừ (**Compensation Rollback**): Hoàn tiền ví 100% bằng khóa idempotent và nhả tồn kho tức thời.
* **Response (HTTP 201 Created):**
  ```json
  {
    "orderId": "8b9c0d1e-9999-4a5b-8c9d-1e2f3a4b5c6d",
    "orderNo": "ORD-20261118-8B9C0D",
    "totalAmount": 525000,
    "status": "Paid",
    "paymentDeadlineAt": "2026-11-18T00:15:00Z",
    "paymentUrl": null,
    "subOrderIds": [
      "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08"
    ],
    "createdAt": "2026-11-18T00:00:00Z",
    "appliedPriceDrops": []
  }
  ```

---

# PHẦN 4: BOUNDED CONTEXT 3 — VÒNG ĐỜI ĐƠN HÀNG, VẬN CHUYỂN & PHÂN RÃ TÀI CHÍNH (`ORD-01` .. `ORD-19`)

> **Bộ điều khiển:** `OrdersController` (`api/v1/orders`, `api/v1/sub-orders`, `api/v1/shipments`)  
> **Quyền truy cập:** `buyer`, `seller`, `admin`, `staff`

### 4.1. Danh mục Endpoint Đơn hàng & Vận chuyển

| Mã | Method | Endpoint | Vai trò | Nghiệp Vụ Cốt Lõi |
| - | :-: | - | :-: | - |
| `ORD-01 / R78` | `GET` | `/api/v1/orders` | Buyer | Lịch sử đơn hàng của tôi (phân trang DB `page`, `pageSize`, lọc `status`). |
| `ORD-02 / R77` | `GET` | `/api/v1/orders/{orderId}` | Buyer/Seller/Admin | Chi tiết đơn hàng cha (Master Order kèm danh sách SubOrders). |
| `ORD-02B / R79`| `GET` | `/api/v1/sub-orders/{subOrderId}` | Buyer/Seller/Admin | Chi tiết đơn con của Shop (Single DTO bóc tách chi tiết discount). |
| `ORD-02C` | `GET` | `/api/v1/sub-orders` | Seller | Seller tra cứu danh sách đơn con của Shop mình (phân trang DB, lọc `status`). |
| `ORD-04 / R83` | `POST` | `/api/v1/orders/{orderId}/accept` | Seller | Shop tiếp nhận đơn con (`Confirmed` + tự động sinh vận đơn GHN). |
| `ORD-05 / R84` | `POST` | `/api/v1/orders/{orderId}/reject` | Seller | Shop từ chối đơn con (Tự động nhả kho và hoàn tiền ví 100%). |
| `ORD-06 / R80` | `POST` | `/api/v1/orders/{orderId}/cancel` | Buyer | Người mua hủy đơn cha khi còn ở `PendingPayment` / `Paid` (chưa giao). |
| `ORD-07` | `POST` | `/api/v1/sub-orders/{subOrderId}/confirm-receipt` | Buyer | Buyer xác nhận đã nhận hàng đơn con (Yêu cầu nghiêm ngặt `Delivered` $\rightarrow$ giải ngân Escrow). |
| `ORD-07B` | `POST` | `/api/v1/orders/{orderId}/complete` | Buyer | Buyer xác nhận hoàn tất toàn bộ đơn cha (yêu cầu tất cả đơn con đã `Delivered`). |
| `ORD-14 / R102`| `POST` | `/api/v1/sub-orders/{subOrderId}/cancel` | Buyer | Buyer hủy riêng 1 đơn con của Shop cụ thể trước khi hàng xuất kho. |
| `ORD-15` | `POST` | `/api/v1/orders/{orderId}/pay` | Buyer | Thanh toán lại đơn hàng đang ở `PendingPayment` trong hạn 5 phút. |
| `ORD-16` | `GET` | `/api/v1/orders/{orderId}/refunds` | Buyer | Tra cứu lịch sử hoàn tiền chi tiết của đơn hàng. |
| `ORD-17` | `GET` | `/api/v1/sub-orders/{subOrderId}/financial-breakdown` | Buyer/Seller | Bảng kê phân rã tài chính, doanh thu thực nhận và phí sàn chi tiết cho Seller. |
| `ORD-08 / R87` | `GET` | `/api/v1/orders/{orderId}/shipments` | Buyer/Seller | Tra cứu danh sách vận đơn và tiến độ giao hàng của đơn cha (alias: `/api/v1/shipments/order/{orderId}`). |
| `ORD-08B / R101`| `GET` | `/api/v1/sub-orders/{subOrderId}/shipments/timeline` | Buyer/Seller | Dòng thời gian chi tiết hành trình vận chuyển của đơn con. |
| `ORD-04B / R100`| `POST` | `/api/v1/sub-orders/{subOrderId}/shipments/retry` | Seller | Tạo lại vận đơn GHN khi gặp sự cố mạng tạm thời. |
| `ORD-13 / R99` | `POST` | `/api/v1/sub-orders/{subOrderId}/refund` | Buyer | Khởi tạo yêu cầu hoàn tiền cho đơn con. |
| `ORD-19` | `GET` | `/api/v1/orders/buyer/{buyerId}` | Buyer/Admin | Tương thích ngược: Tra cứu danh sách đơn theo `buyerId`. |

---

### 4.2. Chi Tiết Một Số Endpoint Đơn Hàng Mới

#### `ORD-14 / R102`: Người Mua Hủy Riêng Đơn Hàng Con (Sub-Order Cancellation)
* **Endpoint:** `POST /api/v1/sub-orders/{subOrderId}/cancel`
* **Quyền:** `buyer`, `admin`, `staff` (Kiểm tra quyền sở hữu)
* **Điều kiện:** Đơn con phải ở trạng thái `PendingPayment`, `Paid` hoặc `Confirmed` (chưa chuyển `Shipped`).
* **Request Body:**
  ```json
  {
    "reasonCode": "BuyerChangedMind",
    "reasonNote": "Shop delivery is delayed."
  }
  ```
* **Response (HTTP 200 OK):**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "status": "Cancelled",
    "cancelledAt": "2026-11-18T00:05:00Z",
    "refundAmount": 525000,
    "inventoryReleased": true,
    "carrierShipmentCancelled": true
  }
  ```

#### `ORD-15`: Người Mua Thanh Toán Lại Đơn Hàng (Order Repay)
* **Endpoint:** `POST /api/v1/orders/{orderId}/pay`
* **Quyền:** `buyer`, `admin`, `staff`
* **Mô tả:** Khi đơn hàng ở trạng thái `PendingPayment` (do lỗi số dư trước đó), người mua nạp tiền và gọi API này để thanh toán lại trong vòng 5 phút.
* **Response (HTTP 200 OK):**
  ```json
  {
    "orderId": "8b9c0d1e-9999-4a5b-8c9d-1e2f3a4b5c6d",
    "status": "Paid",
    "totalAmount": 525000,
    "paidAt": "2026-11-18T00:08:00Z",
    "walletTransactionId": "w1111111-2222-3333-4444-555555555555"
  }
  ```

#### `ORD-17`: Bảng Kê Phân Rã Tài Chính Cho Người Bán & Người Mua
* **Endpoint:** `GET /api/v1/sub-orders/{subOrderId}/financial-breakdown`
* **Quyền:** `seller` (chủ shop) hoặc `buyer` (người mua)
* **Response (HTTP 200 OK):**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "subOrderNo": "SUB-20261118-B1C2D3",
    "sellerId": "shop1111-1111-1111-1111-000000000001",
    "buyerId": "user1111-1111-1111-1111-000000000001",
    "totalAmount": 525000,
    "itemsAmount": 500000,
    "productDiscountAmount": 0,
    "shopDiscountAmount": 25000,
    "systemDiscountAmount": 0,
    "shippingFeeAmount": 50000,
    "platformFeeAmount": 23750,
    "sellerReceiveAmount": 451250,
    "isEscrowSettled": true,
    "status": "Completed",
    "paymentMethod": "Wallet",
    "completedAt": "2026-11-18T00:10:00Z"
  }
  ```

#### `ORD-16`: Tra Cứu Lịch Sử Hoàn Tiền Đơn Hàng
* **Endpoint:** `GET /api/v1/orders/{orderId}/refunds`
* **Response (HTTP 200 OK):**
  ```json
  [
    {
      "id": "ref11111-1111-1111-1111-000000000001",
      "orderId": "8b9c0d1e-9999-4a5b-8c9d-1e2f3a4b5c6d",
      "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
      "amount": 525000,
      "refundType": "Full",
      "status": "Completed",
      "reasonCode": "BuyerChangedMind",
      "reasonNote": "Buyer cancelled sub-order.",
      "requestedAt": "2026-11-18T00:05:00Z",
      "completedAt": "2026-11-18T00:05:02Z"
    }
  ]
  ```

---

# PHẦN 5: BOUNDED CONTEXT 4 — PHÂN HỆ QUẢN TRỊ KHUYẾN MÃI & VOUCHER (`VOUCHER-01` .. `VOUCHER-09`)

> **Bộ điều khiển:** `DiscountsController` (`api/v1/discounts`)  
> **Quyền hạn:** `buyer` (khám phá/validate), `seller` (tạo/quản lý voucher của Shop), `admin`/`staff` (quản trị toàn sàn)

### 5.1. Danh mục Endpoint Voucher & Khuyến Mãi

| Mã | Method | Endpoint | Vai trò | Nghiệp Vụ Cốt Lõi |
| - | :-: | - | :-: | - |
| `VOUCHER-01` | `GET` | `/api/v1/discounts/available` | Buyer | Lấy danh sách voucher khả dụng kèm cờ thẩm định `isEligible` & `estimatedDiscountAmount`. |
| `VOUCHER-02` | `POST` | `/api/v1/discounts/validate` | Buyer | Thẩm định mã voucher đơn lẻ với giỏ hàng hiện tại. |
| `VOUCHER-03` | `POST` | `/api/v1/discounts/recommend` | Buyer | Đề xuất tổ hợp voucher tối ưu nhất cho người mua (Shopee Best Deal). |
| `VOUCHER-04` | `POST` | `/api/v1/discounts` | Seller/Admin | Tạo mã khuyến mãi mới (Kiểm tra quyền Seller qua UserService). |
| `VOUCHER-05` | `GET` | `/api/v1/discounts` | Seller/Admin | Danh sách khuyến mãi có phân trang, lọc theo `scopeType`, `status`, `search`. |
| `VOUCHER-06` | `GET` | `/api/v1/discounts/{discountId}` | Seller/Admin | Chi tiết một mã khuyến mãi kèm danh sách scope. |
| `VOUCHER-07` | `PUT` | `/api/v1/discounts/{discountId}` | Seller/Admin | Cập nhật thông tin voucher (tên, giới hạn, thời hạn). |
| `VOUCHER-08` | `PATCH` | `/api/v1/discounts/{discountId}/status` | Seller/Admin | Đổi trạng thái voucher (`Draft`, `Active`, `Inactive`, `Expired`). |
| `VOUCHER-09` | `DELETE` | `/api/v1/discounts/{discountId}` | Seller/Admin | Xóa mềm mã khuyến mãi (`IsDeleted = true`). |

---

### 5.2. Chi Tiết Một Số Endpoint Voucher Quan Trọng

#### `VOUCHER-01`: Lấy Danh Sách Voucher Khả Dụng Cho Người Mua
* **Endpoint:** `GET /api/v1/discounts/available?sellerId={guid}&cartItemIds={guid1,guid2}&page=1&pageSize=20`
* **Response (HTTP 200 OK):**
  ```json
  {
    "items": [
      {
        "id": "disc1111-1111-1111-1111-000000000001",
        "code": "NEXUS50K",
        "name": "Giam 50k cho don tu 300k",
        "scopeType": "System",
        "ownerSellerId": null,
        "discountType": "FixedAmount",
        "discountValue": 50000,
        "minOrderAmount": 300000,
        "maxDiscountAmount": 50000,
        "startsAt": "2026-11-01T00:00:00Z",
        "endsAt": "2026-11-30T23:59:59Z",
        "isEligible": true,
        "ineligibleReason": null,
        "estimatedDiscountAmount": 50000
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

#### `VOUCHER-02`: Thẩm Định Mã Voucher Đơn Lẻ Với Giỏ Hàng
* **Endpoint:** `POST /api/v1/discounts/validate`
* **Vai trò:** `buyer`, `admin`, `staff`
* **Bảo mật & Ràng buộc:**
  - **Chống IDOR**: Mọi `cartItemIds` gửi lên bắt buộc phải thuộc về giỏ hàng đang hoạt động của chính người mua (`buyerId`). Nếu có bất kỳ ID nào không thuộc về người mua hoặc không tồn tại, trả về HTTP 400 `VAL_INVALID_INPUT`.
  - **Giới hạn số lượng tổng (`MaxUsesTotal`)**: Nếu voucher đã đạt giới hạn `UsedCount + ReservedCount >= MaxUsesTotal`, trả về `200 OK` với `isValid: false`, `ineligibleReason: "VOUCHER_OUT_OF_STOCK"`, `message: "Voucher has reached its total usage limit."`.
  - **Giới hạn số lượt trên mỗi khách hàng (`MaxUsesPerUser`)**: Nếu người mua đã dùng hoặc đang giữ chỗ đạt ngưỡng (`DiscountCodeUsages` với trạng thái khác `Released`), trả về `200 OK` với `isValid: false`, `ineligibleReason: "VOUCHER_USAGE_LIMIT_EXCEEDED"`, `message: "You have reached the usage limit for this voucher."`.
* **Request Body:**
  ```json
  {
    "code": "SHOP20K",
    "cartItemIds": [
      "c1111111-1111-1111-1111-000000000001"
    ]
  }
  ```
* **Response (HTTP 200 OK - Hợp lệ):**
  ```json
  {
    "isValid": true,
    "discountId": "d1111111-1111-1111-1111-000000000001",
    "code": "SHOP20K",
    "scopeType": "Shop",
    "estimatedDiscountAmount": 20000.0,
    "ineligibleReason": null,
    "message": null
  }
  ```
* **Response (HTTP 200 OK - Không hợp lệ do hết lượt):**
  ```json
  {
    "isValid": false,
    "discountId": null,
    "code": null,
    "scopeType": null,
    "estimatedDiscountAmount": 0.0,
    "ineligibleReason": "VOUCHER_OUT_OF_STOCK",
    "message": "Voucher has reached its total usage limit."
  }
  ```
* **Response (HTTP 400 Bad Request - Vi phạm IDOR hoặc giỏ hàng trống):**
  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    "title": "Bad request",
    "status": 400,
    "errorCode": "VAL_INVALID_INPUT",
    "message": "No valid cart items provided for voucher validation."
  }
  ```

#### `VOUCHER-03`: Đề Xuất Tổ Hợp Voucher Tối Ưu (Shopee Best Deal Recommendation)
* **Endpoint:** `POST /api/v1/discounts/recommend`
* **Request Body:**
  ```json
  {
    "cartItemIds": [
      "c1111111-1111-1111-1111-000000000001"
    ]
  }
  ```
* **Response (HTTP 200 OK):**
  ```json
  {
    "recommendedVoucherCodes": [
      "SHOPDISC25K",
      "NEXUS50K"
    ],
    "savingsBreakdown": {
      "productDiscount": 0,
      "shopDiscount": 25000,
      "systemDiscount": 50000,
      "totalSavings": 75000
    }
  }
  ```

#### `VOUCHER-04`: Tạo Mới Mã Khuyến Mãi (Seller / Admin)
* **Endpoint:** `POST /api/v1/discounts`
* **Request Body:**
  ```json
  {
    "code": "SHOPVIP10",
    "name": "Giam 10% don tu 200k",
    "scopeType": "Shop",
    "ownerSellerId": "shop1111-1111-1111-1111-000000000001",
    "discountType": "Percentage",
    "discountValue": 10,
    "minOrderAmount": 200000,
    "maxDiscountAmount": 50000,
    "maxUsesTotal": 1000,
    "maxUsesPerUser": 1,
    "isVisible": true,
    "startsAt": "2026-11-18T00:00:00Z",
    "endsAt": "2026-12-31T23:59:59Z",
    "scopes": [
      {
        "scopeRefType": "Shop",
        "scopeRefId": "shop1111-1111-1111-1111-000000000001"
      }
    ]
  }
  ```
* **Response (HTTP 201 Created):** Trả về `DiscountDetailResponse`.

---

# PHẦN 6: BOUNDED CONTEXT 5 — PHÂN HỆ QUẢN TRỊ PHÍ SÀN (`FEE-01` .. `FEE-02`)

> **Bộ điều khiển:** `AdminPlatformFeeController` (`api/v1/admin/platform-fee`)  
> **Quyền hạn:** Bắt buộc `staff` hoặc `admin`

### `FEE-01`: Tra Cứu Tỷ Lệ Phí Sàn Hiện Tại
* **Endpoint:** `GET /api/v1/admin/platform-fee`
* **Response (HTTP 200 OK):**
  ```json
  {
    "defaultPercent": 5.0,
    "updatedByRole": "admin",
    "updatedAt": "2026-11-18T00:00:00Z"
  }
  ```

### `FEE-02`: Cập Nhật Tỷ Lệ Phí Sàn Toàn Hệ Thống
* **Endpoint:** `PUT /api/v1/admin/platform-fee`
* **Request Body:**
  ```json
  {
    "defaultPercent": 6.5
  }
  ```
* **Response (HTTP 200 OK):**
  ```json
  {
    "defaultPercent": 6.5,
    "updatedByRole": "admin",
    "updatedAt": "2026-11-18T00:05:00Z"
  }
  ```

---

# PHẦN 7: BOUNDED CONTEXT 6 — PHÂN HỆ ĐƠN HÀNG ĐẤU GIÁ (`AUC-01` .. `AUC-09`)

> **Bộ điều khiển:** `AuctionOrdersController` (`api/v1/auction-orders`)  
> **Quyền hạn:** `buyer` (winner), `seller`, `admin`, `staff`, và `X-Internal-Service-Key` (cho service nội bộ)

### 7.1. Danh mục Endpoint Đơn Đấu Giá

| Mã | Method | Endpoint | Vai trò / Auth | Mô tả |
| - | :-: | - | - | - |
| `AUC-01` | `POST` | `/api/v1/auction-orders/settlement` | `X-Internal-Service-Key` | Tiếp nhận kết quả đấu giá tạo đơn `PendingPayment` (Hỗ trợ runner-up & cọc). |
| `AUC-02` | `PUT` | `/api/v1/auction-orders/{auctionOrderId}/shipping` | Buyer | Winner nhập địa chỉ giao hàng, tính cước GHN và chốt `TotalAmount`. |
| `AUC-03` | `POST` | `/api/v1/auction-orders/{auctionOrderId}/pay` | Buyer | Winner thanh toán qua ví Nexus, kích hoạt hạn Seller duyệt 24h. |
| `AUC-04` | `POST` | `/api/v1/auction-orders/{auctionOrderId}/accept` | Seller | Seller tiếp nhận đơn trong 24h, tạo vận đơn GHN. |
| `AUC-05` | `POST` | `/api/v1/auction-orders/{auctionOrderId}/reject` | Seller | Seller từ chối đơn, tự động hoàn trả số tiền thanh toán thực tế cho Buyer. |
| `AUC-06` | `POST` | `/api/v1/auction-orders/{auctionOrderId}/confirm-receipt` | Buyer | Buyer xác nhận đã nhận hàng đấu giá, giải ngân Escrow cho Seller. |
| `AUC-07` | `GET` | `/api/v1/auction-orders/{auctionOrderId}` | Buyer/Seller | Xem chi tiết đơn hàng đấu giá. |
| `AUC-08` | `GET` | `/api/v1/auction-orders/seller` | Seller | Danh sách đơn hàng đấu giá của Seller (phân trang DB). |
| `AUC-09` | `GET` | `/api/v1/internal/auction-orders/{orderId}/notification-snapshot` | `X-Internal-Service-Key` | Lấy snapshot đơn đấu giá phục vụ thông báo cho Auction Service. |
| `AUC-10` | `GET` | `/api/v1/auction-orders/me` | Buyer | Danh sách đơn hàng đấu giá của cá nhân người mua thắng đấu giá (phân trang DB và lọc status). |

---

### 7.2. Chi Tiết Một Số Endpoint Đơn Đấu Giá Quan Trọng

#### `AUC-01`: Tiếp Nhận Kết Quả Đấu Giá (Settlement Ingestion)
* **Endpoint:** `POST /api/v1/auction-orders/settlement`
* **Bảo mật:** `X-Internal-Service-Key`
* **Mô tả:** Tiếp nhận thông tin chốt phiên từ `AuctionService` để tạo đơn `AuctionOrder` ở trạng thái `PendingPayment`. Hỗ trợ 3 nguồn đơn:
  1. `source = "WINNER"`: Đơn thắng đấu giá bậc thang tiêu chuẩn (hoặc đơn đấu giá có Giá sàn được Người bán chấp nhận bán dưới giá sàn). Khấu trừ tiền cọc (`depositAmount`), tính `remainingAmount = winningAmount - depositAmount`.
  2. `source = "SECOND_CHANCE"`: Đơn trao cơ hội cho Runner-up khi Winner chính quá hạn 24h.
  3. `source = "BUY_NOW"`: Đơn Mua ngay với mức giá định sẵn. AuctionService đã hold 100% tiền hàng (`depositAmount == winningAmount`). Commerce tính `remainingAmount = 0 VND`. Đơn ở trạng thái `PendingPayment` chờ Buyer nhập địa chỉ nhận hàng để tính phí ship GHN. Khi Buyer trả phí ship (`TotalAmount = ShippingFeeAmount`), đơn chuyển sang `Paid`.
* **Quy chuẩn Giá sàn (Reserve Price):** Khi phiên không đạt giá sàn, AuctionService giữ phiên trong trạng thái `AwaitingSellerApproval` (12h) và **không** gửi sang Commerce. Nếu Seller đồng ý bán, AuctionService đẩy sang Commerce với `source = "WINNER"`.
* **Request Body (BUY_NOW):**
  ```json
  {
    "sessionId": "4f9c6d1a-8b2e-4e7a-9a1b-3c5d7e9f0a2b",
    "source": "BUY_NOW",
    "winnerId": "11111111-2222-3333-4444-555555555555",
    "sellerId": "99999999-8888-7777-6666-555555555555",
    "itemId": "aaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    "winningAmount": 5000000.0,
    "depositAmount": 5000000.0,
    "paymentDeadlineAt": "2026-11-21T16:00:00Z"
  }
  ```
* **Response (HTTP 201 Created):**
  ```json
  {
    "orderId": "7b8c9d0e-1f2a-3b4c-5d6e-7f8a9b0c1d2e",
    "orderNo": "AO-20261120-A1B2C3",
    "auctionId": "4f9c6d1a-8b2e-4e7a-9a1b-3c5d7e9f0a2b",
    "orderSource": "BuyNow",
    "status": "PendingPayment",
    "paymentDeadlineAt": "2026-11-21T16:00:00Z"
  }
  ```

#### `AUC-10`: Danh Sách Đơn Đấu Giá Của Người Mua Cá Nhân
* **Endpoint:** `GET /api/v1/auction-orders/me?page=1&pageSize=20&status=PendingPayment`
* **Vai trò:** `buyer`, `admin`, `staff`
* **Mô tả:** Lấy danh sách các đơn hàng đấu giá thuộc về người mua đang đăng nhập (`BuyerId == currentUserId`), tự động sắp xếp theo `CreatedAt DESC`, hỗ trợ lọc theo trạng thái (`PendingPayment`, `Paid`, `Confirmed`, `Shipping`, `Delivered`, `Completed`, `Cancelled`) và phân trang chuẩn `PagedData<T>`.
* **Response (HTTP 200 OK):**
  ```json
  {
    "items": [
      {
        "id": "a0000000-0000-0000-0000-000000000001",
        "orderNo": "AO-20261118-00001",
        "auctionId": "auc11111-1111-1111-1111-000000000001",
        "buyerId": "user1111-1111-1111-1111-000000000001",
        "sellerId": "shop1111-1111-1111-1111-000000000001",
        "productName": "Đồng hồ cơ cổ điển",
        "imageUrl": "https://example.com/watch.png",
        "winningAmount": 1500000.0,
        "totalAmount": 1330000.0,
        "status": "PendingPayment",
        "paymentDeadlineAt": "2026-11-19T12:00:00Z",
        "createdAt": "2026-11-18T12:00:00Z",
        "winningBidId": "bid11111-1111-1111-1111-000000000001",
        "orderSource": "Winner",
        "depositAmount": 200000.0,
        "shippingFeeAmount": 30000.0
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

# PHẦN 8: BOUNDED CONTEXT 7 — GIAO TIẾP NỘI BỘ & ĐÁNH GIÁ ĐƠN HÀNG (`INTERNAL-01`)

### `INTERNAL-01`: Tra Cứu Điều Kiện Đánh Giá Đối Tác (Review Eligibility)
* **Endpoint:** `GET /api/v1/internal/review-eligibility?referenceType={Order|Auction}&referenceId={UUID}`
* **Bảo mật:** `X-Internal-Service-Key` (UserService gọi để kiểm tra quyền đánh giá shop/buyer).
* **Quy chuẩn:**
  - `referenceType = "Order"`: `referenceId` bắt buộc là **ID của SubOrder** (không dùng Order cha).
  - `referenceType = "Auction"`: `referenceId` là ID của AuctionOrder.
* **Response (HTTP 200 OK):**
  ```json
  {
    "referenceType": "Order",
    "referenceId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "status": "Completed",
    "buyerId": "user1111-1111-1111-1111-000000000001",
    "sellerId": "shop1111-1111-1111-1111-000000000001",
    "completedAt": "2026-11-18T00:10:00Z"
  }
  ```
* **Lỗi (HTTP 404 Not Found):**
  ```json
  {
    "errorCode": "REVIEW_ORDER_NOT_FOUND",
    "message": "The order was not found."
  }
  ```

---

### `INTERNAL-02`: Xác Minh Đơn Hàng Hợp Lệ Cho Khiếu Nại/Tố Cáo (Dispute Eligibility)
* **Endpoint:** `GET /api/v1/internal/orders/dispute-eligibility?orderOrSubOrderId={UUID}&reporterId={UUID}&reportedUserId={UUID}`
* **Bảo mật:** `X-Internal-Service-Key` (UserService gọi để kiểm tra điều kiện mở Dispute/Report).
* **Quy chuẩn:**
  - Hỗ trợ tra cứu theo `SubOrder`, `Order` (gốc), hoặc `AuctionOrder`.
  - Kiểm tra quan hệ 2 chiều giữa `reporterId` và `reportedUserId` (Buyer-Seller hoặc Seller-Buyer).
  - Đơn hàng phải ở trạng thái `Delivered` hoặc `Completed` mới đủ điều kiện (`isEligible: true`).
* **Response (HTTP 200 OK):**
  ```json
  {
    "isEligible": true,
    "orderType": "SubOrder",
    "buyerId": "user1111-1111-1111-1111-000000000001",
    "sellerId": "shop1111-1111-1111-1111-000000000001",
    "status": "Delivered",
    "deliveredAt": "2026-11-19T00:10:00Z"
  }
  ```
* **Lỗi:**
  - `401 Unauthorized` (`INTERNAL_KEY_INVALID`): Thiếu hoặc sai header `X-Internal-Service-Key`.
  - `404 Not Found` (`ORDER_NOT_FOUND`): Không tìm thấy đơn hàng hoặc cặp người dùng không khớp đơn hàng.
  - `400 Bad Request` (`VAL_INVALID_INPUT`): Thiếu tham số bắt buộc.

---

### `INTERNAL-03`: Giải Ngân Hoàn Tiền Khiếu Nại (Dispute Resolution Refund)
* **Endpoint:** `POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund`
* **Bảo mật:** `X-Internal-Service-Key` (UserService gọi khi Admin phân xử khiếu nại thành công cho Người mua).
* **Quy chuẩn:**
  - `SubOrder` phải ở trạng thái `Delivered` hoặc `Completed`. Nếu không thỏa mãn, trả về `400 Bad Request` (`INVALID_REFUND_STATUS`).
  - Thực hiện gọi `IWalletClient.RefundSubOrderPaymentAsync` để hoàn tiền trực tiếp vào ví Buyer qua Wallet Service với idempotency key `dispute-refund-{disputeId}`.
  - Chuyển trạng thái `SubOrder` sang `Refunded`, cập nhật `CompletedAt`.
  - Ghi nhận `OrderStatusHistory` với `ToStatus = Refunded`, `ActorRole = System`, `Reason = $"Dispute refund: {reason}"`.
  - Hoàn tất hoặc tạo mới bản ghi `OrderRefund` với `Status = Completed`, `ReasonCode = "DISPUTE_RESOLUTION"`.
* **Request Body:**
  ```json
  {
    "disputeId": "a1000000-0000-0000-0000-000000000001",
    "refundAmount": 850000.0,
    "reason": "Damaged goods in transit confirmed by support",
    "note": "Resolved in favor of buyer under Dispute #DISP-20261121-001"
  }
  ```
* **Response (HTTP 200 OK):**
  ```json
  {
    "subOrderId": "02000000-0000-0000-0000-000000000001",
    "refundId": "r1000000-0000-0000-0000-000000000001",
    "amount": 850000.0,
    "status": "Completed",
    "completedAt": "2026-11-21T01:15:00Z"
  }
  ```
* **Lỗi:**
  - `401 Unauthorized` (`INTERNAL_KEY_INVALID`): Thiếu hoặc sai header `X-Internal-Service-Key`.
  - `404 Not Found` (`SUB_ORDER_NOT_FOUND`): Không tìm thấy đơn hàng con.
  - `400 Bad Request` (`INVALID_REFUND_STATUS`): Trạng thái đơn không hợp lệ để hoàn tiền khiếu nại.
  - `400 Bad Request` (`WALLET_REFUND_FAILED`): Lỗi xử lý hoàn tiền từ phía Wallet Service.
  - `503 Service Unavailable` (`INTERNAL_CONFIGURATION_ERROR`): Khóa bảo mật nội bộ chưa được cấu hình.

---

# PHẦN 9: WEBHOOK TIẾP NHẬN TRẠNG THÁI GIAO VẬN

### Endpoint: `POST /api/v1/sub-orders/{subOrderId}/delivery-status`
* **Bảo mật:** `X-Internal-Service-Key` (Được gọi từ Fulfillment Service / GHN Gateway).
* **Request Body:**
  ```json
  {
    "status": "Delivered",
    "deliveredAt": "2026-11-18T14:30:00Z",
    "carrierCode": "GHN",
    "trackingCode": "GHN-SGN-1029384",
    "note": "Delivered successfully with signature",
    "location": "Tan Binh Hub, Ho Chi Minh City",
    "podUrl": "https://cdn.nexus.vn/pod/20261118/suborder-pod-01.jpg",
    "shipperName": "Nguyen Van Bưu Tá",
    "shipperPhone": "0912345678"
  }
  ```
* **Xử lý Chuyển trạng thái:**
  1. Ghi nhận `DeliveredAt`, cập nhật trạng thái `SubOrderStatus.Delivered`.
  2. Ghi nhận `OrderStatusHistory` lưu vết đầy đủ tên bưu tá, SĐT và URL ảnh chụp bằng chứng giao hàng (`podUrl`).
  3. Tiền thanh toán **tiếp tục nằm trong Escrow** để bảo vệ quyền khiếu nại của khách (không tự ý giải ngân).
  4. Nếu 100% đơn con trong đơn cha đã `Delivered`/`Completed`, tự động cập nhật `Order.Status = OrderStatus.Delivered`.
* **Response (HTTP 200 OK):**
  ```json
  {
    "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
    "status": "Delivered",
    "updatedAt": "2026-11-18T14:30:00Z"
  }
  ```

---

# PHẦN 10: HỢP ĐỒNG OUTBOUND TYPED REST CLIENTS (GỌI LIÊN DỊCH VỤ)

Commerce Service giao tiếp với các dịch vụ vệ tinh qua các Typed HTTP Clients cấu hình kèm Polly Resilience:

### 10.1. Catalog Service (`:8002` REST) — `ICatalogClient`
1. `GET /api/v1/internal/variants/{skuId}`: Tra cứu thông tin giá và tính khả dụng của SKU.
2. `POST /api/v1/internal/variants`: Batch tra cứu danh sách SKU giỏ hàng.
3. `POST /api/v1/internal/catalog/skus/validate-variant-switch`: Kiểm tra hợp lệ khi đổi biến thể SKU.
4. `POST /api/v1/catalog/purchases/sync`: Đồng bộ danh sách sản phẩm đã hoàn tất mua hàng để mở quyền đánh giá.

### 10.2. Fulfillment Service (`:8006` REST) — `IFulfillmentClient`
1. `GET /api/v1/inventories/{skuId}`: Kiểm tra số lượng tồn kho.
2. `POST /api/v1/inventories/by-skus`: Batch kiểm tra tồn kho giỏ hàng.
3. `POST /api/v1/internal/inventories/reservations`: Giữ hàng tạm thời 5 phút trước khi thanh toán.
4. `POST /api/v1/internal/inventories/reservations/commit`: Khấu trừ vĩnh viễn tồn kho khi đơn đã thanh toán.
5. `POST /api/v1/internal/inventories/reservations/release`: Giải phóng hàng dự lưu khi hủy đơn hoặc thanh toán thất bại.
6. `POST /api/v1/internal/inventories/reservations/batch-release`: Batch nhả tồn kho cho tiến trình quét quá hạn.
7. `POST /api/v1/shipments/quote`: Tính cước phí vận chuyển GHN theo địa chỉ bưu cục.
8. `POST /api/v1/shipments`: Tạo đơn vận chuyển sang GHN khi Seller tiếp nhận đơn.
9. `POST /api/v1/shipments/{subOrderId}/cancel`: Hủy vận đơn GHN khi đơn con bị hủy trước xuất kho.

### 10.3. Wallet Service (`:8008` REST) — `IWalletClient`
1. `GET /api/v1/wallets/me/balance`: Kiểm tra số dư ví khả dụng của người mua.
2. `POST /api/v1/wallets/pay`: Khấu trừ tiền ví để thanh toán đơn hàng (Header `Idempotency-Key`).
3. `POST /api/v1/wallets/refund`: Hoàn tiền ví khi đơn hàng bị hủy hoặc bồi hoàn (Idempotent).
4. `POST /api/v1/wallets/escrow/settle`: Giải ngân tiền từ quỹ ký quỹ sang ví khả dụng của Seller khi đơn hoàn tất.

### 10.4. User Service (`:8001` REST) — `IUserClient`
1. `POST /api/v1/users/reputation/adjust`: Cộng thưởng uy tín (+1) cho Seller khi hoàn tất đơn, hoặc phạt vi phạm.
2. `GET /api/v1/users/{userId}/permissions`: Kiểm tra quyền phát hành voucher của Shop.

---

# PHẦN 11: BẢNG TRA CỨU MÃ LỖI HỆ THỐNG CHUẨN RFC 9110 TOÀN DIỆN

Toàn bộ mã lỗi trả về tuân thủ nghiêm ngặt chuẩn **`SCREAMING_SNAKE_CASE`** và thông báo lỗi **Sentence case tiếng Anh**:

| Mã Lỗi (Error Code) | HTTP Status | Thông Báo Lỗi Chuẩn (`detail`) | Nguyên Nhân Kích Hoạt |
| :--- | :---: | :--- | :--- |
| `VAL_INVALID_INPUT` | 400 | The input payload contains validation errors. | Dữ liệu đầu vào không hợp lệ (số âm, chuỗi rỗng). |
| `UNAUTHORIZED` | 401 | Authentication credentials are missing or invalid. | Thiếu hoặc sai JWT token hoặc header `x-user-id`. |
| `FORBIDDEN` | 403 | You do not have permission to access this resource. | Truy cập đơn hàng hoặc giỏ hàng không thuộc sở hữu. |
| `INTERNAL_KEY_INVALID` | 403 | Forbidden. Invalid or missing internal service key. | Gọi Webhook hoặc API nội bộ thiếu secret key. |
| `CART_NOT_FOUND` | 404 | Active shopping cart not found. | Không tìm thấy giỏ hàng của người dùng. |
| `CART_ITEM_NOT_FOUND` | 404 | Cart item not found. | Món hàng không tồn tại trong giỏ của người mua. |
| `CART_LIMIT_EXCEEDED` | 400 | Cart cannot exceed maximum limit of 50 different items. | Giỏ hàng vượt quá 50 loại sản phẩm. |
| `CART_ITEM_LIMIT_EXCEEDED`| 400 | Total quantity for a single product cannot exceed 99. | Số lượng của 1 SKU vượt quá 99 cái. |
| `CART_CONCURRENCY_CONFLICT`| 409 | Cart was modified by another request. Please refresh. | Xung đột cập nhật giỏ hàng đồng thời (RowVersion). |
| `INSUFFICIENT_STOCK` | 400 | One or more items in the order have insufficient stock. | Tồn kho tại Fulfillment Service không đủ đáp ứng. |
| `INSUFFICIENT_WALLET_BALANCE`| 400 | Buyer wallet balance is insufficient to complete the payment. | Số dư ví của người mua không đủ thanh toán. |
| `UNSUPPORTED_PAYMENT_METHOD`| 400 | Payment method is not supported. Use Wallet or Cod. | Cố tình gửi `PaymentMethod.VnPay`. |
| `ORDER_NOT_FOUND` | 404 | Order was not found. | Không tìm thấy mã đơn hàng cha. |
| `SUB_ORDER_NOT_FOUND` | 404 | Sub-order was not found. | Không tìm thấy mã đơn hàng con của Shop. |
| `ORDER_PAYMENT_DEADLINE_EXPIRED` | 400 | Payment deadline for this order has expired. | Quá hạn 5 phút thanh toán đơn hàng. |
| `INVALID_STATUS` | 400 | Order is not in a valid status for this action. | Thao tác sai trạng thái (Ví dụ xác nhận nhận hàng khi chưa giao). |
| `SUB_ORDER_ALREADY_SHIPPED` | 400 | Dispatched packages cannot be cancelled directly. | Khách cố tình bấm hủy khi đơn con đã `Shipped`. |
| `DISCOUNT_NOT_FOUND` | 404 | Promotion discount code was not found. | Mã voucher không tồn tại trên hệ thống. |
| `DISCOUNT_USAGE_LIMIT_EXCEEDED` | 400 | Promotion discount usage limit has been exceeded. | Voucher đã hết lượt sử dụng (toàn sàn hoặc per-user). |
| `DISCOUNT_MIN_SPEND_NOT_MET` | 400 | Order total does not meet the minimum spend requirement. | Đơn hàng chưa đạt giá trị tối thiểu của Voucher. |
| `DISCOUNT_EXPIRED` | 400 | Promotion discount has expired. | Voucher đã quá hạn sử dụng (`EndsAt`). |
| `AUCTION_ORDER_NOT_FOUND` | 404 | Auction order was not found. | Không tìm thấy đơn hàng đấu giá. |
| `REVIEW_ORDER_NOT_FOUND` | 404 | The order was not found. | Kiểm tra điều kiện đánh giá không tìm thấy đơn. |

---

# PHẦN 12: BỘ TYPESCRIPT TYPE DEFINITIONS HOÀN CHỈNH (FRONTEND & SDK DTOS)

Dành cho các đội ngũ phát triển Frontend Web (React/Next.js), Mobile App (React Native/Flutter), và SDK tích hợp:

```typescript
// =============================================================================
// ENUMS & TYPES
// =============================================================================

export type OrderStatus =
  | "PendingPayment"
  | "PaymentProcessing"
  | "Paid"
  | "Cancelled"
  | "Expired"
  | "Completed"
  | "Delivered";

export type SubOrderStatus =
  | "PendingPayment"
  | "Paid"
  | "Confirmed"
  | "Shipped"
  | "Delivered"
  | "Completed"
  | "Cancelled"
  | "Refunded";

export type PaymentMethod = "Wallet" | "Cod";

export type CancellationReasonCode =
  | "BuyerChangedMind"
  | "OutOfStock"
  | "PaymentTimeout"
  | "SellerRejected"
  | "Other"
  | "DeliveryFailed"
  | "PackageReturned"
  | "CancellationWithoutReason";

export type DiscountScopeType = "System" | "Shop" | "Product";
export type DiscountType = "Percentage" | "FixedAmount";
export type DiscountStatus = "Draft" | "Active" | "Inactive" | "Expired";
export type DiscountScopeRefType = "Shop" | "Product" | "Sku" | "Category" | "All";

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

// =============================================================================
// CART DTOS
// =============================================================================

export interface CartItemDto {
  id: string;
  cartId: string;
  productId: string;
  skuId: string;
  sellerId: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
  isAvailable: boolean;
  currentStock: number;
  hasPriceChanged: boolean;
  originalPrice: number;
  createdAt: string;
  updatedAt: string;
}

export interface CartDto {
  id: string;
  buyerId: string;
  status: string;
  itemCount: number;
  subtotalAmount: number;
  rowVersion: number;
  lastActivityAt: string;
  items: CartItemDto[];
}

export interface CartSummaryResponse {
  cartId: string;
  itemCount: number;
  subtotalAmount: number;
}

export interface GroupedCartShopDto {
  sellerId: string;
  shopName: string;
  itemCount: number;
  shopSubtotal: number;
  items: CartItemDto[];
}

export interface GroupedCartResponse {
  cartId: string;
  totalItemCount: number;
  totalUniqueItems: number;
  subtotalAmount: number;
  shops: GroupedCartShopDto[];
}

export interface AddCartItemRequest {
  productId: string;
  skuId: string;
  sellerId: string;
  quantity: number;
}

export interface UpdateCartItemQuantityRequest {
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

export interface CalculateSelectedCartResponse {
  selectedItemCount: number;
  selectedSubtotalAmount: number;
  hasInvalidItems: boolean;
  invalidItemIds: string[];
}

export interface ValidateCartRequest {
  cartItemIds?: string[] | null;
}

export interface ValidateCartItemResult {
  cartItemId: string;
  skuId: string;
  isValid: boolean;
  isAvailable: boolean;
  availableStock: number;
  hasPriceChanged: boolean;
  currentPrice: number;
  originalPrice: number;
  reason?: string | null;
}

export interface ValidateCartResponse {
  isValid: boolean;
  hasPriceChanges: boolean;
  hasUnavailableItems: boolean;
  items: ValidateCartItemResult[];
}

export interface GuestCartItemInput {
  productId: string;
  skuId: string;
  sellerId: string;
  quantity: number;
}

export interface MergeGuestCartRequest {
  guestItems: GuestCartItemInput[];
}

export interface CleanupCheckedOutItemsRequest {
  purchasedCartItemIds: string[];
}

// =============================================================================
// CHECKOUT & ORDER DTOS
// =============================================================================

export interface ShippingAddressInput {
  receiverName: string;
  contactPhone: string;
  street: string;
  ward: string;
  district?: string | null;
  province?: string;
  provinceId?: number | null;   // required: the GHN province id
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

export interface CreateOrderRequest {
  quoteId: string;
  paymentMethod: PaymentMethod;
  shippingAddress: string;
  contactPhone: string;
  note?: string | null;
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
  status: string;
  paymentDeadlineAt: string;
  paymentUrl?: string | null;
  subOrderIds: string[];
  createdAt: string;
  appliedPriceDrops?: PriceDropDto[] | null;
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

export interface ShippingAddressSnapshot {
  receiverName: string;
  contactPhone: string;
  street: string;
  ward: string;
  district: string;
  province: string;
  provinceId?: number | null;   // null for an order placed before the province id was kept
  ghnDistrictId: number;
  ghnWardCode: string;
  fullAddressText: string;
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

export interface OrderCompleteResponse {
  orderId: string;
  orderNo: string;
  status: string;
  completedAt: string;
  completedSubOrderCount: number;
  completedSubOrderIds: string[];
}

export interface CancelSubOrderRequest {
  reasonCode?: CancellationReasonCode;
  reasonNote?: string | null;
}

export interface CancelSubOrderResponse {
  subOrderId: string;
  status: string;
  cancelledAt: string;
  refundAmount: number;
  inventoryReleased: boolean;
  carrierShipmentCancelled: boolean;
}

export interface RepayOrderResponse {
  orderId: string;
  status: string;
  totalAmount: number;
  paidAt: string;
  walletTransactionId?: string | null;
}

export interface OrderRefundResponse {
  id: string;
  orderId?: string | null;
  subOrderId?: string | null;
  amount: number;
  refundType: string;
  status: string;
  reasonCode: string;
  reasonNote?: string | null;
  requestedAt: string;
  completedAt?: string | null;
}

export interface SubOrderFinancialBreakdownResponse {
  subOrderId: string;
  subOrderNo: string;
  sellerId: string;
  buyerId: string;
  totalAmount: number;
  itemsAmount: number;
  productDiscountAmount: number;
  shopDiscountAmount: number;
  systemDiscountAmount: number;
  shippingFeeAmount: number;
  platformFeeAmount: number;
  sellerReceiveAmount: number;
  isEscrowSettled: boolean;
  status: string;
  paymentMethod: string;
  completedAt?: string | null;
}

export interface TimelineEventDto {
  status: string;
  title: string;
  description: string;
  occurredAt: string;
  location?: string | null;
}

export interface ShipmentTimelineResponse {
  subOrderId: string;
  subOrderNo: string;
  orderId: string;
  carrierCode?: string | null;
  carrierName?: string | null;
  trackingCode?: string | null;
  labelPrintUrl?: string | null;
  currentStatus: string;
  estimatedDeliveryAt?: string | null;
  shippingAddress: ShippingAddressSnapshot;
  timeline: TimelineEventDto[];
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
  labelPrintUrl?: string | null;
}

export interface ShipmentTrackingResponse {
  orderId: string;
  orderNo: string;
  shipments: SubOrderShipmentInfo[];
}

export interface RetryShipmentResponse {
  subOrderId: string;
  subOrderNo: string;
  trackingCode: string;
  carrierCode: string;
  labelPrintUrl: string;
  createdAt: string;
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
  carrierCode?: string | null;
  trackingCode?: string | null;
  note?: string | null;
  location?: string | null;
  podUrl?: string | null;
  shipperName?: string | null;
  shipperPhone?: string | null;
}

// =============================================================================
// VOUCHER & PROMOTION DTOS
// =============================================================================

export interface BuyerAvailableVoucherDto {
  id: string;
  code: string;
  name: string;
  scopeType: DiscountScopeType;
  ownerSellerId?: string | null;
  discountType: DiscountType;
  discountValue: number;
  minOrderAmount: number;
  maxDiscountAmount?: number | null;
  startsAt?: string | null;
  endsAt?: string | null;
  isEligible: boolean;
  ineligibleReason?: string | null;
  estimatedDiscountAmount: number;
}

export interface ValidateVoucherRequest {
  code: string;
  cartItemIds: string[];
}

export interface ValidateVoucherResponse {
  isValid: boolean;
  discountId: string;
  code: string;
  scopeType: DiscountScopeType;
  estimatedDiscountAmount: number;
}

export interface RecommendVouchersRequest {
  cartItemIds: string[];
}

export interface TierSavingsDto {
  productDiscount: number;
  shopDiscount: number;
  systemDiscount: number;
  totalSavings: number;
}

export interface RecommendVouchersResponse {
  recommendedVoucherCodes: string[];
  savingsBreakdown: TierSavingsDto;
}

export interface CreateDiscountScopeInput {
  scopeRefType: DiscountScopeRefType;
  scopeRefId?: string | null;
}

export interface CreateDiscountRequest {
  code: string;
  name: string;
  scopeType: DiscountScopeType;
  ownerSellerId?: string | null;
  discountType: DiscountType;
  discountValue: number;
  minOrderAmount?: number;
  maxDiscountAmount?: number | null;
  maxUsesTotal?: number | null;
  maxUsesPerUser?: number;
  isVisible?: boolean;
  startsAt?: string | null;
  endsAt?: string | null;
  scopes?: CreateDiscountScopeInput[] | null;
}

export interface UpdateDiscountRequest {
  name: string;
  isVisible: boolean;
  maxUsesPerUser: number;
  maxUsesTotal?: number | null;
  endsAt?: string | null;
}

export interface UpdateDiscountStatusRequest {
  status: DiscountStatus;
}

export interface DiscountScopeDto {
  id: string;
  scopeRefType: DiscountScopeRefType;
  scopeRefId?: string | null;
}

export interface DiscountDetailResponse {
  id: string;
  code: string;
  name: string;
  scopeType: DiscountScopeType;
  ownerSellerId?: string | null;
  discountType: DiscountType;
  discountValue: number;
  minOrderAmount: number;
  maxDiscountAmount?: number | null;
  maxUsesTotal?: number | null;
  maxUsesPerUser: number;
  usedCount: number;
  reservedCount: number;
  isVisible: boolean;
  status: DiscountStatus;
  startsAt?: string | null;
  endsAt?: string | null;
  createdAt: string;
  scopes: DiscountScopeDto[];
}

export interface DiscountSummaryDto {
  id: string;
  code: string;
  name: string;
  scopeType: DiscountScopeType;
  ownerSellerId?: string | null;
  discountType: DiscountType;
  discountValue: number;
  minOrderAmount: number;
  maxDiscountAmount?: number | null;
  maxUsesTotal?: number | null;
  usedCount: number;
  reservedCount: number;
  isVisible: boolean;
  status: DiscountStatus;
  startsAt?: string | null;
  endsAt?: string | null;
}

// =============================================================================
// PLATFORM FEE DTOS
// =============================================================================

export interface PlatformFeeResponse {
  defaultPercent: number;
  updatedByRole: string;
  updatedAt: string;
}

export interface UpdatePlatformFeeRequest {
  defaultPercent: number;
}

// =============================================================================
// AUCTION ORDER & INTERNAL DTOS
// =============================================================================

export interface AuctionSettlementRequest {
  auctionId: string;
  winnerId: string;
  sellerId: string;
  winningBidId?: string | null;
  productId: string;
  skuId?: string | null;
  productName: string;
  productImageUrl?: string | null;
  skuAttributes?: string | null;
  winningAmount: number;
  paymentDeadlineAt: string;
  depositAmount?: number | null;
  source?: "WINNER" | "SECOND_CHANCE" | "BUY_NOW" | null;
  shippingAddress?: ShippingAddressInput | null;
  contactPhone?: string | null;
}

export interface DisputeRefundRequest {
  disputeId: string;
  refundAmount: number;
  reason: string;
  note?: string | null;
}

export interface DisputeRefundResponse {
  subOrderId: string;
  refundId: string;
  amount: number;
  status: string;
  completedAt: string;
}

export interface AuctionSettlementResponse {
  orderId: string;
  orderNo: string;
  auctionId: string;
  winnerId: string;
  status: string;
  paymentDeadlineAt: string;
}

export interface SubmitAuctionShippingRequest {
  shippingAddress: ShippingAddressInput;
  contactPhone: string;
  contactName?: string | null;
}

export interface PayAuctionOrderResponse {
  orderId: string;
  orderNo: string;
  status: string;
  paidAt: string;
  sellerRespondDeadlineAt: string;
  walletTransactionId: string;
}

export interface SellerRespondResponse {
  orderId: string;
  orderNo: string;
  status: string;
  action: "Accepted" | "Rejected";
  respondedAt: string;
  trackingCode?: string | null;
  refundAmount?: number | null;
}

export interface AuctionOrderDetailResponse {
  id: string;
  orderNo: string;
  auctionId: string;
  buyerId: string;
  sellerId: string;
  winningBidId: string;
  productId: string;
  skuId?: string | null;
  productName: string;
  productImageUrl?: string | null;
  skuAttributes?: string | null;
  winningAmount: number;
  depositAmount: number;
  remainingAmount: number;
  shippingFeeAmount?: number | null;
  totalAmount?: number | null;
  sellerReceiveAmount: number;
  platformFeeAmount: number;
  orderSource: "Winner" | "SecondChance";
  status: string;
  paymentMethod: string;
  paymentDeadlineAt: string;
  paidAt?: string | null;
  sellerRespondDeadlineAt?: string | null;
  sellerAcceptedAt?: string | null;
  shippingAddress?: string | null;
  trackingCode?: string | null;
  shippingCarrier?: string | null;
  shippingLabelUrl?: string | null;
  shippedAt?: string | null;
  deliveredAt?: string | null;
  completedAt?: string | null;
  cancelledAt?: string | null;
  createdAt: string;
}

export interface AuctionOrderNotificationSnapshot {
  orderId: string;
  orderNo: string;
  totalAmount: number;
  paidAt: string;
  sellerRespondDeadlineAt: string;
  buyerId: string;
  sellerId: string;
  status: string;
}

export interface ReviewEligibilityDto {
  referenceType: "Order" | "Auction";
  referenceId: string;
  status: string;
  buyerId: string;
  sellerId: string;
  completedAt?: string | null;
}
```

---

## Phần 9: Bounded Context 8 — Post-Order Lifecycle, Address Modification & Refund Governance (SPEC-032)

### 9.1 Đổi địa chỉ nhận hàng sau khi đặt hàng
- `PATCH /api/v1/sub-orders/{subOrderId}/shipping-address`
  - **Mô tả:** Đổi địa chỉ nhận hàng cho đơn hàng con khi đơn chưa xuất kho (`PendingPayment`, `Paid`, `Confirmed` và chưa có `TrackingCode`).
  - **Bảo vệ cước phí:** Nếu đơn đã thanh toán (`Paid`), chỉ cho phép đổi khi cước mới $\le$ cước cũ. Nếu cước tăng, từ chối với HTTP 400 `SHIPPING_FEE_INCREASED`.
- `PATCH /api/v1/orders/{orderId}/shipping-address`
  - **Mô tả:** Đổi địa chỉ nhận hàng cho toàn bộ đơn hàng cha (tất cả đơn con phải thỏa mãn điều kiện).
- `PATCH /api/v1/auction-orders/{auctionOrderId}/shipping-address`
  - **Mô tả:** Đổi địa chỉ nhận hàng cho đơn đấu giá trước khi xuất kho.

### 9.2 Quản trị Hoàn tiền (`RefundsController`)
- `GET /api/v1/refunds`
  - **Mô tả:** Tra cứu danh sách yêu cầu hoàn tiền phân trang database và lọc theo `status`, `subOrderId`. Role Buyer xem đơn của mình, Seller xem đơn shop mình, Admin/Staff xem toàn bộ.
- `GET /api/v1/refunds/{id}`
  - **Mô tả:** Xem chi tiết yêu cầu hoàn tiền.
- `POST /api/v1/refunds/{id}/approve`
  - **Mô tả:** Seller hoặc Admin duyệt hoàn tiền. Hoàn tiền vào ví Buyer qua `WalletClient`, chuyển `SubOrder` sang `Refunded`.
- `POST /api/v1/refunds/{id}/reject`
  - **Mô tả:** Seller hoặc Admin từ chối hoàn tiền, bắt buộc nhập `reason`.

### 9.3 Mở rộng vòng đời Đơn hàng Đấu giá
- `GET /api/v1/auction-orders/{id}/shipments/timeline`
  - **Mô tả:** Xem dòng thời gian vận chuyển đơn hàng đấu giá.
- `POST /api/v1/auction-orders/{id}/cancel`
  - **Mô tả:** Hủy đơn đấu giá trước khi người bán bấm chấp nhận. Nếu đơn đã `Paid`, tự động hoàn tiền vào ví Buyer và gửi callback sang `AuctionService`.
- `POST /api/v1/auction-orders/{id}/shipments/retry`
  - **Mô tả:** Thử lại tạo vận đơn GHN khi đơn đã `Confirmed` nhưng chưa có mã vận đơn do nghẽn mạng.

### 9.4 Quản trị Đơn hàng Tập trung cho Admin & Staff
- `GET /api/v1/admin/orders`
  - **Mô tả:** Tra cứu danh sách đơn cha toàn sàn (`[RequireRole("admin", "staff")]`), hỗ trợ lọc theo `buyerId`, `sellerId`, `status`, `fromDate`, `toDate`, phân trang database.
- `GET /api/v1/admin/sub-orders`
  - **Mô tả:** Tra cứu danh sách đơn con toàn sàn (`[RequireRole("admin", "staff")]`), hỗ trợ lọc theo `sellerId`, `buyerId`, `status`, `trackingCode`, `fromDate`, `toDate`, phân trang database.


---

## Bổ sung: kho của seller khi báo phí, đặt đơn và tạo vận đơn (SP-05)

Mỗi shop giao từ kho riêng của mình. Khi kho thiếu, `POST /api/v1/checkouts/quote` (và báo giá lại) trả lỗi 422 chuyển tiếp từ Fulfillment, **không trừ tiền**:

| Mã lỗi | HTTP | Ý nghĩa |
|---|---|---|
| `SELLER_WAREHOUSE_MISSING` | 422 | Seller của một sản phẩm trong giỏ chưa có kho đang hoạt động |
| `SELLER_WAREHOUSE_INCOMPLETE` | 422 | Kho có nhưng thiếu quận, phường hoặc mã tỉnh đã được xác nhận với GHN (kho cũ chưa có mã tỉnh phải được lưu lại) |

`POST /api/v1/sub-orders/{subOrderId}/shipments/retry` (seller) cũng trả hai mã trên khi kho chưa dùng được, ngoài `SHIPMENT_ALREADY_EXISTS` (400), `INVALID_STATUS` (400) và `FULFILLMENT_SERVICE_UNAVAILABLE` (503).

Với bước đặt đơn, câu trả lời bị mất (mạng, 5xx không có mã) là **kết quả chưa rõ**: báo giá đã bị dùng và có thể đã có đơn hoặc khoản thu; client phải cho người mua kiểm tra đơn hàng và ví trước khi đặt lại. Với bước báo giá, mọi lỗi đều an toàn để thử lại.

## Bổ sung: mã vận đơn trong chi tiết đơn con (B1)

`GET /api/v1/sub-orders/{subOrderId}` và các đơn con trong `GET /api/v1/orders/{id}` có thêm `carrierCode`, `trackingCode` và `shipmentPendingCreation`. Đơn đã chấp nhận mà `trackingCode` rỗng nghĩa là vận đơn chưa tạo được: seller dùng nút tạo lại vận đơn.

## Dia chi giao hang duoc xac minh voi GHN (24/11/2026)
Moi dia chi dung de bao gia, dat don, doi dia chi nhan va don dau gia (nhap tay, tu so dia chi, dia chi mac dinh) phai co du `provinceId`, `ghnDistrictId`, `ghnWardCode` va duoc danh sach GHN xac nhan la thuoc cung mot chuoi (Commerce hoi Fulfillment `internal/shipping/locations/validate`). Ket qua tinh phi cua GHN khong duoc coi la bang chung: GHN tinh theo phuong va bo qua quan khong khop.
- Thieu ma tinh, quan hoac phuong, hoac bo ma khong khop: `400 INVALID_SHIPPING_ADDRESS`.
- Khong co nguon GHN dang tin cay: `503 LOCATION_SOURCE_UNAVAILABLE`, khong bao gia va khong doi gi.
- Viec xac minh lam luc tao bao gia; luc dat don khong goi GHN lai (bao gia da chua dia chi da xac minh), nen mot loi 503 khong the lam mat bao gia khi chua co don.
- Dia chi tu so dia chi cua UserService duoc doc theo ten truong thuc te (`addressId`, `addressLine`, `wardName`, `districtName`, `provinceName`, `provinceId`, `districtId`, `wardCode`); truoc day chi doc ten tien to `ghn` nen mot dia chi lay tu so dia chi bi tu choi.
