# Feature Specification: Buyer Order Cancellation with Automated Wallet Refund & 2-Tier Administrative Address Normalization

**Feature Identifier**: `015-buyer-cancellation-and-address-alignment`  
**Created**: 2026-11-09  
**Status**: Ready for Implementation (`speckit-implement`)  
**Target Service**: `CommerceService` (Port `8003`, Database `commerce_db:5435`)  
**Priority**: CRITICAL (P1)  

---

## 1. Executive Summary & Problem Statement

### Lỗ hổng 1: Đơn hàng thanh toán bằng Ví điện tử bị khóa cứng không thể hủy (Critical Business Bug)
Khi khách hàng đặt hàng và chọn phương thức thanh toán `Wallet`, đơn hàng lập tức chuyển sang trạng thái `OrderStatus.Paid` ngay khi tạo thành công. Tuy nhiên, logic hủy đơn hiện tại tại `OrderService.CancelOrderAsync` đặt điều kiện kiểm tra cứng:
```csharp
if (order.Status != OrderStatus.PendingPayment)
{
    throw new CommerceDomainException("ORDER_CANNOT_BE_CANCELLED", "Order cannot be cancelled in its current status.");
}
```
Hậu quả: Khách hàng mua sắm bằng Ví Nexus **hoàn toàn không thể hủy đơn hàng** ngay cả khi các Shop chưa hề đóng gói hay giao hàng. Thêm vào đó, hệ thống chưa có cơ chế hoàn trả tiền ví tự động khi khách hủy đơn, buộc phải đối soát thủ công.

### Lỗ hổng 2: Lỗi xác thực địa chỉ 2 cấp hành chính (Frontend/Backend Incompatibility)
Giao diện người dùng (`nexus-dev-fe`) đã chuẩn hóa quy trình nhập địa chỉ giao hàng theo mô hình tinh giản 2 cấp (Tỉnh/Thành phố – Phường/Xã), loại bỏ trường Quận/Huyện (`district: ""`). Tuy nhiên, Backend Commerce Service vẫn đặt ràng buộc bắt buộc:
```csharp
string.IsNullOrWhiteSpace(request.ShippingAddress.District)
```
Hậu quả: Mọi request checkout từ Frontend mới đều bị ném lỗi HTTP 400 `VAL_INVALID_INPUT` (`"Shipping address fields cannot be empty."`). Đồng thời, khi `District` rỗng, thuật toán ghép chuỗi cũ sinh ra dấu phẩy kép `, ,` trên giao diện vận chuyển.

---

## 2. User Stories & Acceptance Criteria

### User Story 1: Buyer Order Cancellation for Paid Orders with Automated Wallet Refund (Priority: P1)
> **Là một** Người mua hàng (Buyer) đã thanh toán đơn hàng bằng Ví Nexus,  
> **Tôi muốn** có thể chủ động hủy đơn hàng khi các Shop chưa tiến hành giao hàng,  
> **Để** tôi được tự động hoàn trả 100% số tiền đã thanh toán về lại Ví Nexus mà không cần khiếu nại CSKH.

#### Scenario 1.1: Hủy thành công đơn hàng đã thanh toán (Paid) trước khi giao
- **GIVEN** Một đơn hàng hợp lệ của Buyer đang ở trạng thái `OrderStatus.Paid`.
- **AND** Toàn bộ các đơn hàng con (`SubOrders`) đều chưa chuyển sang trạng thái `Shipping`, `Delivered`, hoặc `Completed`.
- **WHEN** Buyer gửi yêu cầu hủy đơn qua `POST` hoặc `PUT /api/v1/orders/{orderId}/cancel`.
- **THEN** Hệ thống chuyển trạng thái `Order.Status = OrderStatus.Cancelled`.
- **AND** Mọi đơn con chưa hủy chuyển sang `SubOrderStatus.Cancelled` kèm `CancelledAt = UtcNow`.
- **AND** Hệ thống ghi nhận bản ghi `OrderCancellation` với `ActorRole = Buyer`, `WasPaid = true`, `InventoryReleased = true`.
- **AND** Hệ thống tự động tạo bản ghi `OrderRefund` (Status: `Requested`) và kích hoạt `_walletClient.RefundSubOrderPaymentAsync(...)` với `idempotencyKey: $"refund-suborder-cancel-{so.Id}"`.
- **AND** Khi hoàn tiền ví thành công, cập nhật `OrderRefund.Status = RefundStatus.Completed`.
- **AND** Hệ thống gọi `_fulfillmentClient.ReleaseStockAsync(order.Id)` để nhả tồn kho đã giữ.
- **AND** Trả về HTTP 200 OK `{ orderId, status: "Cancelled", cancelledAt }`.

#### Scenario 1.2: Chặn hủy khi đơn con đã bàn giao vận chuyển
- **GIVEN** Đơn hàng có ít nhất một đơn con (`SubOrder`) ở trạng thái `SubOrderStatus.Shipping`, `Delivered`, hoặc `Completed`.
- **WHEN** Buyer gửi yêu cầu hủy đơn qua `POST` hoặc `PUT /api/v1/orders/{orderId}/cancel`.
- **THEN** Hệ thống từ chối yêu cầu, ném ngoại lệ `CommerceDomainException("ORDER_CANNOT_BE_CANCELLED", "Order cannot be cancelled once shipped, delivered, or completed.")` với mã HTTP 400.

#### Scenario 1.3: Chặn hủy khi người gọi không phải chủ sở hữu đơn hàng
- **GIVEN** Một người dùng không phải là `BuyerId` của đơn hàng.
- **WHEN** Cố tình gửi request hủy đơn.
- **THEN** Hệ thống từ chối với HTTP 403 `FORBIDDEN` và message `"You do not have permission to cancel this order."`.

---

### User Story 2: 2-Tier Administrative Address Normalization (Priority: P1)
> **Là một** Khách hàng và Kỹ sư Frontend,  
> **Tôi muốn** hệ thống checkout chấp nhận địa chỉ giao hàng mô hình 2 cấp (không bắt buộc nhập Quận/Huyện),  
> **Để** quá trình khảo giá (`quote`) và đặt hàng (`create order`) diễn ra mượt mà, không bị lỗi validation và không sinh lỗi dấu phẩy kép trên nhãn in vận chuyển.

#### Scenario 2.1: Khảo giá thành công với địa chỉ 2 cấp
- **GIVEN** Request khảo giá `POST /api/v1/checkouts/quote` chỉ cung cấp `receiverName`, `contactPhone`, `street`, `ward`, `province` (không có `district` hoặc `district = null`).
- **WHEN** Hệ thống thực thi thẩm định đầu vào trong `QuoteCheckoutAsync`.
- **THEN** Request vượt qua validation thành công (không ném lỗi `VAL_INVALID_INPUT`).
- **AND** Phí ship GHN và số dư ví được tính toán chính xác, lưu cache Quote TTL 15 phút.

#### Scenario 2.2: Ghép chuỗi địa chỉ thông minh trong Snapshot
- **GIVEN** Khách hàng đặt hàng với địa chỉ không có `district`.
- **WHEN** Hệ thống khởi tạo đối tượng `ShippingAddressSnapshot`.
- **THEN** Thuộc tính `District` được gán mặc định an toàn là `string.Empty` (không bao giờ null trong JSON lưu DB).
- **AND** Chuỗi `FullAddressText` được ghép tự động từ các thành phần không rỗng qua `string.Join(", ", parts)`, tuyệt đối không xuất hiện dấu phẩy kép `, ,`.

---

### User Story 3: REST Controller Ergonomics & HTTP Verb Expansion (Priority: P2)
> **Là một** Kỹ sư Frontend (Web/Mobile),  
> **Tôi muốn** endpoint hủy đơn hỗ trợ cả phương thức `POST` và `PUT`, đồng thời cho phép request body rỗng,  
> **Để** tích hợp giao diện linh hoạt mà không bị lỗi 400 hoặc 405 Method Not Allowed.

#### Scenario 3.1: Hủy đơn bằng HTTP PUT hoặc không truyền body
- **WHEN** Client gọi `PUT /api/v1/orders/{orderId}/cancel` không kèm Request Body (Body = null).
- **THEN** Controller tự động khởi tạo request mặc định với `ReasonCode = CancellationReasonCode.BuyerChangedMind` và `ReasonText = "Buyer cancelled order"`.
- **AND** Đơn hàng được hủy thành công và trả về HTTP 200 OK.

---

### User Story 4: Automated Test Suite Realignment & Verification (Priority: P1)
> **Là một** Kỹ sư QA / DevOps,  
> **Tôi muốn** toàn bộ bộ Unit Tests được cập nhật theo logic nghiệp vụ mới,  
> **Để** đảm bảo hệ thống đạt tỷ lệ kiểm thử thành công 100% và giải pháp biên dịch 0 lỗi 0 cảnh báo.

#### Scenario 4.1: Kiểm thử luồng hủy đơn có hoàn tiền
- Cập nhật test cũ `CancelOrderAsync_ShouldThrowException_WhenOrderIsAlreadyPaid` thành `CancelOrderAsync_ShouldCancelOrderAndRefund_WhenOrderIsPaidAndNotShipped`.
- Thêm test mới `CancelOrderAsync_ShouldThrowException_WhenSubOrderIsAlreadyShipped`.
- Xác thực `_walletClientMock.Verify(RefundSubOrderPaymentAsync)` và `_fulfillmentClientMock.Verify(ReleaseStockAsync)` được gọi chính xác 1 lần.

---

## 3. Domain & Technical Constraints

1. **Zero Database Migration**: `ShippingAddressSnapshot` được lưu trữ dưới dạng Native `JSONB` trong PostgreSQL qua EF Core 8 `.ToJson()`. Việc mở rộng `District` thành nullable hoàn toàn tương thích ngược, không yêu cầu sinh migration mới.
2. **Idempotency Guarantee**: Mã hoàn tiền ví `idempotencyKey` bắt buộc tuân thủ quy tắc định danh duy nhất: `$"refund-suborder-cancel-{so.Id}"`.
3. **No-Repository Principle**: Toàn bộ thao tác truy vấn và lưu trữ tiếp tục sử dụng trực tiếp `ICommerceDbContext` / `AppDbContext`.
4. **Universal Error Rules**: 100% thông báo lỗi bằng tiếng Anh Sentence-case, mã lỗi `SCREAMING_SNAKE_CASE`.
