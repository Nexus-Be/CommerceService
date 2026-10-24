# 03 — SỰ KIỆN BẤT ĐỒNG BỘ QUA RABBITMQ (COMMERCE SERVICE EVENT MESSAGING)
> **Trạng thái triển khai (2026-10-24):** ❌ Chưa có nghiệp vụ — 0/3 event publish, 0/4 consumer; chỉ có **hạ tầng** MassTransit 8.3.0 + RabbitMQ + EF Outbox được đăng ký trong DI.
> **Nguồn sự thật:** code trong repo này (`file:line` ghi kèm trong từng bảng).
> **Cập nhật lần cuối:** 2026-10-24.
CommerceService **được thiết kế** là **Trung Tâm Điều Phối Saga (Saga Orchestrator)** cho luồng đặt hàng toàn sàn. ⚠️ Hiện tại **chưa có** `IPublishEndpoint`, `IConsumer<>`, `IRequestClient`, `IBus` hay `Publish(...)` nào trong mã nguồn (grep toàn repo = 0 kết quả); chưa có State Machine của MassTransit.
### 0.1. Hạ tầng messaging thật đang có
| Hạng mục | Trạng thái | Bằng chứng |
|---|---|---|
| Package `MassTransit` + `.RabbitMQ` + `.EntityFrameworkCore` | ✅ CÓ — version **8.3.0** (không phải 8.4.1) | `Commerce.Infrastructure/Commerce.Infrastructure.csproj:22-24` |
| `AddMassTransit` + `UsingRabbitMq` + `ConfigureEndpoints` | ✅ CÓ | `Commerce.Infrastructure/DependencyInjection.cs:23-48` |
| Entity Framework Outbox (`AddEntityFrameworkOutbox<AppDbContext>`, `UsePostgres`, `UseBusOutbox`) | ✅ CÓ | `Commerce.Infrastructure/DependencyInjection.cs:27-31` |
| Inbox/Outbox table mapping (`AddInboxStateEntity`, `AddOutboxMessageEntity`, `AddOutboxStateEntity`) | ✅ CÓ | `Commerce.Infrastructure/Persistence/AppDbContext.cs:38-40` |
| Publisher / Consumer / Saga State Machine | ❌ CHƯA CÓ | không có class nào kế thừa `IConsumer<>` / `MassTransitStateMachine<>` |
Ghi chú cấu hình: broker lấy từ `RabbitMq:Host|Port|User|Password` (`DependencyInjection.cs:35-38`, `Commerce.Api/appsettings.json:12-17`), mặc định `localhost:5672`.
---
## 1. SỰ KIỆN PHÁT HÀNH (EVENTS PUBLISHED)
Đặc tả mục tiêu (chưa triển khai): 0/3 event dưới đây tồn tại trong code.
| # | Tên Sự Kiện | Thời Điểm Kích Hoạt | Payload Dữ Liệu | Services Quan Tâm | Trạng thái | Bằng chứng |
|---|---|---|---|---|---|---|
| 1 | `OrderSubmittedEvent` | Khách gửi request tạo đơn hàng thành công | `OrderId`, `UserId`, `Items: [{ ProductId, Quantity }]`, `TotalAmount`, `CreatedAt` | `CatalogService` (Giữ kho ảo), `LogService` | ❌ CHƯA CÓ | không có class event; `OrderService` không có `CreateOrder`/`Publish` |
| 2 | `OrderPaidEvent` | Nhận được xác nhận thanh toán thành công từ ví | `OrderId`, `TransactionId`, `PaidAt` | `FulfillmentService` (Tạo phiếu xuất kho), `NotificationService` (Báo khách) | ❌ CHƯA CÓ | không có class event |
| 3 | `OrderCancelledEvent` | Đơn hàng bị hủy do hết kho hoặc thanh toán lỗi | `OrderId`, `Reason`, `CancelledAt` | `CatalogService` (Nhả lại kho nếu trước đó đã giữ), `NotificationService` | ❌ CHƯA CÓ | không có class event |
> ❌ `OrderSubmittedEvent`/`IOrderSubmittedEvent` **không tồn tại** trong repo này. Đây là mắt xích còn thiếu của Saga đặt hàng toàn hệ thống — xem `Commerce_AUDIT.md` mục CO-02.
---
## 2. SỰ KIỆN LẮNG NGHE (CONSUMERS TRONG SAGA)
Đặc tả mục tiêu (chưa triển khai): 0/4 consumer được đăng ký.
| # | Consumer (đặc tả) | Nguồn phát | Hành vi Saga mong muốn | Trạng thái | Bằng chứng |
|---|---|---|---|---|---|
| 1 | `StockReservedEvent` | `CatalogService` | Saga chuyển `Submitted` → `AwaitingPayment`; bắn `ProcessWalletPayment` / yêu cầu `WalletService` trừ tiền | ❌ CHƯA CÓ | không có `IConsumer<>` |
| 2 | `StockReservationFailedEvent` | `CatalogService` | Saga → `Faulted`, cập nhật đơn thành `Cancelled` (lý do "Sản phẩm đã hết hàng") | ❌ CHƯA CÓ | không có `IConsumer<>` |
| 3 | `PaymentCompletedEvent` | `WalletService` | Saga → `Completed`, cập nhật đơn thành `Paid` / `Confirmed` | ❌ CHƯA CÓ | không có `IConsumer<>` |
| 4 | `PaymentFailedEvent` | `WalletService` | Bù trừ: đơn → `Cancelled` (lý do "Số dư ví không đủ"), yêu cầu `CatalogService` nhả hàng đã giữ | ❌ CHƯA CÓ | không có `IConsumer<>` |
---
## 3. GHI CHÚ TRIỂN KHAI
- Trạng thái Saga trong đặc tả cũ (`OrderSagaState`) **không có bảng và không có entity** tương ứng: 16 entity hiện tại không có `OrderSagaState` (xem `04_DATA_PERSISTENCE.md`).
- Hạ tầng Outbox đã sẵn sàng cho việc publish nguyên tử cùng transaction, nên khi bổ sung event chỉ cần inject `IPublishEndpoint` vào `OrderService` và gọi publish **trước/sau** `SaveChangesAsync` theo đúng mẫu Outbox — hiện `SaveChangesAsync` còn 0 lời gọi (`ICommerceDbContext.cs:27`).
- Không có `Commerce.Tests` ⇒ không có test nào cho luồng event/Saga.