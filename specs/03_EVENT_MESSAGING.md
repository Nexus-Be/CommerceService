# 03 — SỰ KIỆN BẤT ĐỒNG BỘ QUA RABBITMQ (COMMERCE SERVICE EVENT MESSAGING)

CommerceService là **Trung Tâm Điều Phối Saga (Saga Orchestrator)** cho luồng đặt hàng toàn sàn.

---

## 1. SỰ KIỆN PHÁT HÀNH (EVENTS PUBLISHED)

| Tên Sự Kiện | Thời Điểm Kích Hoạt | Payload Dữ Liệu | Services Quan Tâm |
|:---|:---|:---|:---|
| `OrderSubmittedEvent` | Khách gửi request tạo đơn hàng thành công | `OrderId`, `UserId`, `Items: [{ ProductId, Quantity }]`, `TotalAmount`, `CreatedAt` | `CatalogService` (Giữ kho ảo), `AuditService`. |
| `OrderPaidEvent` | Nhận được xác nhận thanh toán thành công từ ví | `OrderId`, `TransactionId`, `PaidAt` | `FulfillmentService` (Tạo phiếu xuất kho), `NotificationService` (Báo khách). |
| `OrderCancelledEvent` | Đơn hàng bị hủy do hết kho hoặc thanh toán lỗi | `OrderId`, `Reason`, `CancelledAt` | `CatalogService` (Nhả lại kho nếu trước đó đã giữ), `NotificationService`. |

---

## 2. SỰ KIỆN LẮNG NGHE (CONSUMERS TRONG SAGA)

1. **`StockReservedEvent`** (Từ `CatalogService`):
   - Saga chuyển trạng thái từ `Submitted` sang `AwaitingPayment`.
   - Bắn tiếp lệnh `ProcessWalletPayment` hoặc phát event yêu cầu `WalletService` trừ tiền.

2. **`StockReservationFailedEvent`** (Từ `CatalogService`):
   - Saga chuyển trạng thái sang `Faulted` $\rightarrow$ Cập nhật đơn hàng thành `Cancelled` với lý do "Sản phẩm đã hết hàng".

3. **`PaymentCompletedEvent`** (Từ `WalletService`):
   - Saga chuyển trạng thái sang `Completed` $\rightarrow$ Cập nhật đơn hàng thành `Paid` / `Confirmed`.

4. **`PaymentFailedEvent`** (Từ `WalletService`):
   - Saga kích hoạt bù trừ $\rightarrow$ Cập nhật đơn hàng thành `Cancelled` (Lý do: Số dư ví không đủ) $\rightarrow$ Bắn event yêu cầu `CatalogService` nhả lại số lượng hàng đã giữ trước đó.
