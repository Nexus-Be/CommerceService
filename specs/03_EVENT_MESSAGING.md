# 03 — QUY CHUẨN GIAO TIẾP VÀ BÃI BỎ SAGA & MESSAGE BROKER (COMMERCE SERVICE)

> **Trạng thái triển khai (2026-10-30):** 🛑 ĐÃ BÃI BỎ HOÀN TOÀN RABBITMQ, MASSTRANSIT & SAGA — Toàn bộ cơ chế Saga State Machine, Outbox tables và Event Consumers đã được bãi bỏ. Quá trình thanh toán và đặt hàng được điều phối trực tiếp qua các cuộc gọi REST API đồng bộ có xử lý bù trừ và timeout.
> **Nguồn sự thật:** code (`Commerce.Api/Program.cs`).
> **Cập nhật lần cuối:** 2026-10-30.

---

## 1. NGUYÊN TẮC BÃI BỎ MESSAGE BROKER & SAGA

1. **Gỡ bỏ hoàn toàn MassTransit**:
   - Toàn bộ package `MassTransit` và `MassTransit.EntityFrameworkCore` đã được loại bỏ.
   - Không còn bảng `OrderSagaState` hay các entity event trong mã nguồn.

2. **Chuyển sang Điều Phối Đồng Bộ Qua Typed REST Clients**:
   - Sau khi giữ kho, `CommerceService` gọi trực tiếp `WalletService` qua REST client `IWalletClient` (`POST /api/v1/internal/wallet/orders/{orderId}/payment`), chuyển tiếp bearer token của người mua và chỉ gửi `amount` trong body.
   - `orderId` là khóa idempotency của Wallet; giữ nguyên order ID, buyer token và amount khi retry. Chỉ `200 OK` xác nhận thanh toán; giữ nguyên status và error code khi Wallet trả lỗi.
   - Khi cần giữ kho, gọi trực tiếp `CatalogService` qua `ICatalogClient` (`POST /api/v1/products/reserve`).
   - Nhờ đó, hệ thống không còn rủi ro message bị thất lạc trong hàng đợi RabbitMQ hay xung đột trạng thái Saga phức tạp.
