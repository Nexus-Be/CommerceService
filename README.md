# COMMERCE SERVICE — ARCHITECTURE & DEVELOPMENT GUIDE

## (CẨM NANG KIẾN TRÚC & PHÁT TRIỂN DÀNH CHO DEVELOPER VÀ AI AGENT)

> **Trạng thái triển khai (2026-11-16):** ✅ ĐÃ HOÀN THIỆN & CHUẨN HÓA 100% RESTFUL — Quản lý Giỏ hàng, Đơn hàng Đa Người bán (Master Order & Sub-Orders), Đơn hàng Đấu giá (Auction Orders), Điều phối Thanh toán 2 pha, Vận đơn Giao Hàng Nhanh (GHN) và Giải ngân Quỹ Ủy Thác (Escrow Settlement). Bãi bỏ hoàn toàn gRPC, RabbitMQ, MassTransit và Saga.
> **Nguồn sự thật:** Toàn bộ mã nguồn trong `Commerce.sln` (`Commerce.Domain`, `Commerce.Application`, `Commerce.Infrastructure`, `Commerce.Api`, `tests/Commerce.UnitTests`).
> **Thông tin dịch vụ**:
> - **Service Port**: `8003` (Container & Local thống nhất).
> - **Database**: PostgreSQL 16 (`commerce_db`), Port máy host: `5435`.
> - **Tài liệu kiến trúc toàn diện**: [docs/COMMERCE_ARCHITECTURE_AND_FLOW_SPEC.md](docs/COMMERCE_ARCHITECTURE_AND_FLOW_SPEC.md) (v3.0).

---

## 1. TỔNG QUAN BỀ MẶT DỊCH VỤ

| Thành phần | Trạng thái | Bằng chứng mã nguồn |
|---|---|---|
| **Health Check & Swagger** | ✅ CÓ | `GET /health` (`Program.cs:59`), Swagger UI (`/swagger`) |
| **Exception Handling (RFC 9110)** | ✅ CÓ | `GlobalExceptionHandlerMiddleware.cs` chuẩn hóa ProblemDetails |
| **User Context & Identity Extraction** | ✅ CÓ | `UserContextMiddleware.cs` & `HttpContextExtensions.cs` |
| **Giỏ hàng (`CartsController`)** | ✅ CÓ (14 APIs) | `Commerce.Api/Controllers/Public/CartsController.cs` (`CART-01` .. `CART-14`) |
| **Đơn hàng (`OrdersController`)** | ✅ CÓ (14 APIs) | `Commerce.Api/Controllers/Public/OrdersController.cs` (`R75` .. `R101`) |
| **Đơn đấu giá (`AuctionOrdersController`)**| ✅ CÓ (8 APIs) | `Commerce.Api/Controllers/Public/AuctionOrdersController.cs` (`US1` .. `US9`) |
| **Outbound Typed REST Clients** | ✅ CÓ (5 Clients)| `WalletClient`, `CatalogClient`, `FulfillmentClient`, `UserClient`, `AuctionClient` |
| **Hosted Background Workers** | ✅ CÓ (2 Workers)| `PaymentTimeoutWorker.cs`, `VnPayPaymentPollingWorker.cs` |
| **Bộ kiểm thử tự động (Test Suite)** | ✅ CÓ (60+ Tests)| `tests/Commerce.UnitTests` (100% Passed) |

---

## 2. SƠ ĐỒ PHÂN TẦNG VÀ CẤU TRÚC THƯ MỤC CHUẨN

```
CommerceService/
│
├── Commerce.sln                                       # Solution chính thức
│
├── Commerce.Domain/                                   # 1. TẦNG DOMAIN (POCO 100%, ZERO EXTERNAL DEPENDENCY)
│   ├── Base/                                          # BaseEntity<T>, IAuditableEntity, ISoftDelete
│   ├── Entities/                                      # 16 Entities (Cart, CartItem, Order, SubOrder, AuctionOrder...)
│   ├── Enums/                                         # Canonical Enums (OrderStatus, SubOrderStatus, PaymentMethod...)
│   ├── ValueObjects/                                  # Snapshots (ShippingAddressSnapshot, VoucherBreakdown...)
│   └── Exceptions/                                    # CommerceDomainException (Mã lỗi chuẩn RFC 9110)
│
├── Commerce.Application/                              # 2. TẦNG APPLICATION (USE CASES & FEATURE CO-LOCATION)
│   ├── Common/                                        # PagedData<T>, PaginationConstants, Extensions
│   ├── Interfaces/                                    # ICommerceDbContext, IWalletClient, ICatalogClient, IFulfillmentClient...
│   ├── Services/Cart/                                 # ICartService.cs, CartService.cs, CartRequests.cs, CartResponses.cs, CartDtos.cs
│   ├── Services/Order/                                # IOrderService.cs, OrderService.cs, OrderRequests.cs, OrderResponses.cs, OrderDtos.cs
│   ├── Services/AuctionOrder/                         # IAuctionOrderService.cs, AuctionOrderService.cs, AuctionOrderRequests.cs...
│   └── DependencyInjection.cs                         # AddApplicationServices()
│
├── Commerce.Infrastructure/                           # 3. TẦNG INFRASTRUCTURE (PERSISTENCE & OUTBOUND REST CLIENTS)
│   ├── Persistence/                                   # AppDbContext, Configurations/ (16 Fluent API), Migrations/ (4 migrations), Seeds/
│   ├── Services/                                      # Typed Clients: WalletClient, CatalogClient, FulfillmentClient, UserClient, AuctionClient
│   └── DependencyInjection.cs                         # AddInfrastructureServices() (DbContext, HttpClientFactory với Polly)
│
├── Commerce.Api/                                      # 4. TẦNG API / HOST (ENTRY POINT, THIN CONTROLLERS & WORKERS)
│   ├── Controllers/Public/                            # CartsController.cs, OrdersController.cs, AuctionOrdersController.cs
│   ├── Middleware/                                    # GlobalExceptionHandlerMiddleware.cs, UserContextMiddleware.cs
│   ├── Extensions/                                    # HttpContextExtensions.cs, ServiceDefaultsExtensions.cs
│   ├── Filters/                                       # RequireRoleAttribute.cs
│   ├── Workers/                                       # PaymentTimeoutWorker.cs, VnPayPaymentPollingWorker.cs
│   └── Program.cs                                     # WebApplication Bootstrap
│
└── tests/Commerce.UnitTests/                          # 5. TẦNG TEST (60+ AUTOMATED UNIT & INTEGRATION TESTS)
    ├── Application/                                   # CartServiceTests, OrderServiceTests, AuctionOrderServiceTests, EndToEndFlowTests...
    ├── Domain/                                        # CartDomainTests, FinancialInvariantTests
    ├── Infrastructure/                                # ExternalClientFaultTests, IdempotencyKeyTests, Typed client tests
    └── Controllers/                                   # Delivery callback & endpoint controller tests
```

---

## 3. NGUYÊN TẮC THIẾT KẾ BẤT BIẾN

1. **No-Repository Pattern over EF Core**:
   - Tầng Application làm việc trực tiếp với `ICommerceDbContext` và các `DbSet<T>`.
   - Tuyệt đối không bọc thêm tầng `IOrderRepository`, `ICartRepository` hay `UnitOfWork`.
2. **Synchronous Typed REST Orchestration**:
   - Giao tiếp giữa các dịch vụ hoàn toàn thông qua HTTP REST Client đồng bộ (`IWalletClient`, `ICatalogClient`, `IFulfillmentClient`, `IUserClient`, `IAuctionClient`).
   - Xử lý lỗi theo cơ chế Fail-Closed và thực thi lệnh đền bù (Compensating action, ví dụ xả kho khi thanh toán thất bại).
3. **Thin 1-to-1 Controller Pairing & Feature Co-location**:
   - Mỗi Controller ở tầng Api chỉ inject duy nhất Feature Service tương ứng (`CartsController` $\rightarrow$ `ICartService`, `OrdersController` $\rightarrow$ `IOrderService`, `AuctionOrdersController` $\rightarrow$ `IAuctionOrderService`).
   - Controller không chứa logic tính toán tiền nong, không truy vấn DbContext.
4. **Bảo Vệ Quyền Sở Hữu Tài Nguyên (Resource Ownership Governance)**:
   - Controller luôn trích xuất `currentUserId` từ `HttpContext.GetUserId()` do API Gateway chuyển giao qua header `x-user-id`.
   - Cấm tin tưởng tham số `buyerId` từ query string hay route URL. Nếu `currentUserId != buyerId` và không phải Admin/Staff, hệ thống trả về HTTP 403 `FORBIDDEN`.
5. **Quy Chuẩn Thông Báo Lỗi**:
   - 100% Tiếng Anh, viết hoa duy nhất chữ cái đầu (Sentence case): `"Cart item not found."`, `"Order cannot be cancelled in its current status."`.
   - Mã lỗi `SCREAMING_SNAKE_CASE` (ví dụ: `ORDER_NOT_FOUND`, `CART_NOT_FOUND`, `VAL_INVALID_INPUT`).
   - Enum trả về JSON theo chuẩn chuỗi PascalCase (`"PendingPayment"`, `"Paid"`, `"Delivered"`).

---

## 4. HƯỚNG DẪN CHẠY VÀ KIỂM THỬ ĐỘC LẬP

```bash
# 1. Khởi động cơ sở dữ liệu PostgreSQL (Port 5435):
docker compose up -d commerce-db

# 2. Biên dịch Solution ở cấu hình Release:
dotnet build Commerce.sln -c Release

# 3. Chạy toàn bộ bộ kiểm thử tự động (Unit & Integration Tests):
dotnet test Commerce.sln -c Release

# 4. Khởi chạy Commerce Service:
dotnet run --project Commerce.Api

# 5. Kiểm tra Health Check:
curl http://localhost:8003/health
```

---

## 5. DANH MỤC TÀI LIỆU QUAN TRỌNG

- [docs/COMMERCE_ARCHITECTURE_AND_FLOW_SPEC.md](docs/COMMERCE_ARCHITECTURE_AND_FLOW_SPEC.md): Đặc tả kiến trúc kỹ thuật và giải phẫu luồng nghiệp vụ chi tiết nhất (v3.0).
- [docs/api_catalog.md](docs/api_catalog.md): Danh mục tra cứu nhanh toàn bộ RESTful API Endpoints.
- [specs/README.md](specs/README.md): Bộ chỉ dẫn kỹ thuật Spec Kit dành cho AI Coding Agents.
- [.specify/memory/constitution.md](.specify/memory/constitution.md): Hiến pháp kỹ thuật tối cao của CommerceService.
