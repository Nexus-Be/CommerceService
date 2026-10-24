# 02 — HỢP ĐỒNG GIAO TIẾP GRPC (COMMERCE SERVICE GRPC CONTRACTS)
> **Trạng thái triển khai (2026-10-24):** ❌ Chưa có — 0/10 rpc; 0 file `.proto` trong repo; không có package `Grpc.AspNetCore` trong bất kỳ `.csproj` nào.
> **Nguồn sự thật:** code trong repo này (`file:line` ghi kèm trong từng bảng).
> **Cập nhật lần cuối:** 2026-10-24.
Dịch vụ gRPC của CommerceService **được đặc tả** trong hợp đồng duy nhất `Contracts/shop.proto` dưới service `CommerceApi`. ⚠️ **Đường dẫn này không tồn tại trong repo**: `Commerce.Api/Contracts/` không có, và `find . -name "*.proto"` trả về 0 kết quả. Toàn bộ mục 1 dưới đây là **đặc tả mục tiêu, chưa triển khai**.
---
## 1. DANH SÁCH CÁC RPC METHODS
### 1.1. Bảng trạng thái thực tế (đối chiếu code)
| # | RPC (đặc tả) | Request → Response (đặc tả) | Trạng thái | Bằng chứng |
|---|---|---|---|---|
| 1 | `GetCart` | `UserRequest` → `Cart` | ❌ CHƯA CÓ | không có `.proto`, không có `*GrpcService.cs` |
| 2 | `AddCartItem` | `CartItemRequest` → `Cart` | ❌ CHƯA CÓ | không có `.proto`, không có `*GrpcService.cs` |
| 3 | `UpdateCartItem` | `UpdateCartItemRequest` → `Cart` | ❌ CHƯA CÓ | không có `.proto`, không có `*GrpcService.cs` |
| 4 | `RemoveCartItem` | `RemoveCartItemRequest` → `Cart` | ❌ CHƯA CÓ | không có `.proto`, không có `*GrpcService.cs` |
| 5 | `ClearCart` | `UserRequest` → `Cart` | ❌ CHƯA CÓ | không có `.proto`, không có `*GrpcService.cs` |
| 6 | `CreateOrder` | `CreateOrderRequest` → `Order` | ❌ CHƯA CÓ | `OrderService` chỉ có 2 hàm đọc, xem 1.2 |
| 7 | `GetOrder` | `OrderRequest` → `Order` | ❌ CHƯA CÓ | chưa có lớp gRPC; chỉ có `OrderService.GetByIdAsync` trả `OrderDto?` (`Commerce.Application/Services/Order/OrderService.cs:20`) |
| 8 | `ListOrders` | `MyOrdersRequest` → `OrderPage` | ❌ CHƯA CÓ | chưa có lớp gRPC; chỉ có `OrderService.ListByBuyerIdAsync` (`OrderService.cs:36`) |
| 9 | `ListOrdersBySeller` | `SellerOrdersRequest` → `OrderPage` | ❌ CHƯA CÓ | không có truy vấn theo `SellerId` trong Application |
| 10 | `ListAllOrders` | `PageRequest` → `OrderPage` | ❌ CHƯA CÓ | không có truy vấn `ListAll` trong Application |
### 1.2. Bề mặt thật hiện có (thay thế tạm cho gRPC)
| Thành phần | Trạng thái | Bằng chứng |
|---|---|---|
| `CartService.GetActiveCartByBuyerIdAsync(Guid, CancellationToken)` | ✅ CÓ (1 truy vấn đọc) | `Commerce.Application/Services/Cart/CartService.cs:19` |
| `OrderService.GetByIdAsync(Guid, CancellationToken)` | ✅ CÓ (1 truy vấn đọc) | `Commerce.Application/Services/Order/OrderService.cs:20` |
| `OrderService.ListByBuyerIdAsync(Guid, CancellationToken)` | ✅ CÓ (1 truy vấn đọc) | `Commerce.Application/Services/Order/OrderService.cs:36` |
| Ghi dữ liệu (`Add`/`Update`/`Remove` + `SaveChangesAsync`) | ❌ CHƯA CÓ | `SaveChangesAsync` chỉ được **khai báo** ở `Commerce.Application/Interfaces/ICommerceDbContext.cs:27`, **0 lời gọi** trong toàn repo |
| Minimal endpoint `/health` | ✅ CÓ | `Commerce.Api/Program.cs:58` |
### 1.3. Hợp đồng đặc tả gốc (giữ nguyên — đặc tả mục tiêu, chưa triển khai)
```protobuf
service CommerceApi {
  // Thao tác giỏ hàng (Shopping Cart)
  rpc GetCart(UserRequest) returns (Cart);
  rpc AddCartItem(CartItemRequest) returns (Cart);
  rpc UpdateCartItem(UpdateCartItemRequest) returns (Cart);
  rpc RemoveCartItem(RemoveCartItemRequest) returns (Cart);
  rpc ClearCart(UserRequest) returns (Cart);
  // Đặt hàng & Quản lý đơn hàng (Orders)
  rpc CreateOrder(CreateOrderRequest) returns (Order);
  rpc GetOrder(OrderRequest) returns (Order);
  rpc ListOrders(MyOrdersRequest) returns (OrderPage);
  rpc ListOrdersBySeller(SellerOrdersRequest) returns (OrderPage);
  rpc ListAllOrders(PageRequest) returns (OrderPage);
}
```
---
## 2. QUY CHUẨN XỬ LÝ LỖI & MÃ LỖI (ERROR STATUS CODES)
⚠️ **Phần này là đặc tả mục tiêu**: class `CommerceGrpcService` **không tồn tại**, nên không có chỗ nào ném `RpcException` trong repo (grep `RpcException` = 0 kết quả).
Đặc tả mong muốn khi xử lý trong `CommerceGrpcService`:
- **`StatusCode.InvalidArgument`** (400 Bad Request): Dòng hàng > 50 dòng, số lượng < 1 hoặc > 1000, `Idempotency-Key` không đúng định dạng UUID, giỏ hàng rỗng.
- **`StatusCode.NotFound`** (404 Not Found): Không tìm thấy Order hoặc CartItem với ID tương ứng.
- **`StatusCode.PermissionDenied`** (403 Forbidden): User không thể xem chi tiết đơn hàng của người khác (trừ khi có role `staff` hoặc `admin`).
- **`StatusCode.AlreadyExists`** (409 Conflict): `Idempotency-Key` bị trùng lặp với đơn hàng khác đang xử lý.
### 2.1. Mã lỗi thật đang chạy (middleware HTTP, không phải gRPC)
| Mã lỗi trả về | HTTP Status | Điều kiện | Bằng chứng |
|---|---|---|---|
| `NOT_FOUND` | 404 | `EntityNotFoundException` | `Commerce.Api/Middleware/GlobalExceptionHandlerMiddleware.cs:38` |
| `BUSINESS_RULE_VIOLATION` | 400 | `BusinessRuleValidationException` | `GlobalExceptionHandlerMiddleware.cs:43` |
| `DOMAIN_ERROR` | 400 | `DomainException` | `GlobalExceptionHandlerMiddleware.cs:48` |
| `VAL_INVALID_ARGUMENT` | 400 | `ArgumentException` | `GlobalExceptionHandlerMiddleware.cs:53` |
| `INTERNAL_SERVER_ERROR` | 500 | mọi exception khác | `GlobalExceptionHandlerMiddleware.cs:58` |
Ghi chú: `Idempotency-Key` **chưa được kiểm tra ở bất kỳ đâu** trong code (grep `Idempotency` = 0 kết quả) — xem đặc tả ở `00_AGENT_MISSION_GUARDRAILS.md` mục 2.3, hiện là yêu cầu chưa triển khai.