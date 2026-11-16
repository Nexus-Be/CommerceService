# 01 — PHÂN TẦNG CLEAN ARCHITECTURE & QUY TẮC PHỤ THUỘC (COMMERCE SERVICE)

> **Trạng thái triển khai (2026-11-16):** ✅ ĐÃ HOÀN THIỆN & CHUẨN HÓA — 4 tầng Clean Architecture chuẩn hóa: Feature Co-location cho Cart, Order, và AuctionOrder; 1-to-1 Controller Pairing với 3 Controllers; 5 Outbound Typed Clients; 2 Background Workers; 60+ Unit & Integration Tests.
> **Nguồn sự thật:** Code trong `Commerce.sln` (`Commerce.Domain`, `Commerce.Application`, `Commerce.Infrastructure`, `Commerce.Api`, `tests/Commerce.UnitTests`).
> **Cập nhật lần cuối:** 2026-11-16.

---

## 1. CẤU TRÚC PHÂN TẦNG VÀ LUỒNG PHỤ THUỘC (DEPENDENCY RULE)

```
Commerce.sln
├── Commerce.Domain/           # 1. TẦNG DOMAIN: POCO 100%, ZERO DEPENDENCY
│   ├── Base/                  # BaseEntity<T>, IAuditableEntity, ISoftDelete
│   ├── Entities/              # 16 Entities: Cart, CartItem, Order, SubOrder, SubOrderItem, AuctionOrder...
│   ├── Enums/                 # Canonical Enums: OrderStatus, SubOrderStatus, PaymentMethod, CartStatus...
│   ├── ValueObjects/          # Snapshots: ShippingAddressSnapshot, VoucherBreakdown, ShipmentTrackingSnapshot...
│   └── Exceptions/            # CommerceDomainException (Mã lỗi SCREAMING_SNAKE_CASE, English sentence case)
│
├── Commerce.Application/      # 2. TẦNG APPLICATION: USE CASES & FEATURE CO-LOCATION
│   ├── Common/                # PagedData<T>, PaginationConstants, Extensions (AuditLog, Enum)
│   ├── Interfaces/            # ICommerceDbContext, IWalletClient, ICatalogClient, IFulfillmentClient, IUserClient, IAuctionClient
│   ├── Services/Cart/         # ICartService.cs, CartService.cs, CartRequests.cs, CartResponses.cs, CartDtos.cs
│   ├── Services/Order/        # IOrderService.cs, OrderService.cs, OrderRequests.cs, OrderResponses.cs, OrderDtos.cs
│   ├── Services/AuctionOrder/ # IAuctionOrderService.cs, AuctionOrderService.cs, AuctionOrderRequests.cs, AuctionOrderResponses.cs
│   └── DependencyInjection.cs # AddApplicationServices()
│
├── Commerce.Infrastructure/   # 3. TẦNG INFRASTRUCTURE: PERSISTENCE & OUTBOUND TYPED CLIENTS
│   ├── Persistence/           # AppDbContext, Configurations/ (16 Fluent API classes), Migrations/ (4 migrations), Seeds/
│   ├── Services/              # Typed REST Clients: WalletClient, CatalogClient, FulfillmentClient, UserClient, AuctionClient
│   └── DependencyInjection.cs # AddInfrastructureServices() (DbContext pool, HttpClientFactory với Polly resilience)
│
├── Commerce.Api/              # 4. TẦNG API / HOST: ENTRY POINT, THIN CONTROLLERS & WORKERS
│   ├── Controllers/Public/    # CartsController.cs, OrdersController.cs, AuctionOrdersController.cs
│   ├── Middleware/            # GlobalExceptionHandlerMiddleware (RFC 9110), UserContextMiddleware
│   ├── Extensions/            # HttpContextExtensions, ServiceDefaultsExtensions, ObservabilityExtensions
│   ├── Filters/               # RequireRoleAttribute
│   ├── Workers/               # PaymentTimeoutWorker.cs, VnPayPaymentPollingWorker.cs
│   └── Program.cs             # WebApplication bootstrap, Kestrel, Swagger, HealthCheck
│
└── tests/Commerce.UnitTests/  # 5. TEST SUITE: 60+ AUTOMATED UNIT & INTEGRATION TESTS
    ├── Application/           # CartServiceTests, OrderServiceTests, AuctionOrderServiceTests, EndToEndFlowTests...
    ├── Domain/                # CartDomainTests, FinancialInvariantTests
    ├── Infrastructure/        # Typed client tests, Idempotency tests, Fault tolerance tests
    └── Controllers/           # Delivery callback & endpoint controller tests
```

### Luồng Phụ Thuộc (Dependency Inversion)
```
[ Commerce.Api ] ────────────┐
                             │
                             ▼
               [ Commerce.Application ] ──▶ [ Commerce.Domain ]
                             ▲
                             │
[ Commerce.Infrastructure ] ─┘
```
- `Commerce.Domain` độc lập 100%, không tham chiếu bất kỳ project hay external package nào.
- `Commerce.Application` chỉ tham chiếu `Commerce.Domain`. Giao tiếp cơ sở dữ liệu trừu tượng hóa qua `ICommerceDbContext`.
- `Commerce.Infrastructure` triển khai `AppDbContext` kế thừa `ICommerceDbContext` và các Outbound Typed Clients.
- `Commerce.Api` chỉ làm nhiệm vụ tiếp nhận HTTP request, xác thực danh tính từ Header và điều phối qua Feature Service tương ứng.

---

## 2. QUY CHUẨN CODE CỐT LÕI TỪNG TẦNG

1. **Feature Co-Location (Tầng Application)**:
   - Mọi nghiệp vụ của một tính năng phải gom cụm trong cùng thư mục `Commerce.Application/Services/{Feature}/`.
   - Gồm đủ bộ: `I{Feature}Service.cs`, `{Feature}Service.cs`, `{Feature}Requests.cs`, `{Feature}Responses.cs`, `{Feature}Dtos.cs`.
2. **Thin 1-to-1 Controller Pairing (Tầng Api)**:
   - `CartsController.cs` $\rightarrow$ chỉ inject duy nhất `ICartService`.
   - `OrdersController.cs` $\rightarrow$ chỉ inject duy nhất `IOrderService`.
   - `AuctionOrdersController.cs` $\rightarrow$ chỉ inject duy nhất `IAuctionOrderService`.
   - Controller không chứa logic tính toán tiền nong, không truy vấn trực tiếp DbContext.
3. **No-Repository Pattern**:
   - Sử dụng trực tiếp `ICommerceDbContext` với các thuộc tính `DbSet<T>` và `SaveChangesAsync` trong Application layer.
   - Cấm tạo các lớp bọc trừu tượng như `IOrderRepository`, `ICartRepository`, `UnitOfWork`.
4. **Quy Chuẩn Tên Enum Chuẩn Hóa (Canonical Enums)**:
   - `SubOrderStatus`: Dùng `Confirmed` (cấm dùng alias `SellerAccepted`), dùng `Shipped` (cấm dùng alias `Shipping`).
   - `DiscountType`: Dùng `Percentage` và `FixedAmount`.
   - `DiscountScopeType`: Dùng `System` (cấm dùng alias `Global`).
