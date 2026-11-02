# Feature Specification: Fulfillment Service Integration (Tồn kho, Giữ hàng, Giao vận & Làm xám Giỏ hàng)

**Feature Directory**: `specs/003-fulfillment-integration`  
**Feature Branch**: `feat/cart-service-v2`  
**Created**: 2026-11-01  
**Status**: Ready for Implementation  
**Input**: Tích hợp ranh giới Fulfillment Service (Inventory, Reservation 15m TTL, Release, Commit, GHN Shipping) vào Commerce Service, hiện thực hóa tính năng làm xám sản phẩm hết hàng trong giỏ như Shopee, và tự động hóa hủy đơn quá hạn thanh toán.

---

## 1. User Scenarios & Testing *(mandatory)*

### User Story 1 - Kiểm Tra Tồn Kho Khi Thêm Món Vào Giỏ (Priority: P1 - CART-04)
Là một người mua (Buyer), khi tôi thêm một sản phẩm vào giỏ hàng với số lượng $N$, hệ thống phải kiểm tra số lượng tồn kho khả dụng bên Fulfillment Service. Nếu không đủ hàng, hệ thống phải thông báo ngay lập tức để tôi không thêm số lượng vượt quá khả năng cung cấp.

* **Independent Test**: Gửi `POST /api/v1/carts/me/items` với `{ skuId, quantity }`. Hệ thống gọi `IFulfillmentClient.CheckStockAvailabilityAsync`. Nếu tồn kho < quantity, trả về `400 Bad Request` kèm mã lỗi `INSUFFICIENT_STOCK`.
* **Acceptance Scenarios**:
  1. **Given** SKU còn 5 sản phẩm khả dụng trong kho, **When** người mua thêm 2 sản phẩm, **Then** hệ thống cho phép thêm vào giỏ thành công và trả về `200 OK`.
  2. **Given** SKU chỉ còn 1 sản phẩm khả dụng, **When** người mua thêm 2 sản phẩm, **Then** hệ thống từ chối với mã lỗi `INSUFFICIENT_STOCK`, thông báo `"Insufficient stock available in warehouse."`.

---

### User Story 2 - Cơ Chế Làm Xám Sản Phẩm Hết Hàng Trong Giỏ Hàng (Priority: P1 - CART-01, CART-03)
Là một người mua đã để sản phẩm trong giỏ từ trước, khi tôi mở lại giỏ hàng, nếu sản phẩm đó đã bị hết hàng trong kho (Fulfillment) hoặc bị gỡ bán (Catalog), hệ thống phải đánh dấu làm xám sản phẩm đó và vô hiệu hóa nút tick chọn, giống hệt trải nghiệm người dùng trên Shopee.

* **Independent Test**: Gửi `GET /api/v1/carts/me`. Dữ liệu trả về ở từng dòng item phải có cờ `isAvailable` (boolean) và `disabledReason` (string).
* **Acceptance Scenarios**:
  1. **Given** Sản phẩm trong giỏ đã hết hàng (`availableStock == 0`), **When** người mua xem giỏ hàng, **Then** item trả về `isAvailable: false`, `disabledReason: "OUT_OF_STOCK"`.
  2. **Given** Sản phẩm bị Shop gỡ bán hoặc ẩn trên Catalog, **When** người mua xem giỏ hàng, **Then** item trả về `isAvailable: false`, `disabledReason: "PRODUCT_INACTIVE"`.
  3. **Given** Sản phẩm vẫn còn hàng và đang bán bình thường, **When** xem giỏ, **Then** item trả về `isAvailable: true`, `disabledReason: null`.

---

### User Story 3 - Tính Cước Phí Ship GHN Động Cho Từng Shop (Priority: P1 - CHK-01 / R75)
Là một người mua xem trước báo giá (Checkout Quote), tôi muốn cước phí vận chuyển của từng Shop được tính toán chính xác dựa trên địa chỉ nhận của tôi qua đơn vị vận chuyển GHN (do Fulfillment phụ trách).

* **Independent Test**: Gửi `POST /api/v1/checkouts/quote`. Hệ thống gọi `IFulfillmentClient.CalculateShopShippingFeeAsync` cho từng Shop và tổng hợp vào `shippingFeeAmount`.

---

### User Story 4 - Giữ Hàng Ảo Khi Tạo Đơn Hàng Với Hạn Định 15 Phút (Priority: P1 - CHK-02 / R76)
Là một người mua bấm "Đặt hàng", hệ thống phải khóa tạm thời (Reserve Stock) số lượng sản phẩm trong kho của Fulfillment Service trong vòng 15 phút. Trong thời gian này, không ai có thể cướp mất số hàng của tôi, đảm bảo tôi thanh toán là chắc chắn nhận được hàng.

* **Independent Test**: Gửi `POST /api/v1/orders`. Hệ thống gọi `IFulfillmentClient.ReserveStockAsync(orderId, items, ttlMinutes: 15)`.
* **Acceptance Scenarios**:
  1. **Given** Tất cả sản phẩm trong đơn đều giữ được kho thành công, **When** tạo đơn, **Then** đơn hàng được tạo với trạng thái `PendingPayment`, hạn thanh toán 15 phút.
  2. **Given** Một trong các món hàng vừa bị khách khác mua hết sạch, **When** tạo đơn, **Then** hệ thống hủy lệnh và ném lỗi `INSUFFICIENT_STOCK`, không tạo đơn và không trừ tiền.

---

### User Story 5 - Tự Động Hủy Đơn & Nhả Kho Khi Quá Hạn 15 Phút (Priority: P1 - Background Worker)
Là chủ Shop và sàn thương mại, nếu người mua đặt hàng nhưng quá 15 phút không thanh toán, hệ thống phải tự động hủy đơn và nhả số lượng hàng bị khóa tạm thời trở lại kho để người khác có cơ hội mua, tránh tình trạng bị "giam hàng ảo".

* **Independent Test**: Background Service `PaymentTimeoutWorker` chạy ngầm định kỳ 1 phút, quét các đơn `PendingPayment` có `PaymentDeadlineAt < UtcNow`.
* **Acceptance Scenarios**:
  1. **Given** Đơn hàng có `Status == PendingPayment` và đã quá hạn 15 phút, **When** Worker quét tới, **Then** đơn chuyển sang `Cancelled` (lý do: `PaymentTimeout`), và gọi `IFulfillmentClient.ReleaseStockAsync(orderId)` để trả hàng về kho khả dụng.

---

### User Story 6 - Xác Nhận Trừ Kho Vĩnh Viễn Khi Đã Thanh Toán (Priority: P2)
Khi người mua thanh toán tiền thành công (qua Ví hoặc VNPay), Commerce Service gửi lệnh sang Fulfillment Service để chuyển trạng thái giữ tạm (Reserved) thành thực trừ vĩnh viễn (Deducted).

* **Independent Test**: Khi thanh toán thành công, Commerce gọi `IFulfillmentClient.CommitStockAsync(orderId)`.

---

### User Story 7 - Nhả Hàng Khi Người Mua Hoặc Shop Hủy Đơn (Priority: P2 - ORD-05, ORD-06)
Khi đơn hàng bị hủy bởi người mua hoặc bị từ chối bởi người bán, Commerce Service lập tức gửi lệnh sang Fulfillment Service để nhả lại toàn bộ hàng cho kho.

* **Independent Test**: Trong `CancelOrderAsync` và `SellerRejectSubOrderAsync`, gọi `IFulfillmentClient.ReleaseStockAsync(orderId)`.

---

### User Story 8 - Tạo Vận Đơn GHN Khi Shop Nhận Đơn (Priority: P2 - ORD-04)
Khi Shop bấm tiếp nhận đơn hàng (`ORD-04`), Commerce gọi sang Fulfillment Service để tạo mã vận đơn GHN chính thức và lưu mã tracking vào đơn con.

* **Independent Test**: Trong `SellerAcceptSubOrderAsync`, gọi `IFulfillmentClient.CreateShipmentAsync(subOrderId)`.

---

## 2. Requirements & Bất Biến Nghiệp Vụ *(mandatory)*

- **Ranh giới Bounded Context**:
  - `CatalogService`: Chỉ tra cứu thông tin sản phẩm (`GetSkuDetailsAsync`). Tuyệt đối KHÔNG giữ kho tại Catalog.
  - `FulfillmentService`: Quản lý kho, tồn kho khả dụng, giữ kho (`Reserve`), nhả kho (`Release`), trừ kho (`Commit`), tính cước ship GHN và tạo vận đơn.
  - `CommerceService`: Quản lý giỏ hàng, đơn hàng, điều phối giao dịch.
- **Trải nghiệm Giỏ hàng Shopee**:
  - Dòng giỏ hàng bắt buộc có `isAvailable: bool` và `disabledReason: string?` (`"OUT_OF_STOCK"`, `"PRODUCT_INACTIVE"`).
- **Thời hạn Giữ hàng (Reservation TTL)**: Cố định 15 phút kể từ thời điểm tạo đơn.
