# COMMERCE SERVICE — ARCHITECTURE & FOLDER STRUCTURE GUIDE
## (CẨM NANG CẤU TRÚC THƯ MỤC & PHÁT TRIỂN DÀNH CHO DEVELOPER VÀ AI AGENT)
> **Trạng thái triển khai (2026-10-24):** ❌ Một phần rất nhỏ — 16/16 bảng có entity + migration, 3 hàm đọc trong Application (8 file / 199 LOC), 0/10 rpc gRPC, 0/3 event, 0 project test.
> **Nguồn sự thật:** code trong repo này (`file:line` ghi kèm trong từng bảng).
> **Cập nhật lần cuối:** 2026-10-24.
> **Thông tin dịch vụ**:
> - **Service Port**: `8003` khi chạy container/Docker (✅ `Dockerfile:3-4`). ⚠️ Chạy local qua `launchSettings.json`: `http = 5105`, `https = 7035` (`Commerce.Api/Properties/launchSettings.json:17,27`).
> - **Database**: PostgreSQL (`commerce_db`), Port máy host: `5435` (✅ `appsettings.json:10`).
> - **Nhiệm vụ chính (thiết kế)**: Giỏ hàng (Cart), Đơn hàng (Order), Mã giảm giá (DiscountCode), Điều phối Checkout Saga — ⚠️ hiện chỉ phần **schema** là có thật; nghiệp vụ và gRPC chưa triển khai.
---
## 0. BỀ MẶT THẬT ĐANG CHẠY
| Thành phần | Trạng thái | Bằng chứng |
|---|---|---|
| `GET /health` | ✅ CÓ | `Commerce.Api/Program.cs:58` |
| Swagger UI (chỉ môi trường Development) | ✅ CÓ | `Commerce.Api/Program.cs:35-39` |
| `GlobalExceptionHandlerMiddleware` (ProblemDetails-style JSON lỗi) | ✅ CÓ | `Commerce.Api/Middleware/GlobalExceptionHandlerMiddleware.cs:32-60` |
| `CartService.GetActiveCartByBuyerIdAsync` | ✅ CÓ (đọc) | `Commerce.Application/Services/Cart/CartService.cs:19` |
| `OrderService.GetByIdAsync` / `ListByBuyerIdAsync` | ✅ CÓ (đọc) | `Commerce.Application/Services/Order/OrderService.cs:20,36` |
| gRPC Server / `*.proto` | ❌ CHƯA CÓ | 0 file `.proto`, 0 package gRPC |
| Ghi dữ liệu (`SaveChangesAsync`) | ❌ CHƯA CÓ | khai báo `ICommerceDbContext.cs:27`, **0 lời gọi** |
| Publisher / Consumer / Saga State Machine | ❌ CHƯA CÓ | hạ tầng MassTransit 8.3.0 + Outbox đã đăng ký (`Commerce.Infrastructure/DependencyInjection.cs:23-48`) nhưng 0 nghiệp vụ |
| Project test | ❌ CHƯA CÓ | `Commerce.sln` chỉ có 4 project (Domain/Application/Infrastructure/Api) |
| OpenTelemetry | ❌ CHƯA CÓ | không có package OTel; `Otel:Endpoint` trong `appsettings.json:18-20` **không được đọc ở đâu** |
---
## 1. SƠ ĐỒ PHÂN TẦNG VÀ CẤU TRÚC THƯ MỤC CHUẨN
```
src/Services/Commerce/
│
├── Commerce.sln                                       # Tên solution THẬT (không phải CommerceService.sln)
│
├── Commerce.Domain/                                   # 1. TẦNG DOMAIN (POCO 100%, ZERO DEPENDENCY)
│   ├── Base/                                          # BaseEntity, IAuditable, ISoftDelete
│   ├── Entities/                                      # 16 entity: Cart, CartItem, Order, SubOrder, SubOrderItem...
│   ├── Enums/                                         # 18 enum: OrderStatus, CartStatus, PaymentMethod, SubOrderStatus...
│   └── Exceptions/DomainException.cs                  # DomainException, EntityNotFoundException, BusinessRuleValidationException
│
├── Commerce.Application/                              # 2. TẦNG APPLICATION (USE CASES & BUSINESS LOGIC) — 8 file / 199 LOC
│   ├── Interfaces/ICommerceDbContext.cs               # Abstraction của DbContext (16 DbSet + SaveChangesAsync)
│   ├── Services/Cart/                                 # ICartService.cs, CartService.cs, CartDtos.cs
│   ├── Services/Order/                                # IOrderService.cs, OrderService.cs, OrderDtos.cs
│   └── DependencyInjection.cs                         # AddApplicationServices() → đăng ký 2 service
│
├── Commerce.Infrastructure/                           # 3. TẦNG INFRASTRUCTURE (CÔNG NGHỆ & HẠ TẦNG CỤ THỂ)
│   ├── Persistence/
│   │   ├── AppDbContext.cs                            # AppDbContext (+ CommerceDbContext) triển khai ICommerceDbContext
│   │   ├── Configurations/                            # 16 Fluent API Configurations (*Configuration.cs)
│   │   ├── Migrations/                                # 1 migration: 20261024103145_InitialCommerceSchema
│   │   └── Seeds/CommerceSeedData.cs                  # Seed DiscountCode, SystemSetting, Cart, Order
│   └── DependencyInjection.cs                         # AddInfrastructureServices(configuration)
│
└── Commerce.Api/                                      # 4. TẦNG API / HOST (ENTRY POINT & MIDDLEWARE)
    ├── Middleware/GlobalExceptionHandlerMiddleware.cs # Bắt mọi lỗi ngoại lệ, format JSON lỗi
    ├── Properties/launchSettings.json                 # Port local: 5105 / 7035 (KHÔNG phải 8003)
    └── Program.cs                                     # Kestrel HTTP/1 & HTTP/2, DI, Swagger, /health
```
❌ **Không tồn tại trong repo** (đã từng được ghi sai trong tài liệu cũ): `Commerce.Api/Services/CommerceGrpcService.cs`, thư mục `Commerce.Api/Contracts/`, `Contracts/shop.proto`, thư mục `Application/Common/`, công cụ `./.tools/dotnet-ef`.
---
## 2. NGUYÊN TẮC PHÂN CHIA TRÁCH NHIỆM
1. **`Commerce.Domain`**: Chứa các Entity nghiệp vụ giỏ hàng, đơn hàng, hóa đơn, hoàn tiền. Tuyệt đối không import thư viện ngoài hay EF Core vào đây (✅ hiện trạng: `Commerce.Domain.csproj` không có `PackageReference`). ⚠️ Chưa có domain method — toàn bộ entity chỉ là property.
2. **`Commerce.Application`**: Chứa các ca sử dụng (Use Cases). Mỗi tính năng nằm trong thư mục riêng `Services/{Feature}/` gồm DTOs, Interface và Implementation. Gọi CSDL qua `ICommerceDbContext` (✅ không dùng Repository pattern). ⚠️ Hiện chỉ có 3 hàm đọc, chưa có lệnh ghi.
3. **`Commerce.Infrastructure`**: Triển khai `AppDbContext` kế thừa `ICommerceDbContext`. ⚠️ Đã đăng ký Transactional Outbox EF Core + RabbitMQ, nhưng **chưa có Saga State Machine** (0 class `MassTransitStateMachine<>`) — xem `specs/03_EVENT_MESSAGING.md`.
4. **`Commerce.Api`**: Cổng tiếp nhận cực kỳ mỏng, hiện chỉ có `/health` + Swagger. `GlobalExceptionHandlerMiddleware` chuẩn hóa lỗi (mã `NOT_FOUND`, `BUSINESS_RULE_VIOLATION`, `DOMAIN_ERROR`, `VAL_INVALID_ARGUMENT`, `INTERNAL_SERVER_ERROR`).
---
## 3. QUY TRÌNH THÊM TÍNH NĂNG MỚI (DÀNH CHO DEV & AI AGENT)
1. **Bước 1**: Tạo Entity & Enum trong `Commerce.Domain/Entities/` và `Commerce.Domain/Enums/`.
2. **Bước 2**: Tạo Configuration trong `Commerce.Infrastructure/Persistence/Configurations/` và khai báo `DbSet<T>` trong `ICommerceDbContext` & `AppDbContext`; thêm migration bằng `dotnet ef` (global tool).
3. **Bước 3**: Tạo thư mục tính năng trong `Commerce.Application/Services/{Feature}/` gồm `I{Feature}Service.cs`, `{Feature}Service.cs`, và `{Feature}Dtos.cs`.
4. **Bước 4**: Đăng ký Service vào `Commerce.Application/DependencyInjection.cs`.
5. **Bước 5**: ⚠️ Bề mặt expose hiện chỉ có minimal API trong `Commerce.Api/Program.cs`. Muốn expose qua gRPC phải thêm package `Grpc.AspNetCore` + `Contracts/shop.proto` — **hiện chưa có**, nên bước này là việc phải làm mới, không phải "gọi service trong `CommerceGrpcService.cs`" (file đó không tồn tại).
---
## 4. QUY CHUẨN THÔNG BÁO LỖI (MANDATORY ERROR RULES)
1. **100% English**: Tuyệt đối không dùng tiếng Việt.
2. **Sentence case**: Viết hoa duy nhất chữ cái đầu (`"Order not found in the system."`).
3. **Error Code SCREAMING_SNAKE_CASE**: Ví dụ `ORDER_NOT_FOUND`, `CART_NOT_FOUND`, `ORDER_INVALID_STATUS`.
⚠️ Lưu ý: ngoại lệ hiện có trong seed data là tên/diễn giải tiếng Việt (`CommerceSeedData.cs:28` — `"Khuyến mại chào mừng thành viên mới"`); quy chuẩn trên áp dụng cho **thông báo lỗi trả về client**, không áp dụng cho dữ liệu seed.
---
## 5. CÁCH CHẠY VÀ KIỂM THỬ ĐỘC LẬP SERVICE
```bash
# 1. Bật database của Commerce Service (Port 5435):
cd src/Services/Commerce
docker compose up -d commerce-db
# 2. Tạo & Cập nhật migration (dùng dotnet-ef global tool — KHÔNG có ./.tools/dotnet-ef trong repo):
dotnet ef migrations add <TenMigration> --project Commerce.Infrastructure --startup-project Commerce.Api
dotnet ef database update --project Commerce.Infrastructure --startup-project Commerce.Api
# 3. Chạy service (local: http 5105, https 7035 — container: 8003):
dotnet run --project Commerce.Api
curl http://localhost:5105/health
```
Migration hiện có: `20261024103145_InitialCommerceSchema` (đã có sẵn, không cần tạo lại `InitialCommerceSchema`).