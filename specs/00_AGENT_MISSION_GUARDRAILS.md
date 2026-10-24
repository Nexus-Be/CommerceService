# 00 — NHIỆM VỤ CỐT LÕI & RÀO CHẮN CHỐNG ẢO GIÁC (COMMERCE SERVICE)
> **Trạng thái triển khai (2026-10-24):** ❌ Một phần rất nhỏ — 16/16 bảng có entity + DbSet, 3 truy vấn đọc, 0/10 rpc gRPC, 0/3 event, 0 test.
> **Nguồn sự thật:** code trong repo này (`file:line` ghi kèm trong từng bảng).
> **Cập nhật lần cuối:** 2026-10-24.
---
## 1. NHIỆM VỤ & RANH GIỚI MIỀN (BOUNDED CONTEXT)
**CommerceService** được giao trách nhiệm độc quyền về việc quản lý giỏ hàng và vòng đời đơn hàng thương mại điện tử. Dưới đây là **nhiệm vụ thiết kế** kèm trạng thái thật của từng phần trong code:
| # | Nhiệm vụ (đặc tả) | Trạng thái | Bằng chứng |
|---|---|---|---|
| 1 | Quản lý giỏ hàng (`Cart`, `CartItem`): thêm, bớt, cập nhật số lượng, xóa sản phẩm, xóa trắng giỏ | ⚠️ MỘT PHẦN — chỉ **đọc** giỏ đang `Active`; không có lệnh ghi | `Commerce.Application/Services/Cart/CartService.cs:19-44` (1 hàm duy nhất) |
| 2 | Tạo đơn hàng (`CreateOrder`): Mua ngay (`Items`) hoặc Checkout toàn bộ giỏ (`CartId`) | ❌ CHƯA CÓ | `OrderService` chỉ có `GetByIdAsync` + `ListByBuyerIdAsync` (`Commerce.Application/Services/Order/OrderService.cs:20,36`) |
| 3 | Quản lý vòng đời đơn hàng: `Pending` → `StockReserved` → `Paid` → `Preparing` → `Shipped` → `Completed` / `Cancelled` | ❌ CHƯA CÓ — chỉ có **enum** `OrderStatus`/`SubOrderStatus`, không có hàm chuyển trạng thái | `Commerce.Domain/Enums/OrderStatus.cs:3-10`, `Commerce.Domain/Enums/SubOrderStatus.cs:3-15` |
| 4 | **Đầu Mối Điều Phối Checkout Saga** (giữ kho `CatalogService`, trừ tiền `WalletService`) | ❌ CHƯA CÓ — hạ tầng MassTransit 8.3.0 + Outbox đã đăng ký nhưng 0 state machine, 0 publisher, 0 consumer | `Commerce.Infrastructure/DependencyInjection.cs:23-48`; xem `03_EVENT_MESSAGING.md` |
| 5 | Cung cấp dịch vụ gRPC Server tại cổng `8003` cho ApiGateway | ❌ CHƯA CÓ — không có file `.proto`, không có `*GrpcService.cs`, không có package gRPC | bề mặt thật chỉ có `GET /health` (`Commerce.Api/Program.cs:58`) |
Ghi chú vòng đời thật: `Order` dùng `OrderStatus` (`PendingPayment=0`, `PaymentProcessing=1`, `Paid=2`, `Cancelled=3`, `Expired=4`) — **không có** `Pending`/`StockReserved`/`Preparing`/`Shipped` như đặc tả; các trạng thái đó nằm ở `SubOrderStatus`.
---
## 2. NHỮNG ĐIỀU CẤM KỴ ĐỐI VỚI AI AGENTS (ANTI-HALLUCINATION GUARDRAILS)
1. **TUYỆT ĐỐI KHÔNG DÙNG REPOSITORY PATTERN BỌC NGOÀI EF CORE**:
   - Sử dụng trực tiếp `AppDbContext` (hoặc interface `ICommerceDbContext`) và `DbSet<T>` trong tầng Application.
   - Không được tạo `IOrderRepository`, `ICartRepository`, `UnitOfWork`.
   - ✅ Hiện trạng tuân thủ: `CartService`/`OrderService` dùng `ICommerceDbContext` trực tiếp, 0 repository.
2. **KHÔNG TỰ Ý TRỪ TIỀN HOẶC TRỪ KHO BÊN TRONG COMMERCE**:
   - `CommerceService` **KHÔNG** sở hữu kho hàng và **KHÔNG** sở hữu số dư tiền của khách.
   - Việc kiểm tra/giữ kho do `CatalogService` xử lý. Việc trừ số dư do `WalletService` xử lý. Commerce chỉ đóng vai trò gửi Request/Event và chờ phản hồi từ 2 service kia qua Saga.
3. **BẮT BUỘC KIỂM TRA IDEMPOTENCY-KEY KHI TẠO ĐƠN**:
   - Mọi request tạo đơn bắt buộc mang `Idempotency-Key` (UUID). Nếu trùng key trong vòng 24h, phải trả về đơn hàng đã tạo trước đó hoặc báo lỗi xung đột, không được tạo 2 đơn trùng lặp.
   - ❌ **Chưa triển khai**: grep `Idempotency` trong toàn repo = 0 kết quả.
4. **TUYỆT ĐỐI KHÔNG THAY ĐỔI CỔNG 8003 VÀ DATABASE 5435**:
   - Port **container/Docker** là `8003` (✅ khớp: `Dockerfile:3-4` `EXPOSE 8003` / `ASPNETCORE_URLS=http://+:8003`; `docker-compose.yml:37`).
   - ⚠️ Port **chạy local** qua `launchSettings.json` **không phải 8003**: profile `http` = `http://localhost:5105`, profile `https` = `https://localhost:7035;http://localhost:5105` (`Commerce.Api/Properties/launchSettings.json:17,27`). Đây là sai lệch đã biết (CO-05), chưa sửa vì phạm vi tài liệu chỉ cho phép sửa `.md`.
   - Chuỗi kết nối DB trỏ tới `localhost:5435/commerce_db` với mật khẩu mặc định `MatkhaucuaTui123@` (✅ khớp: `Commerce.Api/appsettings.json:10`, `Commerce.Infrastructure/DependencyInjection.cs:14-15`).