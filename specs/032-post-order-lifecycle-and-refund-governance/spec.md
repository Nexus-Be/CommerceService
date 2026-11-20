# Feature Specification: Post-Order Lifecycle, Address Modification and Refund Governance

**Feature Branch**: `032-post-order-lifecycle-and-refund-governance`  
**Created**: 2026-11-19  
**Status**: Draft  
**Scope**: Inward-Only (CommerceService Port 8003, Database `commerce_db:5435`)  
**Input**: Triển khai gói tính năng SPEC-032 gồm: Đổi địa chỉ nhận hàng sau khi đặt hàng cho SubOrder, Order, AuctionOrder (kèm kiểm soát cước GHN); Chuẩn hóa và mở rộng quản trị hoàn tiền (RefundsController với approve/reject); Mở rộng vòng đời đơn hàng đấu giá (timeline, cancel trước giao, retry vận đơn); và Bộ API tra cứu đơn hàng tập trung cho Admin/Staff.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Đổi địa chỉ giao hàng sau khi đặt hàng (Post-Order Change Address) (Priority: P1) 🎯 MVP

Sau khi đặt hàng thành công, khách hàng có nhu cầu đổi địa chỉ nhận hàng (ví dụ chọn nhầm địa chỉ nhà thay vì công ty) trước khi người bán bàn giao hàng cho shipper. Hệ thống cho phép người mua cập nhật địa chỉ nhận hàng cho `SubOrder`, toàn bộ `Order` (các SubOrder hợp lệ), hoặc `AuctionOrder`.

- **Why this priority**: Giải quyết nhu cầu thiết yếu hàng ngày của người mua, giảm tỷ lệ hủy đơn do nhầm địa chỉ giao hàng.
- **Independent Test**: Người mua gửi yêu cầu đổi địa chỉ `PATCH /api/v1/sub-orders/{id}/shipping-address` với `AddressId` mới $\rightarrow$ Hệ thống kiểm tra trạng thái đơn hàng, gọi GHN tính lại cước phí; nếu cước mới $\le$ cước cũ thì cập nhật địa chỉ snapshot mới và ghi nhận lịch sử thay đổi.
- **Acceptance Scenarios**:
  1. **Given** `SubOrder` ở trạng thái `PendingPayment`, **When** người mua đổi địa chỉ hợp lệ, **Then** tính lại cước ship GHN, cập nhật `ShippingAddressSnapshot`, cập nhật `ShippingFeeAmount` và `TotalAmount` của SubOrder.
  2. **Given** `SubOrder` ở trạng thái `Paid` hoặc `Confirmed` nhưng chưa có mã vận đơn (`string.IsNullOrEmpty(TrackingCode)`), **When** người mua đổi địa chỉ và cước ship mới $\le$ cước ship cũ, **Then** cập nhật địa chỉ snapshot và ghi `OrderStatusHistory` lưu vết.
  3. **Given** `SubOrder` ở trạng thái `Paid` nhưng cước ship mới lớn hơn cước ship cũ (`newShippingFee > oldShippingFee`), **When** người mua đổi địa chỉ, **Then** từ chối với lỗi HTTP 400 `SHIPPING_FEE_INCREASED` ("Shipping fee to new address exceeds prepaid amount. Please cancel and reorder.").
  4. **Given** `SubOrder` đã chuyển sang `Shipping`, `Delivered`, `Completed`, hoặc `Cancelled`, **When** người mua cố gắng đổi địa chỉ, **Then** từ chối với lỗi HTTP 400 `CANNOT_CHANGE_ADDRESS_AFTER_SHIPPING` ("Shipping address cannot be changed after the order has been shipped or completed.").
  5. **Given** `Order` cha gồm nhiều `SubOrder`, **When** người mua gọi `PATCH /api/v1/orders/{orderId}/shipping-address`, **Then** cập nhật đồng loạt cho các SubOrder thỏa mãn điều kiện và cập nhật địa chỉ của đơn cha.
  6. **Given** `AuctionOrder` ở trạng thái `PendingPayment` hoặc `Paid` chưa có `TrackingCode`, **When** người mua gọi `PATCH /api/v1/auction-orders/{id}/shipping-address`, **Then** áp dụng quy tắc kiểm tra cước phí và cập nhật tương tự.

---

### User Story 2 - Chuẩn hóa & Mở rộng Quản trị Hoàn tiền (`OrderRefund`) (Priority: P1)

Hệ thống quản lý hoàn tiền cần ngăn chặn việc gửi yêu cầu hoàn tiền tùy tiện hoặc trùng lặp, đồng thời cung cấp bộ API hoàn chỉnh để Người bán, Quản trị viên (Admin) hoặc Nhân viên (Staff) có thể tra cứu, phê duyệt giải ngân hoàn tiền hoặc từ chối yêu cầu hoàn tiền của người mua.

- **Why this priority**: Đóng chặt các lỗ hổng tài chính, bảo vệ quyền lợi người mua và người bán, bảo đảm tính minh bạch của quy trình khiếu nại hoàn tiền.
- **Independent Test**: Người mua gửi yêu cầu hoàn tiền `POST /api/v1/sub-orders/{id}/refund` $\rightarrow$ Hệ thống kiểm tra trạng thái SubOrder (`Delivered` hoặc `Completed`), chặn gửi trùng nếu đã có yêu cầu đang chờ (`Requested`). Seller/Admin gọi `POST /api/v1/refunds/{refundId}/approve` $\rightarrow$ Hệ thống hoàn tiền ví qua `WalletService` và cập nhật SubOrder sang `Refunded`.
- **Acceptance Scenarios**:
  1. **Given** `SubOrder` đang ở trạng thái `PendingPayment`, `Cancelled`, hoặc `Refunded`, **When** gọi yêu cầu refund, **Then** từ chối với mã lỗi HTTP 400 `INVALID_REFUND_STATUS` ("Sub-order status is not eligible for refund request.").
  2. **Given** `SubOrder` đã có yêu cầu refund ở trạng thái `RefundStatus.Requested`, **When** người mua gửi thêm yêu cầu refund, **Then** từ chối với mã lỗi HTTP 409 `REFUND_REQUEST_ALREADY_EXISTS` ("A pending refund request already exists for this sub-order.").
  3. **Given** người dùng đăng nhập gọi `GET /api/v1/refunds`, **When** là Buyer thì chỉ xem yêu cầu của mình, là Seller xem yêu cầu thuộc Shop của mình, là Admin/Staff xem toàn bộ danh sách phân trang tại database.
  4. **Given** yêu cầu refund ở trạng thái `Requested`, **When** Seller hoặc Admin/Staff gọi `POST /api/v1/refunds/{refundId}/approve`, **Then** hệ thống gọi `_walletClient.RefundSubOrderPaymentAsync`, chuyển `SubOrder.Status = SubOrderStatus.Refunded`, cập nhật `OrderRefund.Status = RefundStatus.Completed` và ghi nhận `CompletedAt`.
  5. **Given** yêu cầu refund ở trạng thái `Requested`, **When** Seller hoặc Admin/Staff gọi `POST /api/v1/refunds/{refundId}/reject` kèm lý do, **Then** cập nhật `OrderRefund.Status = RefundStatus.Rejected` và lưu lý do từ chối.

---

### User Story 3 - Mở rộng vòng đời Đơn hàng Đấu giá (Tracking Timeline, Cancel & Retry Shipment) (Priority: P1)

Đơn hàng đấu giá (`AuctionOrder`) cần có đầy đủ các tiện ích tương đương với đơn hàng thường: xem lộ trình vận chuyển bưu cục chi tiết, hủy đơn trước khi người bán gửi hàng (kèm hoàn tiền ví), và nút thử lại tạo vận đơn GHN khi gặp sự cố mạng.

- **Why this priority**: Đảm bảo sự cân bằng và nhất quán nghiệp vụ giữa đơn hàng thường và đơn hàng đấu giá.
- **Independent Test**: Người mua gọi `POST /api/v1/auction-orders/{id}/cancel` khi đơn ở trạng thái `Paid` $\rightarrow$ Hệ thống hoàn tiền ví tự động, hủy đơn, ghi nhận lịch sử và đồng bộ callback sang `AuctionService`.
- **Acceptance Scenarios**:
  1. **Given** `AuctionOrder` tồn tại, **When** người mua, người bán hoặc admin gọi `GET /api/v1/auction-orders/{id}/shipments/timeline`, **Then** trả về dòng thời gian lịch sử trạng thái đơn hàng và các mốc vận chuyển chi tiết.
  2. **Given** `AuctionOrder` ở trạng thái `PendingPayment` hoặc `Paid` chưa được người bán chấp nhận (chưa chuyển sang `Confirmed`), **When** người mua gọi `POST /api/v1/auction-orders/{id}/cancel`, **Then** hủy đơn thành công. Nếu đơn đã `Paid`, tự động hoàn trả số tiền đã thanh toán vào ví của người mua và gửi thông báo callback sang `AuctionService`.
  3. **Given** `AuctionOrder` đã ở trạng thái `Confirmed` nhưng chưa có mã vận đơn `TrackingCode` do lỗi mạng bưu cục, **When** người bán hoặc staff gọi `POST /api/v1/auction-orders/{id}/shipments/retry`, **Then** hệ thống gọi lại `_fulfillmentClient.CreateShipmentAsync` và cập nhật thông tin vận đơn GHN.

---

### User Story 4 - Quản trị Đơn hàng Tập trung cho Admin & Staff (Priority: P2)

Quản trị viên (Admin) và Nhân viên vận hành (Staff) cần có các API chuyên biệt để tra cứu, tìm kiếm và kiểm soát toàn bộ đơn hàng cha (`Orders`) và đơn hàng con (`SubOrders`) trên toàn sàn với khả năng lọc đa tiêu chí và phân trang tại database.

- **Why this priority**: Hỗ trợ bộ phận Vận hành và Chăm sóc khách hàng (CSKH) tra cứu và xử lý sự cố đơn hàng nhanh chóng.
- **Independent Test**: Admin gọi `GET /api/v1/admin/orders?status=Paid&page=1&pageSize=20` $\rightarrow$ Trả về danh sách đơn hàng cha toàn hệ thống phân trang chuẩn `PagedData<OrderSummaryResponse>`.
- **Acceptance Scenarios**:
  1. **Given** người dùng không có role `admin` hoặc `staff`, **When** gọi `GET /api/v1/admin/orders` hoặc `GET /api/v1/admin/sub-orders`, **Then** trả về HTTP 403 `FORBIDDEN`.
  2. **Given** Admin/Staff gọi API tra cứu, **When** truyền các tham số lọc (`buyerId`, `sellerId`, `status`, `trackingCode`, `fromDate`, `toDate`), **Then** thực hiện câu truy vấn `IQueryable` lọc trực tiếp tại PostgreSQL và trả về kết quả phân trang chính xác.

---

## Edge Cases

- **Đổi địa chỉ khi đơn COD ở trạng thái Paid (không thực tế, nhưng mã logic cần chặt chẽ)**:
  - Đơn COD chỉ ở `PendingPayment` cho đến khi giao xong; nếu đổi địa chỉ lúc `PendingPayment`, phí ship cập nhật lại vào tổng thu hộ COD (`TotalAmount`).
- **Nhiều yêu cầu đổi địa chỉ đồng thời (Race Condition)**:
  - Áp dụng kiểm tra trạng thái và cập nhật nguyên tử, ghi nhận `OrderStatusHistory` rõ ràng từng lần đổi.
- **Approve hoàn tiền khi ví hệ thống hoặc WalletService lỗi kết nối**:
  - Bọc Polly retry cho `_walletClient.RefundSubOrderPaymentAsync`. Nếu không thành công, ném lỗi HTTP 503 `WALLET_SERVICE_UNAVAILABLE` và không cập nhật trạng thái đơn thành `Refunded` để tránh lệch sổ sách.

---

## Requirements & Non-Functional Constraints

1. **Port & Database**: Cố định Port `8003`, Database `5435` (`commerce_db`).
2. **Clean Architecture & No-Repository**:
   - Sử dụng trực tiếp `ICommerceDbContext` / `AppDbContext`. Cấm tạo Repository pattern.
3. **Phân trang bắt buộc tại Database**:
   - Mọi API danh sách (`GET /api/v1/refunds`, `GET /api/v1/admin/orders`, `GET /api/v1/admin/sub-orders`) bắt buộc dùng `.Skip().Take()` và trả về `PagedData<T>`.
4. **Error Protocol**:
   - Mã lỗi chuẩn `SCREAMING_SNAKE_CASE` (ví dụ: `CANNOT_CHANGE_ADDRESS_AFTER_SHIPPING`, `SHIPPING_FEE_INCREASED`, `INVALID_REFUND_STATUS`, `REFUND_REQUEST_ALREADY_EXISTS`, `REFUND_NOT_FOUND`).
   - Thông báo lỗi 100% tiếng Anh viết hoa chữ cái đầu câu (Sentence case).
5. **Ownership Enforcement**:
   - Kiểm tra chặt chẽ `currentUserId` từ `HttpContext.GetUserId()` đối với Người mua và Người bán; chỉ Admin/Staff mới được quyền bypass kiểm tra chủ sở hữu.
