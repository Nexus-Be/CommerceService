# Feature Specification: Order & Checkout Service (Quản lý Đặt hàng & Đơn hàng)

**Feature Directory**: `specs/002-order-service`  
**Feature Branch**: `feat/cart-service-v2`  
**Created**: 2026-11-01  
**Status**: Ready for Implementation (Phase 1 & Phase 2)  
**Input**: Triển khai trọn bộ 10 RESTful API Quản lý Báo giá, Đặt hàng và Vòng đời Đơn hàng (`R75` đến `R99` theo `docs/COMMERCE_API_RECONCILIATION_REPORT.md` và `docs/api_catalog.md`).  

---

## 1. User Scenarios & Testing *(mandatory)*

### User Story 1 - Xem Trước Báo Giá Thanh Toán (Priority: P1 - CHK-01 / R75)
Là một người mua (Buyer) đã chọn một hoặc nhiều món hàng từ giỏ, tôi muốn xem trước báo giá chi tiết gồm tiền hàng, tiền ship của từng Shop (tính qua đơn vị vận chuyển GHN), các khoản giảm giá từ voucher và tổng tiền phải trả, để tôi cân nhắc trước khi chốt đặt hàng.

* **Independent Test**: Gửi `POST /api/v1/checkouts/quote` kèm danh sách `cartItemIds`, địa chỉ nhận hàng (`shippingAddress`), số điện thoại, phương thức thanh toán và voucher. Hệ thống tính toán, trả về `200 OK` chứa chi tiết từng Shop và mã `quoteId` có thời hạn 15 phút.
* **Acceptance Scenarios**:
  1. **Given** Người mua chọn 2 món hàng từ 2 Shop khác nhau kèm địa chỉ nhận hợp lệ, **When** gọi `POST /api/v1/checkouts/quote`, **Then** hệ thống trả về `quoteId` (UUID) ở root, danh sách 2 `shops` với `itemsAmount`, `productDiscountAmount`, `shopDiscountAmount`, `systemDiscountAmount`, `discountAmount`, `shippingFeeAmount`, `totalAmount` của từng shop, cùng `grandTotal` và `expiredAt` (+15 phút).
  2. **Given** Danh sách `cartItemIds` rỗng hoặc không tồn tại trong giỏ của buyer, **When** gọi API, **Then** trả về `400 Bad Request` kèm mã lỗi `CART_EMPTY` hoặc `CART_ITEM_NOT_FOUND`.
  3. **Given** Địa chỉ nhận hàng thiếu thông tin bắt buộc (thiếu người nhận, số điện thoại, hoặc xã/huyện), **When** gọi API, **Then** trả về `400 Bad Request` kèm mã lỗi `VAL_INVALID_INPUT`.

---

### User Story 2 - Đặt Hàng Chính Thức Từ Báo Giá (Priority: P1 - CHK-02 / R76)
Là một người mua, sau khi đã xem trước báo giá và đồng ý với số tiền, tôi muốn bấm "Đặt hàng" để hệ thống tạo đơn hàng cha (`Order`) và các đơn hàng con (`SubOrder`) theo từng Shop trong một transaction an toàn, đồng thời tự động xóa các món đã mua khỏi giỏ hàng.

* **Independent Test**: Gửi `POST /api/v1/orders` kèm `quoteId`, `paymentMethod`, `shippingAddress`, `contactPhone`, `note`. Hệ thống lưu trữ `Order`, các `SubOrder` (với `ShippingAddressSnapshot` lưu dưới dạng JSONB), gọi thanh toán nếu chọn Wallet/VNPay, dọn dẹp các món khỏi giỏ hàng và trả về `201 Created`.
* **Acceptance Scenarios**:
  1. **Given** `quoteId` hợp lệ và còn hạn trong 15 phút, người mua chọn `PaymentMethod: "Wallet"`, **When** gọi `POST /api/v1/orders`, **Then** hệ thống tạo `Order` trạng thái `PendingPayment`, gọi `IWalletClient.DirectCheckoutAsync`, nếu thành công cập nhật `Paid`, dọn giỏ hàng và trả về `201 Created` kèm `orderId`, `orderNo`, `subOrderIds`, `status: "Paid"`.
  2. **Given** Người mua chọn `PaymentMethod: "VnPay"`, **When** gọi `POST /api/v1/orders`, **Then** hệ thống tạo đơn với trạng thái `PendingPayment`, sinh `paymentUrl` từ Wallet Service và trả về cho client chuyển hướng.
  3. **Given** `quoteId` đã quá hạn 15 phút hoặc không tồn tại, **When** gọi API, **Then** trả về `400 Bad Request` kèm mã lỗi `QUOTE_EXPIRED`.
  4. **Given** Một trong các mặt hàng trong quote vừa bị người khác mua hết hàng trong kho, **When** gọi API, **Then** trả về `400 Bad Request` kèm mã lỗi `INSUFFICIENT_STOCK`.

---

### User Story 3 - Xem Chi Tiết Đơn Hàng Cha (Priority: P2 - ORD-02 / R77)
Là một người mua, tôi muốn xem thông tin tổng quát của đơn hàng cha bao gồm danh sách các đơn con, phương thức thanh toán, tổng thanh toán và tiến độ xử lý.

* **Independent Test**: Gửi `GET /api/v1/orders/{orderId}`. Trả về `OrderResponse` đầy đủ thông tin hoặc `404 Not Found` nếu không tồn tại.
* **Acceptance Scenarios**:
  1. **Given** `orderId` tồn tại và thuộc sở hữu của `currentUserId`, **When** gọi `GET /api/v1/orders/{orderId}`, **Then** trả về `200 OK` với thông tin `orderNo`, `totalAmount`, `status`, danh sách `subOrders`.
  2. **Given** `orderId` thuộc về người mua khác và caller không phải Admin/Staff, **When** gọi API, **Then** trả về `403 Forbidden` kèm mã lỗi `FORBIDDEN`.

---

### User Story 4 - Xem Lịch Sử Đơn Hàng Của Tôi (Priority: P2 - ORD-01 / R78)
Là một người mua, tôi muốn xem danh sách các đơn hàng tôi đã từng đặt, có phân trang để tiện tra cứu và lọc theo trạng thái.

* **Independent Test**: Gửi `GET /api/v1/orders?pageIndex=1&pageSize=10&status=Paid`. Trả về `PagedData<OrderSummaryResponse>`.
* **Acceptance Scenarios**:
  1. **Given** Người mua có 15 đơn hàng trong hệ thống, **When** gọi `GET /api/v1/orders?pageIndex=1&pageSize=10`, **Then** trả về 10 đơn hàng trang đầu tiên, metadata `totalCount: 15`, `totalPages: 2`.
  2. **Given** Lọc theo `status: "PendingPayment"`, **When** gọi API, **Then** chỉ trả về các đơn đang chờ thanh toán.

---

### User Story 5 - Xem Chi Tiết Đơn Hàng Con Của Shop (Priority: P1 - ORD-02B / R79)
Là một người mua hoặc Người bán (Shop), tôi muốn xem chi tiết đơn hàng con cụ thể của một Shop gồm các mặt hàng, breakdown giảm giá, snapshot địa chỉ giao hàng và trạng thái đơn con.

* **Independent Test**: Gửi `GET /api/v1/sub-orders/{subOrderId}`. Trả về đối tượng đơn `SubOrderDetailResponse` (Single DTO, tuyệt đối không trả phân trang `PagedData`).
* **Acceptance Scenarios**:
  1. **Given** `subOrderId` tồn tại và caller là chủ đơn (Buyer) hoặc là Shop bán món đó (Seller) hoặc Admin, **When** gọi API, **Then** trả về `200 OK` chứa đầy đủ `subOrderNo`, `shopName`, `status`, `shippingAddress` (Object đầy đủ gồm cả `ghnDistrictId`, `ghnWardCode`, `fullAddressText`), `itemsAmount`, `productDiscountAmount`, `shopDiscountAmount`, `systemDiscountAmount`, `discountAmount`, `shippingFeeAmount`, `totalAmount`, và mảng `items`.
  2. **Given** Người dùng khác không liên quan cố tình truy cập `subOrderId`, **When** gọi API, **Then** trả về `403 Forbidden`.

---

### User Story 6 - Người Mua Hủy Đơn Hàng (Priority: P2 - ORD-06 / R80)
Là một người mua, tôi muốn hủy đơn hàng nếu đơn vẫn đang ở trạng thái chờ thanh toán hoặc chưa được người bán tiếp nhận.

* **Independent Test**: Gửi `POST /api/v1/orders/{orderId}/cancel` kèm `{ reasonCode, reasonText }`.
* **Acceptance Scenarios**:
  1. **Given** Đơn hàng đang ở trạng thái `PendingPayment`, **When** người mua hủy với `reasonCode: "BuyerChangedMind"`, **Then** hệ thống chuyển trạng thái `Order` và các `SubOrder` sang `Cancelled`, ghi nhận lịch sử và trả về `200 OK`.
  2. **Given** Đơn hàng đã chuyển sang `Shipping` hoặc `Completed`, **When** người mua cố hủy, **Then** trả về `400 Bad Request` kèm mã lỗi `ORDER_CANNOT_BE_CANCELLED`.

---

### User Story 7 - Người Bán Tiếp Nhận Đơn Hàng (Priority: P2 - ORD-04 / R83)
Là một Người bán (Seller), tôi muốn xác nhận đồng ý xử lý và chuẩn bị gói hàng cho đơn con của Shop mình.

* **Independent Test**: Gửi `POST /api/v1/orders/{orderId}/accept` kèm `{ subOrderId, note }`.
* **Acceptance Scenarios**:
  1. **Given** `SubOrder` thuộc quyền sở hữu của Shop (`sellerId`) và đang ở trạng thái `Paid` (hoặc `PendingPayment` với đơn COD), **When** Shop bấm nhận đơn, **Then** chuyển trạng thái `SubOrder` sang `Confirmed` (`SellerAccepted = 2`), ghi nhận `SellerAcceptedAt = DateTimeOffset.UtcNow`.

---

### User Story 8 - Người Bán Từ Chối Đơn Hàng (Priority: P2 - ORD-05 / R84)
Là một Người bán, nếu sản phẩm tạm thời hết hàng hoặc không thể đáp ứng, tôi muốn từ chối đơn hàng con và nêu rõ lý do.

* **Independent Test**: Gửi `POST /api/v1/orders/{orderId}/reject` kèm `{ subOrderId, reasonCode, reasonText }`.
* **Acceptance Scenarios**:
  1. **Given** Shop từ chối đơn, **When** gọi API, **Then** hệ thống cập nhật `SubOrderStatus = Cancelled`, ghi nhận `cancellationReason: "SellerRejected"`, kích hoạt luồng hoàn tiền nếu đơn đã thanh toán trước.

---

### User Story 9 - Theo Dõi Vận Chuyển Đơn Hàng (Priority: P3 - ORD-08 / R87)
Là một người mua, tôi muốn tra cứu thông tin mã vận đơn và tiến độ giao hàng của các đơn con.

* **Independent Test**: Gửi `GET /api/v1/shipments/order/{orderId}`. Trả về thông tin vận chuyển của từng Shop.

---

### User Story 10 - Yêu Cầu Hoàn Tiền Đơn Con (Priority: P3 - ORD-13 / R99)
Là một người mua hoặc Admin, khi hàng bị hỏng hoặc giao sai, tôi muốn tạo yêu cầu hoàn tiền cho đơn con cụ thể.

* **Independent Test**: Gửi `POST /api/v1/sub-orders/{subOrderId}/refund` kèm `{ reason, note }`. Trả về `200 OK` ghi nhận `OrderRefund` với trạng thái `Requested`.

---

## 2. Requirements & Bất Biến Nghiệp Vụ *(mandatory)*

### 2.1 Bất biến Dữ liệu & Tài chính (Financial Invariants)
- **Tên trường cước phí vận chuyển**: 100% sử dụng **`shippingFeeAmount`**. CẤM từ khóa `shippingFee`.
- **Công thức bất biến tiền đơn con**:
  $$\text{totalAmount} = \text{itemsAmount} - \text{discountAmount} + \text{shippingFeeAmount}$$
  $$\text{discountAmount} = \text{productDiscountAmount} + \text{shopDiscountAmount} + \text{systemDiscountAmount}$$
- **Snapshot Địa chỉ Giao hàng**: Bắt buộc lưu trữ dạng PostgreSQL `jsonb` Value Object trong `SubOrder` thông qua EF Core 8 `.ToJson()`.

### 2.2 Quy tắc Casing & Serialization
- **Mã lỗi kỹ thuật (Control Plane)**: 100% `SCREAMING_SNAKE_CASE` (ví dụ: `QUOTE_EXPIRED`, `ORDER_NOT_FOUND`).
- **Domain Enum (Data Plane)**: 100% `PascalCase` (ví dụ: `"PendingPayment"`, `"SellerRejected"`, `"Wallet"`).

---

## 3. Kiến Trúc Tầng (Clean Architecture)

- **Domain Layer**:
  - `Commerce.Domain/ValueObjects/ShippingAddressSnapshot.cs`
  - `Commerce.Domain/Entities/SubOrder.cs` (Navigation `ShippingAddress`)
- **Application Layer**:
  - `Commerce.Application/Services/Order/OrderRequests.cs`
  - `Commerce.Application/Services/Order/OrderResponses.cs`
  - `Commerce.Application/Services/Order/IOrderService.cs`
  - `Commerce.Application/Services/Order/OrderService.cs`
- **Infrastructure Layer**:
  - `Commerce.Infrastructure/Persistence/Configurations/SubOrderConfiguration.cs` (`.ToJson()` mapping)
- **Api Layer**:
  - `Commerce.Api/Controllers/Public/OrdersController.cs` (Ghép cặp 1-1 với `IOrderService`)
