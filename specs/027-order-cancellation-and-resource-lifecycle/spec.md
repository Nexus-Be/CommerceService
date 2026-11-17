# Feature Specification: Comprehensive Order Cancellation & Resource Rollback Lifecycle

**Feature Branch**: `027-order-cancellation-and-resource-lifecycle`  
**Created**: 2026-11-17  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  
**Input**: Chuẩn hóa toàn diện mọi kịch bản Hủy đơn hàng và Thu hồi/Giải phóng tài nguyên (Tồn kho kho bãi, Voucher giảm giá, Dòng tiền ký quỹ, Điểm uy tín người bán). Khóa chặt tính năng hoàn tiền sau giao hàng thành công.

---

## 1. Executive Summary & Core Invariants

Đặc tả này quy định chi tiết vòng đời Hủy đơn hàng và xử lý tài nguyên liên quan tại `CommerceService`:

1. **Phạm vi Hủy đơn được phép (Valid Cancellation Window)**:
   - Khách hàng và Người bán **chỉ có quyền hủy đơn hàng trước khi hàng được xuất kho vận chuyển** (trạng thái `PendingPayment`, `Paid`, `Confirmed`).
   - Một khi đơn con đã chuyển sang `Shipping` (Đang giao), `Delivered` (Đã giao), hoặc `Completed` (Hoàn tất) $\rightarrow$ **Khóa 100% quyền hủy đơn**. Mọi nỗ lực gọi API hủy đều trả về HTTP 400 `ORDER_CANNOT_BE_CANCELLED`.
2. **Khóa chức năng Hoàn tiền sau khi Giao thành công (No Post-Delivery Refund)**:
   - Khi Shipper cập nhật đơn sang `Delivered`, khách hàng chỉ có thể xác nhận đã nhận hàng (hoặc hệ thống tự động hoàn tất sau 3 ngày). Tiền ký quỹ chuyển cho Người bán.
   - **Hệ thống không hỗ trợ luồng hoàn tiền cho Người mua sau khi đã giao hàng thành công**.
3. **Nguyên tắc Bảo toàn Ngân quỹ với Đơn COD**:
   - Khi đơn COD bị hủy trước giao hoặc do shipper hoàn hàng (`Returned`), hệ thống **tuyệt đối không gọi sang WalletService** vì khách chưa từng trả trước tiền mặt.
4. **Cơ chế Hủy do Quá hạn thanh toán (Payment Timeout Cancellation)**:
   - Background worker quét định kỳ các đơn `PendingPayment` quá 15 phút.
   - Gom mảng `orderIds` (tối đa 50 đơn/lần quét) gửi sang Fulfillment Service trong **1 request duy nhất** (`BatchReleaseStockAsync`) để nhả kho hàng loạt.
   - Batch giải phóng Voucher (`Reserved` $\rightarrow$ `Released`).
   - **Không gọi WalletService** vì tiền chưa từng bị trừ.

---

## 2. Detailed Cancellation Scenarios

### Kịch bản A: Người mua hủy Đơn hàng mẹ (Master Order Cancellation)
* **Kích hoạt**: `POST /api/v1/orders/{orderId}/cancel` bởi Buyer.
* **Điều kiện**: Đơn ở `PendingPayment` hoặc `Paid`, chưa có shop nào chuyển sang `Shipping`.
* **Hành động**:
  1. `Order.Status = Cancelled`, tất cả các `SubOrder` chuyển sang `Cancelled`.
  2. Dòng tiền:
     - Nếu `PaymentMethod.Wallet && WasPaid`: Gọi duy nhất 1 lần `POST /api/v1/wallets/orders/{orderId}/refund` với số tiền `order.TotalAmount`.
     - Nếu `PaymentMethod.Cod`: Không gọi Wallet.
  3. Kho bãi: Gọi `_fulfillmentClient.ReleaseStockAsync(order.Id)`.
  4. Voucher: Nhả toàn bộ voucher đã áp dụng cho đơn hàng (`DiscountCodeUsages.Status = Released`).

### Kịch bản B: Người mua hoặc Shop hủy Đơn hàng con lẻ (SubOrder Cancellation)
* **Kích hoạt**: `POST /api/v1/sub-orders/{subOrderId}/cancel` (Buyer) hoặc `POST /api/v1/sub-orders/{subOrderId}/reject` (Seller).
* **Điều kiện**: `SubOrder.Status` thuộc `PendingPayment`, `Paid`, hoặc `Confirmed`.
* **Hành động**:
  1. Nếu Seller tự ý hủy đơn đã xác nhận/đã thanh toán không lý do: Gọi UserService trừ **5 điểm uy tín Seller**.
  2. `SubOrder.Status = Cancelled`.
  3. Dòng tiền:
     - Nếu `PaymentMethod.Wallet && WasPaid`: Gọi `POST /api/v1/wallets/sub-orders/{subOrderId}/refund` với số tiền `subOrder.TotalAmount`.
     - Nếu COD: Không gọi Wallet.
  4. Kho bãi: Chỉ nhả tồn kho của các sản phẩm thuộc Shop đó qua `_fulfillmentClient.ReleaseSubOrderStockAsync`.
  5. Đồng bộ trạng thái Đơn hàng mẹ: Nếu tất cả các đơn con đều `Cancelled` $\rightarrow$ `Order.Status = Cancelled`.

### Kịch bản C: Hệ thống hủy do Quá hạn thanh toán (Payment Timeout)
* **Kích hoạt**: `PaymentTimeoutWorker` chạy nền mỗi 60 giây.
* **Điều kiện**: `Order.Status == PendingPayment && PaymentDeadlineAt < Now`.
* **Hành động**:
  1. Lấy mảng `orderIds` (tối đa 50 đơn).
  2. Gửi 1 mảng sang kho: `_fulfillmentClient.BatchReleaseStockAsync(orderIds)`.
  3. Nhả voucher hàng loạt trong DB (`Reserved` $\rightarrow$ `Released`).
  4. Cập nhật `Status = Cancelled` cho toàn bộ 50 đơn bằng 1 lệnh `SaveChangesAsync()`.
  5. **Tuyệt đối không gọi sang Wallet**.

### Kịch bản D: Giao hàng thất bại - Hàng hoàn về kho (Package Returned)
* **Kích hoạt**: Webhook giao hàng cập nhật trạng thái `Returned` từ bưu tá / đơn vị vận chuyển.
* **Điều kiện**: SubOrder đang ở trạng thái `Shipping`.
* **Hành động**:
  1. `SubOrder.Status = Cancelled`, lý do `CancellationReasonCode.PackageReturned`.
  2. Dòng tiền:
     - Nếu `PaymentMethod.Wallet && WasPaid`: Tự động gọi `_walletClient.RefundSubOrderPaymentAsync` hoàn tiền ví cho Buyer.
     - Nếu `PaymentMethod.Cod`: Bỏ qua, không gọi Wallet.
  3. Kho bãi: Đánh dấu `InventoryReleased = true`, cập nhật nhập lại hàng vào kho của Shop.

---

## 3. Comparison Matrix: Trước vs Sau khi Chuẩn hóa

| Tiêu chí | Trước khi Chuẩn hóa | Sau khi Chuẩn hóa |
| :--- | :--- | :--- |
| **Hủy đơn mẹ (Master Order)** | Chạy vòng lặp gọi nhiều lần endpoint đơn con `/sub-orders/{id}/refund` | Gọi duy nhất 1 lần endpoint đơn mẹ `/orders/{id}/refund` khớp với `OrderEscrow` |
| **Hủy đơn quá hạn thanh toán** | Quét DB và nhả kho | Giữ nguyên cơ chế quét batch 50 đơn, gửi 1 mảng sang Fulfillment, **khẳng định 100% không gọi Wallet** |
| **Đơn sau khi giao thành công (`Delivered`)** | Còn chưa rõ ràng về việc trả hàng | **Đóng băng hoàn toàn**: Cấm hủy đơn, cấm hoàn tiền tự động cho khách sau khi đã giao hàng |
| **Đơn COD khi hàng hoàn (`Returned`)** | Kiểm tra lỏng lẻo `PaidAt.HasValue` | Kiểm tra nghiêm ngặt `PaymentMethod == PaymentMethod.Wallet`. COD cấm 100% gọi Wallet |
