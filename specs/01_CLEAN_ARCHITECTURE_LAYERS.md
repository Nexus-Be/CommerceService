# 01 — PHÂN TẦNG CLEAN ARCHITECTURE & QUY TẮC PHỤ THUỘC (COMMERCE SERVICE)

Solution `Commerce.sln` bao gồm 4 project tương ứng với 4 tầng kiến trúc sạch (Clean Architecture):

```
Commerce.sln
├── Commerce.Domain/           # Entities (Cart, CartItem, Order, OrderLine), Value Objects, Domain Events
├── Commerce.Application/      # Use cases CQRS (Commands, Queries, DTOs, CheckoutSaga)
├── Commerce.Infrastructure/   # EF Core DbContext, PostgreSQL mappings, MassTransit Saga State Machine
└── Commerce.Api/              # Điểm khởi chạy gRPC Server (Program.cs, DI setup)
```

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
- **`Commerce.Domain`**: Chứa các Entity gốc `Order`, `OrderLine`, `Cart`, `CartItem`, Enum trạng thái `OrderStatus`. Các nghiệp vụ chuyển đổi trạng thái phải đặt trong Domain Entity (ví dụ: `order.MarkAsStockReserved()`, `order.Cancel(string reason)`).
- **`Commerce.Application`**: Chứa các ca sử dụng đặt hàng, xem giỏ hàng, cập nhật giỏ hàng. Khai báo interface `ICommerceDbContext`. Định nghĩa luồng State Machine Saga qua MassTransit.
- **`Commerce.Infrastructure`**: Chứa `CommerceDbContext`, bảng `OrderSagaState`, tích hợp PostgreSQL và cấu hình Outbox/Inbox của MassTransit.
- **`Commerce.Api`**: Điểm khởi động gRPC Server, đăng ký `app.MapGrpcService<CommerceGrpcService>()`.

---

## 2. QUY CHUẨN CODE TỪNG TẦNG

1. **`Commerce.Domain`**:
   - `Order` quản lý danh sách `OrderLine`. Tính toán tổng tiền `TotalAmount = sum(UnitPrice * Quantity)` được đóng gói an toàn trong entity.
   - Trạng thái đơn chỉ được chuyển đổi hợp lệ:
     `Pending` $\rightarrow$ `StockReserved` $\rightarrow$ `Paid` $\rightarrow$ `Preparing` $\rightarrow$ `Shipped` $\rightarrow$ `Completed`.
     Từ bất kỳ bước nào trước `Shipped` đều có thể chuyển sang `Cancelled` nếu có lỗi bù trừ.

2. **`Commerce.Application`**:
   - Giới hạn dòng hàng: Tối đa 50 dòng hàng (`MaxCheckoutLines`) và số lượng mỗi dòng từ 1 đến 1000 (`MaxCheckoutQuantity`).

3. **`Commerce.Infrastructure`**:
   - Khóa ngoại `OrderLine.OrderId` cascade delete khi xóa đơn nháp.
   - Đánh Index trên `Order.UserId`, `Order.Status`, `Order.CreatedAt` và `Cart.UserId`.
