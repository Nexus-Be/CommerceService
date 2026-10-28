# 01 — PHÂN TẦNG CLEAN ARCHITECTURE & QUY TẮC PHỤ THUỘC (COMMERCE SERVICE)
> **Trạng thái triển khai (2026-10-24):** ⚠️ Một phần — 4/4 project và luật phụ thuộc ĐÚNG, nhưng tầng Application mới có 8 file / 199 LOC (3 hàm đọc); chưa có CQRS, chưa có Saga State Machine, chưa có gRPC server.
> **Nguồn sự thật:** code trong repo này (`file:line` ghi kèm trong từng bảng).
> **Cập nhật lần cuối:** 2026-10-24.
Solution `Commerce.sln` bao gồm 4 project tương ứng với 4 tầng kiến trúc sạch (Clean Architecture):
```
Commerce.sln
├── Commerce.Domain/           # Entities (Cart, CartItem, Order, SubOrder, SubOrderItem...), Enums, DomainException
├── Commerce.Application/      # Use cases dạng Service (thư mục Services/Cart, Services/Order), DTOs, ICommerceDbContext
├── Commerce.Infrastructure/   # AppDbContext, 16 Fluent API Configurations, Migrations, Seeds, MassTransit + EF Outbox
└── Commerce.Api/              # Host tối giản (Program.cs, Middleware/GlobalExceptionHandlerMiddleware.cs)
```
| Project | Trạng thái | Ghi chú thực tế |
|---|---|---|
| `Commerce.Domain` | ✅ CÓ | 16 entity, 18 enum, 3 class exception (`Commerce.Domain/Exceptions/DomainException.cs:3,9,15`); 0 package ngoài (`Commerce.Domain/Commerce.Domain.csproj` không có `PackageReference`) |
| `Commerce.Application` | ⚠️ MỘT PHẦN | 8 file / 199 LOC; 2 service (`CartService`, `OrderService`), 3 hàm đọc; **không** có `Commands/`, `Queries/`, `CheckoutSaga` |
| `Commerce.Infrastructure` | ✅ CÓ | `AppDbContext` + 16 `DbSet` + 16 configuration + 1 migration + seed data; MassTransit/RabbitMQ/Outbox đăng ký |
| `Commerce.Api` | ⚠️ MỘT PHẦN | có `Program.cs` (Kestrel HTTP/1+HTTP/2, Swagger, `/health`) và middleware lỗi; **không** có `Services/`, `Contracts/`, không `MapGrpcService` |
---
## 1. NGUYÊN TẮC LUỒNG PHỤ THUỘC (DEPENDENCY RULE)
```
[ Commerce.Api ] ────────────┐
                             │
                             ▼
               [ Commerce.Application ] ──▶ [ Commerce.Domain ]
                             ▲
                             │
[ Commerce.Infrastructure ] ─┘
```
Luật này **đang được tuân thủ trong code** (đối chiếu `*.csproj`):
- `Commerce.Application` → tham chiếu `Commerce.Domain` (`Commerce.Application/Commerce.Application.csproj:10`).
- `Commerce.Infrastructure` → tham chiếu `Commerce.Application` + `Commerce.Domain` (`Commerce.Infrastructure/Commerce.Infrastructure.csproj:4-5`).
- `Commerce.Api` → tham chiếu `Commerce.Application` + `Commerce.Infrastructure` (`Commerce.Api/Commerce.Api.csproj:10-11`).
- **`Commerce.Domain`**: Chứa các Entity gốc `Order`, `SubOrder`, `SubOrderItem` (⚠️ **không có** entity tên `OrderLine` — tên thật là `SubOrderItem`, xem `Commerce.Domain/Entities/SubOrderItem.cs:6`), `Cart`, `CartItem`, enum trạng thái `OrderStatus`/`SubOrderStatus`.
  - ⚠️ Đặc tả yêu cầu "nghiệp vụ chuyển đổi trạng thái đặt trong Domain Entity (ví dụ `order.MarkAsStockReserved()`, `order.Cancel(string reason)`)" — ❌ **chưa triển khai**: cả 16 entity chỉ có property, không có method nghiệp vụ.
- **`Commerce.Application`**: Chứa các ca sử dụng đọc giỏ/đọc đơn. Khai báo interface `ICommerceDbContext` (`Commerce.Application/Interfaces/ICommerceDbContext.cs:8-28`). ⚠️ **Không** có `CheckoutSaga` và **không** có định nghĩa State Machine (xem `03_EVENT_MESSAGING.md`).
- **`Commerce.Infrastructure`**: Chứa `AppDbContext` (`Commerce.Infrastructure/Persistence/AppDbContext.cs:8`). ⚠️ **Không có** bảng/entity `OrderSagaState` — tên này không tồn tại trong repo; Outbox/Inbox table được map qua `AddInboxStateEntity`/`AddOutboxMessageEntity`/`AddOutboxStateEntity` (`AppDbContext.cs:38-40`).
- **`Commerce.Api`**: Điểm khởi động host. ⚠️ Đặc tả `app.MapGrpcService<CommerceGrpcService>()` **chưa có** — class `CommerceGrpcService` **không tồn tại**; `Program.cs` chỉ có `app.MapGet("/health", ...)` (`Commerce.Api/Program.cs:58`).
---
## 2. QUY CHUẨN CODE TỪNG TẦNG
1. **`Commerce.Domain`**:
   - `Order` quản lý danh sách `OrderLine` → ⚠️ **sai tên**: `Order` chứa `ICollection<SubOrder> SubOrders` (`Commerce.Domain/Entities/Order.cs:20`); dòng hàng là `SubOrderItem` nằm dưới `SubOrder` (`Commerce.Domain/Entities/SubOrder.cs:38`).
   - Tính toán tổng tiền `TotalAmount = sum(UnitPrice * Quantity)` "được đóng gói an toàn trong entity" — ❌ **chưa triển khai**: `TotalAmount` là property `decimal` gán từ ngoài (`Order.cs:10`, `SubOrder.cs:23`), không có hàm tính.
   - Trạng thái đơn chỉ được chuyển đổi hợp lệ: `Pending` → `StockReserved` → `Paid` → `Preparing` → `Shipped` → `Completed`, và `Cancelled` trước `Shipped` — ❌ **chưa triển khai**: không có state machine/guard nào; enum thật là `OrderStatus` (5 giá trị) và `SubOrderStatus` (10 tên nhưng chỉ 8 giá trị do alias trùng — xem mục 2.4).
2. **`Commerce.Application`**:
   - Giới hạn dòng hàng: Tối đa 50 dòng (`MaxCheckoutLines`), số lượng mỗi dòng 1..1000 (`MaxCheckoutQuantity`) — ❌ **không tồn tại**: grep `MaxCheckout` = 0 kết quả. Đây là đặc tả mục tiêu.
   - Số lượng đọc thật: 3 hàm (2 trong `OrderService`, 1 trong `CartService`); `SaveChangesAsync` được khai báo ở `ICommerceDbContext.cs:27` nhưng **0 lời gọi** trong toàn repo.
3. **`Commerce.Infrastructure`**:
   - Đặc tả "Khóa ngoại `OrderLine.OrderId` cascade delete khi xóa đơn nháp" — ⚠️ **không đúng với code**: FK `SubOrderItem.SubOrderId` khai báo **không** set `OnDelete` (`Commerce.Infrastructure/Persistence/Configurations/SubOrderItemConfiguration.cs:20-22`, mặc định EF = `Cascade` cho required FK); các quan hệ chính dùng `DeleteBehavior.Restrict` (`OrderConfiguration.cs:23,38`, `SubOrderConfiguration.cs:56`, `AuctionOrderConfiguration.cs:43`) hoặc `SetNull` (`OrderConfiguration.cs:28,33`).
   - Đánh Index trên `Order.UserId`, `Order.Status`, `Order.CreatedAt`, `Cart.UserId` — ❌ **chưa có**: `OrderConfiguration.cs` không khai báo `HasIndex` nào; `CartConfiguration.cs` cũng vậy. Index thật đang có: unique `DiscountCode.Code` (`DiscountCodeConfiguration.cs:13`), unique `SubOrder.SubOrderNo` (`SubOrderConfiguration.cs:13`), unique `AuctionOrder.OrderNo`, unique `CartItem(CartId, SkuId)` + index `CartItem.ProductId` (`CartItemConfiguration.cs:15-16`), unique `OrderInvoice.InvoiceNo`, unique `SystemSetting.SettingKey`.
   - Toàn bộ 16 configuration có global query filter `HasQueryFilter(x => !x.IsDeleted)`.
4. **Cảnh báo enum alias (bug logic thật)** — ⚠️ ba enum khai báo **hai tên cho cùng một giá trị**, trong khi tầng persistence lưu **tên** enum bằng `HasConversion<string>()` (24 chỗ):
   - `SubOrderStatus`: `Confirmed = 2` và `SellerAccepted = 2`; `Shipping = 3` và `Shipped = 3` (`Commerce.Domain/Enums/SubOrderStatus.cs:7-10`).
   - `DiscountType`: `Percent = 0`/`Percentage = 0`, `Fixed = 1`/`FixedAmount = 1` (`Commerce.Domain/Enums/DiscountType.cs:5-8`).
   - `DiscountScopeType`: `System = 0`/`Global = 0` (`Commerce.Domain/Enums/DiscountScopeType.cs:5-6`).
   - Hệ quả: so sánh trong C# (`x.Status == SubOrderStatus.Confirmed`) đúng cả với `SellerAccepted`; nhưng khi ghi/đọc qua `HasConversion<string>()` cột lưu chuỗi tên (`"Confirmed"` vs `"SellerAccepted"`, `"Shipping"` vs `"Shipped"`) nên truy vấn `Where(x => x.Status == SubOrderStatus.Shipped)` **không khớp** bản ghi đã lưu là `"Shipping"`. Cần chọn một tên chuẩn cho mỗi giá trị trước khi viết nghiệp vụ đơn hàng.

---

## 5. QUY CHUẨN CẤU TRÚC CHUẨN HÓA (SERVICE BLUEPRINT)
1. **Giao tiếp đồng bộ (RESTful)**:
   - `Commerce.Api/Controllers/`: Thin Dispatcher Controller, chỉ nhận request, gọi Service Interface trong Application, trả về `IActionResult`. Tuyệt đối cấm logic nghiệp vụ, cấm map anonymous object, cấm khai báo class DTO trong file controller.
   - `Commerce.Application/Services/{Feature}/`: Mỗi feature (`Cart`, `Order`, `Checkout`...) chứa `I{Feature}Service.cs`, `{Feature}Service.cs`, và Request/Response DTOs dùng chung cho Controller.
2. **Giao tiếp bất đồng bộ (RabbitMQ MassTransit)**:
   - `Commerce.Application/Contracts/`: Chứa các Integration Events (ví dụ: `OrderCreatedIntegrationEvent`, `OrderCancelledIntegrationEvent`).
   - `Commerce.Infrastructure/Consumers/`: Chứa các MassTransit Consumer và Saga State Machine xử lý luồng checkout phân tán.
3. **Quản lý Dependency Injection**:
   - `Commerce.Application/DependencyInjection.cs`: Đăng ký toàn bộ Application Services.
   - `Commerce.Infrastructure/DependencyInjection.cs`: Đăng ký `CommerceDbContext`, MassTransit Bus, Outbox, Consumers.
4. **Validation**:
   - Input format validation đặt tại Request DTOs qua Data Annotations (`[Required]`, `[Range]`...).
   - Business validation đặt tại Application Services hoặc Domain Exceptions.
