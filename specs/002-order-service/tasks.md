# Implementation Tasks: Order & Checkout Service (Quản lý Đặt hàng & Đơn hàng)

**Feature Directory**: `specs/002-order-service`  
**Spec Reference**: `specs/002-order-service/spec.md`  
**Plan Reference**: `specs/002-order-service/plan.md`  

---

## Phase 1: Domain Value Objects & Entities

- [x] **TASK-01 (Domain)**: Tạo `Commerce.Domain/ValueObjects/ShippingAddressSnapshot.cs` với các thuộc tính: `ReceiverName`, `ContactPhone`, `Street`, `Ward`, `District`, `Province`, `GhnDistrictId`, `GhnWardCode`, `FullAddressText`.
- [x] **TASK-02 (Domain)**: Cập nhật `Commerce.Domain/Entities/SubOrder.cs`: đổi `ShippingAddress` sang kiểu `ShippingAddressSnapshot`. Đảm bảo `CancellationReasonCode` và `SubOrderStatus` đồng nhất.

---

## Phase 2: Application DTOs & Contracts

- [x] **TASK-03 (Application DTOs)**: Cập nhật `Commerce.Application/Services/Order/OrderRequests.cs` với:
  - `CheckoutQuoteRequest`
  - `ShippingAddressInput`
  - `CreateOrderRequest`
  - `CancelOrderRequest`
  - `SellerAcceptOrderRequest`
  - `SellerRejectOrderRequest`
  - `SubOrderRefundRequest`
- [x] **TASK-04 (Application DTOs)**: Cập nhật `Commerce.Application/Services/Order/OrderResponses.cs` với:
  - `CheckoutQuoteResponse`, `ShopOrderQuote`, `ShopQuoteItem`
  - `CreateOrderResponse`
  - `SubOrderDetailResponse` (chứa đầy đủ `productDiscountAmount`, `shopDiscountAmount`, `systemDiscountAmount`, `shippingFeeAmount`)
  - `ShipmentTrackingResponse`
  - `OrderSummaryResponse`
- [x] **TASK-05 (Application Interface)**: Cập nhật `Commerce.Application/Services/Order/IOrderService.cs` đủ 10 chữ ký hàm chuẩn RESTful.

---

## Phase 3: Infrastructure Persistence & Mapping

- [x] **TASK-06 (Infrastructure)**: Cập nhật `Commerce.Infrastructure/Persistence/Configurations/SubOrderConfiguration.cs` sử dụng EF Core 8 Native JSON `.OwnsOne(x => x.ShippingAddress).ToJson()`.
- [x] **TASK-07 (Infrastructure)**: Kiểm tra và cập nhật `ICommerceDbContext` / `AppDbContext` đảm bảo biên dịch và lưu trữ đúng kiểu.

---

## Phase 4: Application Service Implementation

- [x] **TASK-08 (OrderService - Quote & Create)**:
  - Triển khai `QuoteCheckoutAsync`: lọc cart items theo buyer, phân nhóm shop, tính tiền hàng, chiết khấu, tiền ship GHN, lưu bộ nhớ đệm `quoteId` (15 phút TTL).
  - Triển khai `CreateOrderAsync`: kiểm tra `quoteId`, tạo `Order` + `SubOrders` + `SubOrderItems` trong 1 transaction, gọi `IWalletClient.DirectCheckoutAsync` nếu ví/vnpay, dọn dẹp các món khỏi giỏ hàng.
- [x] **TASK-09 (OrderService - Queries & State Mutations)**:
  - Triển khai `GetOrderDetailAsync`, `ListOrdersAsync` (phân trang database).
  - Triển khai `GetSubOrderDetailAsync` (Single object).
  - Triển khai `CancelOrderAsync`, `SellerAcceptSubOrderAsync`, `SellerRejectSubOrderAsync`.
  - Triển khai `GetShipmentTrackingAsync`, `RefundSubOrderAsync`.

---

## Phase 5: Api Controllers

- [x] **TASK-10 (OrdersController)**: Cập nhật `Commerce.Api/Controllers/Public/OrdersController.cs`:
  - `POST /api/v1/checkouts/quote` (`R75`)
  - `POST /api/v1/orders` (`R76`)
  - `GET /api/v1/orders/{orderId:guid}` (`R77`)
  - `GET /api/v1/orders` (`R78`)
  - `GET /api/v1/sub-orders/{subOrderId:guid}` (`R79`)
  - `POST /api/v1/orders/{orderId:guid}/cancel` (`R80`)
  - `POST /api/v1/orders/{orderId:guid}/accept` (`R83`)
  - `POST /api/v1/orders/{orderId:guid}/reject` (`R84`)
  - `GET /api/v1/shipments/order/{orderId:guid}` (`R87`)
  - `POST /api/v1/sub-orders/{subOrderId:guid}/refund` (`R99`)

---

## Phase 6: Verification & Testing

- [x] **TASK-11 (Unit Tests)**: Thêm các unit test cases trong `tests/Commerce.UnitTests/Application/OrderServiceTests.cs`.
- [x] **TASK-12 (Build & Test Gate)**: Chạy `dotnet build Commerce.sln -c Release` (0 error) và `dotnet test` (100% pass: 24/24 tests).
- [x] **TASK-13 (Documentation Sync)**: Cập nhật checklist và báo cáo đối soát hoàn chỉnh.
