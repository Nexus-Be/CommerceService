# Implementation Plan: Order & Checkout Service (Quản lý Đặt hàng & Đơn hàng)

**Feature Directory**: `specs/002-order-service`  
**Feature Branch**: `feat/cart-service-v2`  
**Spec Reference**: `specs/002-order-service/spec.md`  
**Reconciliation Reference**: `docs/COMMERCE_API_RECONCILIATION_REPORT.md`  

---

## 1. Technical Context & Guardrails

- **Runtime & Framework**: `.NET 8.0`, C# 12, ASP.NET Core Web API.
- **Database**: PostgreSQL 16 on port `5435` (`commerce_db`).
- **Entity Framework Core**: EF Core 8.0 with Npgsql.
- **Architectural Rules (Rule 01 to 06)**:
  - **No-Repository Pattern**: Tầng Application dùng trực tiếp `AppDbContext` và `ICommerceDbContext`. Cấm bọc Repository.
  - **Feature Co-location**: Toàn bộ DTOs, Requests, Responses và Service của Order đặt tại `Commerce.Application/Services/Order/`.
  - **1-to-1 Controller Pairing**: `OrdersController.cs` chỉ inject duy nhất `IOrderService`.
  - **Zero ResultFilter**: Controllers trả về Pure DTOs, API Gateway chịu trách nhiệm bọc vỏ `ApiResponse<T>`.
  - **Field Naming**: Khóa cứng `shippingFeeAmount` 100%. Cấm `shippingFee`.
  - **JSONB Snapshot**: Ánh xạ `ShippingAddressSnapshot` trong `SubOrder` bằng EF Core 8 Native JSON: `b.OwnsOne(x => x.ShippingAddress).ToJson()`.
  - **Outbound Boundary**: Không tạo Controller cho `/wallets/checkout/direct`, gọi sang `WalletService` qua `IWalletClient`.

---

## 2. Design Artifacts & Data Contracts

### 2.1 Domain Layer
- Tạo Value Object: `Commerce.Domain/ValueObjects/ShippingAddressSnapshot.cs`:
  ```csharp
  public record ShippingAddressSnapshot(
      string ReceiverName,
      string ContactPhone,
      string Street,
      string Ward,
      string District,
      string Province,
      int GhnDistrictId,
      string GhnWardCode,
      string FullAddressText
  );
  ```
- Cập nhật `Commerce.Domain/Entities/SubOrder.cs` để thuộc tính `ShippingAddress` có kiểu `ShippingAddressSnapshot`.

### 2.2 Application Layer DTOs (`Commerce.Application/Services/Order/`)
- `OrderRequests.cs`:
  - `CheckoutQuoteRequest(List<Guid> CartItemIds, ShippingAddressInput ShippingAddress, string ContactPhone, PaymentMethod PaymentMethod, string? CarrierCode, List<string>? VoucherCodes)`
  - `CreateOrderRequest(Guid QuoteId, PaymentMethod PaymentMethod, string ShippingAddress, string ContactPhone, string? Note)`
  - `CancelOrderRequest(CancellationReasonCode ReasonCode, string? ReasonText)`
  - `SellerAcceptOrderRequest(Guid SubOrderId, string? Note)`
  - `SellerRejectOrderRequest(Guid SubOrderId, CancellationReasonCode ReasonCode, string? ReasonText)`
  - `SubOrderRefundRequest(string Reason, string? Note)`
- `OrderResponses.cs`:
  - `CheckoutQuoteResponse`, `ShopOrderQuote`, `ShopQuoteItem`
  - `CreateOrderResponse`
  - `SubOrderDetailResponse`, `SubOrderItemResponse` (với đầy đủ `productDiscountAmount`, `shopDiscountAmount`, `systemDiscountAmount`, `shippingFeeAmount`)
  - `ShipmentTrackingResponse`, `SubOrderShipmentInfo`
  - `OrderSummaryResponse`
- `IOrderService.cs`:
  - `Task<CheckoutQuoteResponse> QuoteCheckoutAsync(Guid buyerId, CheckoutQuoteRequest request, CancellationToken ct)`
  - `Task<CreateOrderResponse> CreateOrderAsync(Guid buyerId, CreateOrderRequest request, CancellationToken ct)`
  - `Task<OrderResponse> GetOrderDetailAsync(Guid currentUserId, Guid orderId, CancellationToken ct)`
  - `Task<PagedData<OrderSummaryResponse>> ListOrdersAsync(Guid currentUserId, int pageIndex, int pageSize, OrderStatus? status, CancellationToken ct)`
  - `Task<SubOrderDetailResponse> GetSubOrderDetailAsync(Guid currentUserId, Guid subOrderId, CancellationToken ct)`
  - `Task CancelOrderAsync(Guid currentUserId, Guid orderId, CancelOrderRequest request, CancellationToken ct)`
  - `Task SellerAcceptSubOrderAsync(Guid sellerId, Guid orderId, SellerAcceptOrderRequest request, CancellationToken ct)`
  - `Task SellerRejectSubOrderAsync(Guid sellerId, Guid orderId, SellerRejectOrderRequest request, CancellationToken ct)`
  - `Task<ShipmentTrackingResponse> GetShipmentTrackingAsync(Guid currentUserId, Guid orderId, CancellationToken ct)`
  - `Task RefundSubOrderAsync(Guid currentUserId, Guid subOrderId, SubOrderRefundRequest request, CancellationToken ct)`

### 2.3 Infrastructure Layer
- Cập nhật `SubOrderConfiguration.cs`: Cấu hình `.OwnsOne(x => x.ShippingAddress).ToJson()`.

### 2.4 Api Layer (`Commerce.Api/Controllers/Public/OrdersController.cs`)
- Expose 10 endpoints chuẩn RESTful theo `specs/02_API_REST_CONTRACTS.md`.

---

## 3. Verification Plan
- Chạy `dotnet build Commerce.sln -c Release` (0 lỗi, 0 cảnh báo sai).
- Thêm bộ Unit Tests tại `tests/Commerce.UnitTests/Application/OrderServiceTests.cs`.
- Chạy `dotnet test` vượt qua 100% test cases.
