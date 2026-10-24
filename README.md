# COMMERCE SERVICE — ARCHITECTURE & FOLDER STRUCTURE GUIDE
## (CẨM NANG CẤU TRÚC THƯ MỤC & PHÁT TRIỂN DÀNH CHO DEVELOPER VÀ AI AGENT)

> **Thông tin dịch vụ**:
> - **Service Port**: `8003` (HTTP/1.1 REST & HTTP/2 gRPC)
> - **Database**: PostgreSQL (`commerce_db`), Port máy host: `5435`
> - **Nhiệm vụ chính**: Giỏ hàng (Cart), Đơn hàng (Order), Mã giảm giá (DiscountCode), Điều phối Checkout Saga.

---

## 1. SƠ ĐỒ PHÂN TẦNG VÀ CẤU TRÚC THƯ MỤC CHUẨN

```
src/Services/Commerce/
│
├── Commerce.Domain/                                   # 1. TẦNG DOMAIN (POCO 100%, ZERO DEPENDENCY)
│   ├── Base/                                          # BaseEntity, IAuditable, ISoftDelete
│   ├── Entities/                                      # Cart, CartItem, Order, SubOrder, DiscountCode...
│   ├── Enums/                                         # OrderStatus, CartStatus, PaymentMethod...
│   └── Exceptions/                                    # DomainException, BusinessRuleValidationException
│
├── Commerce.Application/                              # 2. TẦNG APPLICATION (USE CASES & BUSINESS LOGIC)
│   ├── Interfaces/                                    # Interfaces của CSDL và các dịch vụ hạ tầng ngoài
│   │   └── ICommerceDbContext.cs                      # Abstraction của DbContext
│   │
│   ├── Common/                                        # Tiện ích và models dùng chung trong Application
│   │
│   ├── Services/                                      # CÁC DỊCH VỤ XỬ LÝ USE CASE (THEO TÍNH NĂNG)
│   │   ├── Cart/                                      # Module Giỏ hàng
│   │   │   ├── ICartService.cs                        # Interface ca sử dụng giỏ hàng
│   │   │   ├── CartService.cs                         # Xử lý nghiệp vụ giỏ hàng
│   │   │   └── CartDtos.cs                            # CartDto, CartItemDto...
│   │   └── Order/                                     # Module Đơn hàng & Checkout
│   │       ├── IOrderService.cs                       # Interface ca sử dụng đơn hàng
│   │       ├── OrderService.cs                        # Xử lý nghiệp vụ đơn hàng
│   │       └── OrderDtos.cs                           # OrderDto, CreateOrderDto...
│   │
│   └── DependencyInjection.cs                         # Đăng ký AddApplicationServices()
│
├── Commerce.Infrastructure/                           # 3. TẦNG INFRASTRUCTURE (CÔNG NGHỆ & HẠ TẦNG CỤ THỂ)
│   ├── Persistence/                                   # Quản lý CSDL PostgreSQL & EF Core 8
│   │   ├── AppDbContext.cs                            # Triển khai ICommerceDbContext
│   │   ├── Configurations/                            # 16 Fluent API Configurations (*Configuration.cs)
│   │   ├── Migrations/                                # Code-first Database Migrations
│   │   └── Seeds/                                     # Dữ liệu hạt giống cấu hình hệ thống
│   │
│   └── DependencyInjection.cs                         # Đăng ký AddInfrastructureServices(configuration)
│
└── Commerce.Api/                                      # 4. TẦNG API / HOST (ENTRY POINT & MIDDLEWARE)
    ├── Middleware/                                    # MIDDLEWARE TOÀN CỤC CỦA SERVICE
    │   └── GlobalExceptionHandlerMiddleware.cs        # Bắt mọi lỗi ngoại lệ, format RFC 9110 ProblemDetails
    ├── Services/                                      # gRPC Server Service (CommerceGrpcService.cs)
    ├── Contracts/                                     # shop.proto (Hợp đồng gRPC)
    └── Program.cs                                     # Khởi động Kestrel HTTP/1 & HTTP/2, gọi DI App & Infra
```

---

## 2. NGUYÊN TẮC PHÂN CHIA TRÁCH NHIỆM

1. **`Commerce.Domain`**: Chứa các Entity nghiệp vụ giỏ hàng, đơn hàng, hóa đơn, hoàn tiền. Tuyệt đối không import thư viện ngoài hay EF Core vào đây.
2. **`Commerce.Application`**: Chứa toàn bộ ca sử dụng (Use Cases). Mỗi tính năng nằm trong thư mục riêng `Services/{Feature}/` gồm DTOs, Interface và Implementation. Gọi CSDL qua `ICommerceDbContext`.
3. **`Commerce.Infrastructure`**: Triển khai `AppDbContext` kế thừa `ICommerceDbContext`. Quản lý Transactional Outbox và Saga State Machine.
4. **`Commerce.Api`**: Cổng tiếp nhận cực kỳ mỏng. Trang bị `GlobalExceptionHandlerMiddleware` để chuẩn hóa lỗi trả về cho client.

---

## 3. QUY TRÌNH THÊM TÍNH NĂNG MỚI (DÀNH CHO DEV & AI AGENT)

1. **Bước 1**: Tạo Entity & Enum trong `Commerce.Domain/Entities/`.
2. **Bước 2**: Tạo Configuration trong `Commerce.Infrastructure/Persistence/Configurations/` và khai báo `DbSet<T>` trong `ICommerceDbContext` & `AppDbContext`.
3. **Bước 3**: Tạo thư mục tính năng trong `Commerce.Application/Services/{Feature}/` gồm `I{Feature}Service.cs`, `{Feature}Service.cs`, và `{Feature}Dtos.cs`.
4. **Bước 4**: Đăng ký Service vào `Commerce.Application/DependencyInjection.cs`.
5. **Bước 5**: Gọi Service trong `Commerce.Api/Services/CommerceGrpcService.cs`.

---

## 4. QUY CHUẨN THÔNG BÁO LỖI (MANDATORY ERROR RULES)

1. **100% English**: Tuyệt đối không dùng tiếng Việt.
2. **Sentence case**: Viết hoa duy nhất chữ cái đầu (`"Order not found in the system."`).
3. **Error Code SCREAMING_SNAKE_CASE**: Ví dụ `ORDER_NOT_FOUND`, `CART_NOT_FOUND`, `ORDER_INVALID_STATUS`.

---

## 5. CÁCH CHẠY VÀ KIỂM THỬ ĐỘC LẬP SERVICE

```bash
# 1. Bật database của Commerce Service (Port 5435):
cd src/Services/Commerce
docker compose up -d commerce-db

# 2. Tạo & Cập nhật migration:
./.tools/dotnet-ef migrations add InitialCommerceSchema --project Commerce.Infrastructure --startup-project Commerce.Api
./.tools/dotnet-ef database update --project Commerce.Infrastructure --startup-project Commerce.Api

# 3. Chạy service:
dotnet run --project Commerce.Api
```
