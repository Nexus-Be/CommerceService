# Feature Specification: Payment Self-Healing, Order Repay & Financial Reconciliation Engine

**Feature Branch**: `028-payment-retry-and-reconciliation-worker`  
**Created**: 2026-11-17  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  
**Input**: Đề xuất phát triển hoàn thiện luồng thanh toán và đối soát tài chính vừa sức sinh viên OJT: Nút bấm thanh toán lại cho đơn bị nghẽn mạng, Worker tự động quét bù đối soát, và API minh bạch dòng tiền sao kê.

---

## 1. Executive Summary & Value Proposition

Gói tính năng này giải quyết 3 bài toán thực tế về trải nghiệm người dùng và tính toàn vẹn dữ liệu tài chính (Eventual Financial Consistency) mà không yêu cầu kiến trúc hạ tầng phức tạp:

1. **Nút "Thanh toán lại" cho Đơn hàng bị nghẽn mạng (Repay Pending Order)**:
   - Cung cấp endpoint `POST /api/v1/orders/{orderId}/pay` cho phép Người mua thử lại thanh toán qua Ví khi đơn đang ở trạng thái `PendingPayment` trong thời hạn 15 phút.
2. **"Kế toán tự động" quét bù đối soát (Reconciliation & Self-Healing Worker)**:
   - Background worker tự động tìm kiếm các giao dịch bị lỗi mạng dở dang để thử lại:
     - Hoàn tiền bị treo (`OrderRefunds.Status == Requested`): Thử lại gọi hoàn tiền ví cho khách.
     - Quyết toán cho Người bán bị lỗi mạng (`SubOrder.Status == Completed && !IsEscrowSettled`): Thử lại giải ngân sang ví Shop.
3. **Minh bạch hóa Dòng tiền & Sao kê (Financial Transparency APIs)**:
   - `GET /api/v1/orders/{orderId}/refunds`: Lịch sử hoàn tiền chi tiết cho Người mua.
   - `GET /api/v1/sub-orders/{subOrderId}/financial-breakdown`: Bảng kê chi tiết doanh thu thực nhận, phí sàn, tiền ship, voucher cho Người bán.
4. **Bộ kiểm thử tự động toàn diện (Comprehensive Unit Test Suite)**:
   - Viết các test case xUnit + Moq mô phỏng toàn bộ trường hợp biên (mạng lỗi, ví hết tiền, bồi hoàn phân tán).

---

## 2. Detailed Feature Requirements

### Feature 1: API Thanh toán lại đơn hàng (`POST /api/v1/orders/{orderId}/pay`)
* **Endpoint**: `POST /api/v1/orders/{orderId}/pay` trên `OrdersController`.
* **Ràng buộc bảo mật & Nghiệp vụ**:
  1. Kiểm tra `order.BuyerId == currentUserId`.
  2. `order.Status == OrderStatus.PendingPayment` và `order.PaymentMethod == PaymentMethod.Wallet`.
  3. Còn trong hạn thanh toán 15 phút: `order.PaymentDeadlineAt > DateTimeOffset.UtcNow`.
  4. Gọi `_walletClient.PayOrderAsync(order.Id, order.BuyerId, order.TotalAmount)`.
  5. Thành công: `order.Status = OrderStatus.Paid`, `so.Status = SubOrderStatus.Paid`, chốt kho `CommitStockAsync`, dọn dẹp giỏ hàng.
  6. Thất bại (hết tiền): Trả về HTTP 400 `INSUFFICIENT_WALLET_BALANCE`.

### Feature 2: Background Worker quét bù đối soát tự động
* **Vị trí**: Mở rộng trong `PaymentTimeoutWorker.cs` chạy mỗi 5 phút:
  1. **Quét hoàn tiền dở dang**:
     - Lọc `OrderRefunds` có `Status == RefundStatus.Requested`.
     - Thử lại `_walletClient.RefundSubOrderPaymentAsync(...)` hoặc `RefundOrderPaymentAsync(...)`.
     - Nếu thành công: Cập nhật `Status = RefundStatus.Completed` và `CompletedAt = now`.
  2. **Quét quyết toán dở dang cho Shop**:
     - Thêm trường `bool IsEscrowSettled` vào entity `SubOrder` (mặc định `false`, `true` khi settle thành công hoặc là đơn COD).
     - Lọc `SubOrders` có `Status == SubOrderStatus.Completed && !IsEscrowSettled && PaymentMethod == PaymentMethod.Wallet && SellerReceiveAmount > 0`.
     - Thử lại `_walletClient.SettleSellerSubOrderAsync(...)`. Thành công $\rightarrow$ set `IsEscrowSettled = true`.

### Feature 3: API Truy vấn sao kê tài chính
1. **API Lịch sử hoàn tiền**:
   - `GET /api/v1/orders/{orderId}/refunds` (Chỉ Buyer sở hữu hoặc Admin được xem).
   - Trả về danh sách: `RefundId`, `SubOrderId`, `Amount`, `RefundType`, `Status`, `ReasonCode`, `ReasonNote`, `CompletedAt`.
2. **API Phân rã doanh thu cho Người bán**:
   - `GET /api/v1/sub-orders/{subOrderId}/financial-breakdown` (Chỉ Seller của shop đó hoặc Admin được xem).
   - Trả về: `TotalAmount`, `ItemsAmount`, `PlatformFeeAmount`, `ShippingFeeAmount`, `ShopDiscountAmount`, `SellerReceiveAmount`, `IsEscrowSettled`.

---

## 3. Implementation Phasing for OJT Students

| Giai đoạn | Nhiệm vụ cụ thể | Đánh giá năng lực |
| :--- | :--- | :--- |
| **Pha 1 (Ngày 1 - 2)** | Triển khai Feature 3: Các API xem chi tiết phân rã tài chính & lịch sử hoàn tiền (Read-only) | Quen thuộc kiến trúc Clean Architecture, DTO, EF Core LINQ. |
| **Pha 2 (Ngày 3 - 5)** | Triển khai Feature 1: API `POST /api/v1/orders/{orderId}/pay` thử thanh toán lại | Nắm vững vòng đời đơn hàng, gọi inter-service REST client. |
| **Pha 3 (Ngày 6 - 8)** | Triển khai Feature 2: Bác kế toán quét bù đối soát tự động trong Worker | Tư duy lập trình phòng thủ, tính toàn vẹn tài chính phân tán. |
| **Pha 4 (Ngày 9 - 10)** | Triển khai Feature 4: Bộ Unit Tests bao phủ 100% kịch bản biên của Ví | Nâng cao kỹ năng kiểm thử tự động, tư duy chất lượng code. |
