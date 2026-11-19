# Feature Specification: Core Remediation, Security Hardening & Essential Endpoints

**Feature Branch**: `030-core-remediation-and-essential-endpoints`  
**Created**: 2026-11-18  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  
**Input**: Khắc phục dứt điểm 6 lỗi logic nền tảng, lỗ hổng bảo mật và thiếu sót dữ liệu nghiêm trọng (P0) đã được đối soát: Hủy vận đơn GHN khi hủy đơn mẹ, khóa snapshot địa chỉ khi tạo đơn, API xem đơn đấu giá của Buyer, vá IDOR Voucher, bổ sung migration IsEscrowSettled, và mở quyền Admin/Staff tra cứu đơn hàng.

---

## 1. Executive Summary & Value Proposition

Gói tính năng **SPEC-030** là bước đệm nền tảng đầu tiên trong lộ trình chuẩn hóa vòng đời đơn hàng tại `CommerceService`. Toàn bộ 6 hạng mục trong spec này đều đạt tính chất **100% Inward-Only & Self-Contained**, không phụ thuộc vào tiến độ của bất kỳ dịch vụ đối tác nào (`UserService`, `Gateway`), cho phép triển khai, kiểm thử và nghiệm thu ngay lập tức:

1. **Ngăn chặn thất thoát chi phí giao nhận (GHN Courier Safety)**:
   - Khi hủy đơn hàng mẹ (`CancelOrderAsync`), hệ thống tự động gọi sang `FulfillmentService` hủy toàn bộ các phiếu gửi GHN của các đơn con đã xác nhận. Chấm dứt triệt để tình trạng bưu tá GHN vẫn đến kho lấy hàng của đơn đã hủy.
2. **Bảo toàn tính nhất quán địa chỉ và cước vận chuyển**:
   - Khóa chặt `ShippingAddressSnapshot` trong `CreateOrderAsync` theo dữ liệu đã chốt với GHN tại bước Báo giá (`CachedQuote`). Ngăn chặn gian lận cước và lỗi giao sai địa chỉ do client gửi chuỗi địa chỉ văn bản tự do.
3. **Cung cấp API Danh sách Đơn đấu giá cho Người mua (`GET /api/v1/auction-orders/me`)**:
   - Cho phép người mua thắng đấu giá tra cứu danh sách đơn đấu giá của mình theo phân trang và trạng thái, hiển thị rõ số tiền cọc (`DepositAmount`), tiền thanh toán còn lại và hạn chót thanh toán.
4. **Vá lỗ hổng bảo mật IDOR & Kiểm tra giới hạn dùng Voucher**:
   - Buộc kiểm tra quyền sở hữu giỏ hàng (`ci.Cart.BuyerId == buyerId`) trong mọi luồng truy vấn voucher của `DiscountService`.
   - Bổ sung kiểm tra ngưỡng sử dụng tối đa (`MaxUsesTotal`) và giới hạn trên từng tài khoản (`MaxUsesPerUser`) trước khi cho phép áp dụng voucher.
5. **Đồng bộ hóa Database Schema EF Core (`IsEscrowSettled`)**:
   - Tạo migration `20261118010000_AddSubOrderIsEscrowSettled.cs` bổ sung cột `IsEscrowSettled` vào bảng `SubOrders`, đồng bộ hoàn toàn giữa Database, Entity và ModelSnapshot.
6. **Mở quyền Quản trị viên / Nhân viên vận hành (RBAC Governance)**:
   - Hỗ trợ `isAdminOrStaff` trong `OrderService` cho phép Admin và Staff xem chi tiết đơn hàng, đơn con và lộ trình vận đơn phục vụ công tác hỗ trợ khách hàng.

---

## 2. User Scenarios & Acceptance Criteria

### User Story 1 - Tự động hủy vận đơn GHN khi hủy đơn hàng mẹ (Priority: P1)
Khi Người mua hoặc Người bán hủy một Đơn hàng mẹ (`POST /api/v1/orders/{orderId}/cancel`), hệ thống phải kiểm tra từng đơn hàng con (`SubOrder`). Nếu đơn con đã ở trạng thái `Confirmed` và đã sinh mã vận đơn GHN (`TrackingCode`), hệ thống phải gửi lệnh hủy vận đơn sang `FulfillmentService` trước khi hoàn tiền và nhả tồn kho.

- **Independent Test**: Tạo đơn hàng mẹ gồm 2 shop, shop A đã `Confirmed` và có `TrackingCode`. Khách gọi hủy đơn mẹ $\rightarrow$ Xác nhận mock `_fulfillmentClient.CancelShipmentAsync` được gọi đúng `subOrder.Id` của shop A, đơn hàng chuyển sang `Cancelled` và ví được hoàn tiền đầy đủ.
- **Acceptance Scenarios**:
  1. **Given** một đơn hàng ở trạng thái `Paid` có SubOrder đã `Confirmed` kèm `TrackingCode`, **When** người mua bấm hủy đơn, **Then** gọi `_fulfillmentClient.CancelShipmentAsync`, đổi trạng thái SubOrder sang `Cancelled`, hoàn tiền ví và nhả kho.
  2. **Given** một đơn hàng có SubOrder chưa có `TrackingCode`, **When** hủy đơn mẹ, **Then** bỏ qua gọi hủy vận đơn GHN và tiếp tục hủy các tài nguyên khác bình thường.
  3. **Given** một đơn hàng có SubOrder đang ở trạng thái `Shipping`, `Delivered` hoặc `Completed`, **When** người mua gọi API hủy đơn, **Then** ném lỗi HTTP 400 `ORDER_CANNOT_BE_CANCELLED`.

---

### User Story 2 - Khóa chặt Snapshot địa chỉ GHN khi tạo đơn (Priority: P1)
Khi khách hàng gửi yêu cầu tạo đơn hàng (`POST /api/v1/orders`), hệ thống phải sử dụng nguyên vẹn dữ liệu địa chỉ giao hàng và mã vùng GHN (`GhnDistrictId`, `GhnWardCode`) từ báo giá đã được khóa trong cache (`cachedQuote.Request.ShippingAddress`).

- **Independent Test**: Gọi Báo giá với địa chỉ Quận 1 TP.HCM (phí ship 22k). Khi gọi `POST /api/v1/orders`, cố tình truyền trường `request.ShippingAddress = "123 Đường Lạ, Hà Nội"`. Kiểm tra `SubOrder.ShippingAddress` trong DB vẫn lưu đúng địa chỉ Quận 1 và mã vùng GHN của Quận 1, không bị chuỗi lạ ghi đè.
- **Acceptance Scenarios**:
  1. **Given** báo giá `cachedQuote` hợp lệ, **When** tạo đơn hàng, **Then** `addressSnapshot` nhận `FullAddressText` từ `cachedQuote.Request.ShippingAddress.FullAddressText` (hoặc fallback `request.ShippingAddress` chỉ khi snapshot text rỗng), đảm bảo không làm lệch mã vùng GHN.

---

### User Story 3 - API Danh sách Đơn đấu giá cho Người mua (Priority: P1)
Người mua sau khi thắng đấu giá có thể vào trang cá nhân để xem danh sách các đơn đấu giá của mình, theo dõi tình trạng thanh toán và thời hạn thanh toán.

- **Independent Test**: Người mua đăng nhập gọi `GET /api/v1/auction-orders/me?page=1&pageSize=20`. Kiểm tra danh sách trả về chỉ chứa các đơn có `BuyerId == currentUserId`, có đầy đủ `DepositAmount`, `TotalAmount`, `OrderSource` và phân trang chuẩn.
- **Acceptance Scenarios**:
  1. **Given** người mua có 3 đơn đấu giá, **When** gọi `GET /api/v1/auction-orders/me`, **Then** trả về `200 OK` với danh sách 3 đơn sắp xếp theo `CreatedAt DESC`.
  2. **Given** người mua lọc theo `status=PendingPayment`, **When** gọi API, **Then** chỉ trả về các đơn đấu giá đang chờ thanh toán.
  3. **Given** người dùng chưa đăng nhập hoặc không có role hợp lệ, **When** gọi API, **Then** trả về `401 Unauthorized` hoặc `403 Forbidden`.

---

### User Story 4 - Vá bảo mật IDOR & Kiểm tra giới hạn dùng Voucher (Priority: P1)
Hệ thống xử lý Voucher phải đảm bảo an toàn dữ liệu: người dùng không thể mượn ID sản phẩm giỏ hàng của người khác để kiểm tra voucher, và voucher phải bị từ chối nếu đã hết số lượng tổng hoặc người dùng đã dùng hết số lượt cho phép.

- **Independent Test**: 
  - Đăng nhập User B, gọi `POST /api/v1/discounts/validate` với `cartItemIds` thuộc giỏ hàng của User A $\rightarrow$ Trả về `400 VAL_INVALID_INPUT`.
  - Gọi kiểm tra voucher đã đạt `UsedCount + ReservedCount >= MaxUsesTotal` $\rightarrow$ Trả về `isValid: false`, lý do `VOUCHER_OUT_OF_STOCK`.
- **Acceptance Scenarios**:
  1. **Given** danh sách `cartItemIds`, **When** `DiscountService` nạp sản phẩm giỏ hàng, **Then** luôn kèm điều kiện `ci.Cart.BuyerId == buyerId && !ci.IsDeleted`.
  2. **Given** voucher có cấu hình `MaxUsesTotal`, **When** tổng số lượng đã dùng và đang giữ chỗ vượt ngưỡng, **Then** đánh dấu không hợp lệ.
  3. **Given** voucher có cấu hình `MaxUsesPerUser`, **When** số lần sử dụng của người mua vượt ngưỡng, **Then** đánh dấu không hợp lệ.

---

### User Story 5 - Bổ sung EF Core Database Migration IsEscrowSettled (Priority: P1)
Hệ thống database phải được cập nhật đầy đủ cột `IsEscrowSettled` trên bảng `SubOrders` thông qua migration chính thức của EF Core.

- **Independent Test**: Chạy kiểm thử tự động `MigrationConsistencyTests` $\rightarrow$ ModelSnapshot khớp 100% với cấu hình Entity và toàn bộ các file migration sinh SQL thành công.
- **Acceptance Scenarios**:
  1. **Given** dự án `Commerce.Infrastructure`, **When** kiểm tra thư mục `Persistence/Migrations`, **Then** có file migration thêm cột `IsEscrowSettled` kiểu `boolean` (mặc định `false`) vào bảng `SubOrders`.

---

### User Story 6 - Phân quyền Admin / Staff tra cứu chi tiết đơn hàng (Priority: P2)
Quản trị viên và Nhân viên vận hành có thể xem chi tiết đơn hàng mẹ, đơn hàng con, và lộ trình bưu cục của bất kỳ người dùng nào trên hệ thống mà không bị chặn bởi rào cản quyền sở hữu cá nhân.

- **Independent Test**: Đăng nhập tài khoản có role `admin` hoặc `staff`, gọi `GET /api/v1/orders/{orderId}` của một khách hàng khác $\rightarrow$ Trả về `200 OK` với đầy đủ thông tin đơn hàng thay vì ném lỗi `403 FORBIDDEN`.
- **Acceptance Scenarios**:
  1. **Given** người dùng có role `admin` hoặc `staff`, **When** gọi `GetOrderDetailAsync`, `GetSubOrderDetailAsync`, `GetShipmentTrackingAsync`, `GetShipmentTimelineAsync`, **Then** hệ thống cho phép truy cập dữ liệu và trả về kết quả đầy đủ.
  2. **Given** người dùng là khách hàng thông thường khác chủ đơn, **When** gọi các API trên, **Then** hệ thống ném lỗi HTTP 403 `FORBIDDEN`.
