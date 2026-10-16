# 04 — MÔ HÌNH THỰC THỂ & CƠ SỞ DỮ LIỆU (COMMERCE SERVICE DATA PERSISTENCE)

---

## 1. MÔ HÌNH THỰC THỂ CHÍNH (DOMAIN ENTITIES)

1. **`Cart`**:
   - `Id`: `Guid`.
   - `UserId`: `Guid` (Unique, mỗi user chỉ có 1 giỏ hàng hoạt động).
   - `CreatedAt`, `UpdatedAt`: `DateTimeOffset`.
   - Navigation: `ICollection<CartItem> Items`.

2. **`CartItem`**:
   - `Id`: `Guid`.
   - `CartId`: `Guid` (FK $\rightarrow$ `Cart.Id`).
   - `ProductId`: `Guid`.
   - `Quantity`: `int` (Bắt buộc $\ge 1$).
   - `UnitPrice`: `decimal` (Precision: 18, Scale: 2).
   - `AddedAt`: `DateTimeOffset`.

3. **`Order`**:
   - `Id`: `Guid`.
   - `OrderCode`: `string` (Mã đơn thân thiện hiển thị cho khách, ví dụ: `"ORD-202610-8812"`).
   - `UserId`: `Guid`.
   - `Status`: `OrderStatus` (`Pending`, `StockReserved`, `Paid`, `Preparing`, `Shipped`, `Completed`, `Cancelled`).
   - `TotalAmount`: `decimal` (Precision: 18, Scale: 2).
   - `Currency`: `string` (`"VND"`).
   - `IdempotencyKey`: `string` (UUID, Unique Index để chống tạo trùng đơn).
   - `CancellationReason`: `string?`.
   - `CreatedAt`, `UpdatedAt`: `DateTimeOffset`.
   - Navigation: `ICollection<OrderLine> Lines`.

4. **`OrderLine`**:
   - `Id`: `Guid`.
   - `OrderId`: `Guid` (FK $\rightarrow$ `Order.Id`).
   - `ProductId`: `Guid`.
   - `SellerId`: `Guid`.
   - `Quantity`: `int`.
   - `UnitPrice`: `decimal`.
   - `Subtotal`: `decimal` (`UnitPrice * Quantity`).

5. **`OrderStateInstance` (MassTransit Saga State)**:
   - `CorrelationId`: `Guid` (Khóa chính Saga, ánh xạ với `Order.Id`).
   - `CurrentState`: `string`.
   - `PaymentAttemptCount`: `int`.
   - `Version`: `int`.

---

## 2. QUY CHUẨN CƠ SỞ DỮ LIỆU & EF CORE

- Engine: PostgreSQL.
- Database: `commerce_db` tại port `5434`.
- Chuỗi kết nối: `Host=localhost;Port=5434;Database=commerce_db;Username=postgres;Password=...`.
- Tên bảng: `carts`, `cart_items`, `orders`, `order_lines`, `order_saga_states`.
- **No Repository Pattern**: Tầng Application tương tác trực tiếp với `CommerceDbContext`.
