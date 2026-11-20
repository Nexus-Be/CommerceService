# Implementation Plan: Fulfillment Service Integration (Tồn kho, Giữ hàng, Giao vận & Làm xám Giỏ hàng)

**Feature Directory**: `specs/003-fulfillment-integration`  
**Feature Branch**: `feat/cart-service-v2`  
**Spec Reference**: `specs/003-fulfillment-integration/spec.md`  

---

## 1. Technical Context & Ranh Giới Kiến Trúc

- **Port & Database**: Port `8003`, DB `5435` (`commerce_db`).
- **Clean Architecture 4 Tầng**:
  - `Commerce.Application/Interfaces/IFulfillmentClient.cs`: Định nghĩa hợp đồng giao tiếp với Fulfillment Service.
  - `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs`: Triển khai Typed HttpClient kết nối sang cổng `8005` (hoặc mock fallback cho dev/test).
  - `Commerce.Application/Services/Cart/`: Nâng cấp DTO `CartItemResponse` thêm `isAvailable` và `disabledReason`. Nâng cấp `CartService` tích hợp check tồn kho.
  - `Commerce.Application/Services/Order/`: Nâng cấp `OrderService` gọi `ReserveStockAsync`, `ReleaseStockAsync`, `CommitStockAsync`.
  - `Commerce.Api/Workers/PaymentTimeoutWorker.cs`: Background Service quét định kỳ 1 phút để tự động hủy đơn quá hạn và nhả kho.

---

## 2. Design Artifacts & Contracts

### 2.1 Interface `IFulfillmentClient.cs`
- `CheckStockAvailabilityAsync(Guid skuId, int quantity, CancellationToken ct)`
- `BatchCheckStockAsync(List<Guid> skuIds, CancellationToken ct)`
- `ReserveStockAsync(Guid orderId, List<StockReservationItem> items, int ttlMinutes = 15, CancellationToken ct)`
- `CommitStockAsync(Guid orderId, CancellationToken ct)`
- `ReleaseStockAsync(Guid orderId, CancellationToken ct)`
- `CalculateShopShippingFeeAsync(ShopShippingQuoteRequest request, CancellationToken ct)`
- `CreateShipmentAsync(Guid subOrderId, CancellationToken ct)`

### 2.2 DTO Giỏ hàng Cập nhật (`CartResponses.cs`)
- `CartItemResponse`:
  - `bool IsAvailable = true`
  - `string? DisabledReason = null`

---

## 3. Verification Plan
- Chạy `dotnet build Commerce.sln -c Release` (0 lỗi, 0 cảnh báo).
- Viết Unit Tests kiểm chứng:
  - Thêm vào giỏ khi hết hàng ném `INSUFFICIENT_STOCK`.
  - Giỏ hàng làm xám sản phẩm khi tồn kho = 0.
  - Tạo đơn gọi giữ hàng thành công.
  - Quá hạn gọi nhả hàng thành công.
- Chạy `dotnet test` vượt qua 100% test cases.
