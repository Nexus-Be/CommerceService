# 02 — HỢP ĐỒNG GIAO TIẾP GRPC (COMMERCE SERVICE GRPC CONTRACTS)

Dịch vụ gRPC của CommerceService được định nghĩa trong hợp đồng duy nhất `Contracts/shop.proto` dưới service `CommerceApi`:

---

## 1. DANH SÁCH CÁC RPC METHODS

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

Khi xử lý trong `CommerceGrpcService`, ném ra `RpcException`:
- **`StatusCode.InvalidArgument`** (400 Bad Request): Dòng hàng > 50 dòng, số lượng < 1 hoặc > 1000, `Idempotency-Key` không đúng định dạng UUID, giỏ hàng rỗng.
- **`StatusCode.NotFound`** (404 Not Found): Không tìm thấy Order hoặc CartItem với ID tương ứng.
- **`StatusCode.PermissionDenied`** (403 Forbidden): User không thể xem chi tiết đơn hàng của người khác (trừ khi có role `staff` hoặc `admin`).
- **`StatusCode.AlreadyExists`** (409 Conflict): `Idempotency-Key` bị trùng lặp với đơn hàng khác đang xử lý.
