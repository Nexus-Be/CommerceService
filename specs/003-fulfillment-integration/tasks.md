# Implementation Tasks: Fulfillment Service Integration (Tồn kho, Giữ hàng, Giao vận & Làm xám Giỏ hàng)

**Feature Directory**: `specs/003-fulfillment-integration`  
**Spec Reference**: `specs/003-fulfillment-integration/spec.md`  
**Plan Reference**: `specs/003-fulfillment-integration/plan.md`  

---

## Phase 1: Application Interfaces & DTOs

- [x] **TASK-01 (Interface)**: Tạo `Commerce.Application/Interfaces/IFulfillmentClient.cs` định nghĩa đầy đủ 7 phương thức: `CheckStockAvailabilityAsync`, `BatchCheckStockAsync`, `ReserveStockAsync`, `CommitStockAsync`, `ReleaseStockAsync`, `CalculateShopShippingFeeAsync`, `CreateShipmentAsync`.
- [x] **TASK-02 (Cart DTOs)**: Cập nhật `Commerce.Application/Services/Cart/CartResponses.cs` và `CartDtos.cs`: bổ sung `IsAvailable = true` và `DisabledReason = null` vào `CartItemResponse` và `CartItemDto`.

---

## Phase 2: Infrastructure Client Implementation

- [x] **TASK-03 (FulfillmentClient)**: Tạo `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs` kế thừa `IFulfillmentClient` với cơ chế Typed HttpClient và Dev Fallback an toàn khi chưa bật Fulfillment Service.
- [x] **TASK-04 (DI Registration)**: Đăng ký `services.AddHttpClient<IFulfillmentClient, FulfillmentClient>()` trong `Commerce.Infrastructure/DependencyInjection.cs`.

---

## Phase 3: Cart Service Upgrade (Check Tồn Kho & Làm Xám)

- [x] **TASK-05 (Cart Check Stock)**: Nâng cấp `CartService.AddItemAsync`: Inject `IFulfillmentClient`, kiểm tra tồn kho khả dụng qua `CheckStockAvailabilityAsync` trước khi thêm vào giỏ.
- [x] **TASK-06 (Cart Greyed-out)**: Nâng cấp `CartService.GetOrCreateActiveCartAsync`, `GetActiveCartByBuyerIdAsync`, `GetGroupedCartAsync`: Gọi `BatchCheckStockAsync` của Fulfillment và kiểm tra Catalog để gắn cờ `IsAvailable: false` và `DisabledReason: "OUT_OF_STOCK"` hoặc `"PRODUCT_INACTIVE"` cho từng món hàng.

---

## Phase 4: Order Service Upgrade (Giữ Kho 15m TTL & Nhả Kho)

- [x] **TASK-07 (Order Reserve Stock)**: Nâng cấp `OrderService.CreateOrderAsync`: Gọi `_fulfillmentClient.ReserveStockAsync` giữ hàng 15 phút. Nếu thất bại $\rightarrow$ ném lỗi `INSUFFICIENT_STOCK`.
- [x] **TASK-08 (Order Release Stock)**: Nâng cấp `OrderService.CancelOrderAsync` và `SellerRejectSubOrderAsync`: Gọi `_fulfillmentClient.ReleaseStockAsync` nhả hàng lại cho kho.
- [x] **TASK-09 (Order Commit Stock)**: Nâng cấp luồng thanh toán thành công trong `OrderService`: Gọi `_fulfillmentClient.CommitStockAsync` để thực trừ kho vĩnh viễn; gọi `_fulfillmentClient.CreateShipmentAsync` khi Shop chấp nhận đơn.

---

## Phase 5: Automatic Payment Timeout Worker

- [x] **TASK-10 (Background Worker)**: Tạo `Commerce.Api/Workers/PaymentTimeoutWorker.cs` kế thừa `BackgroundService`, chạy mỗi 1 phút quét các đơn `PendingPayment` quá 15 phút để tự động hủy và gọi `ReleaseStockAsync`.
- [x] **TASK-11 (Worker Registration)**: Đăng ký `services.AddHostedService<PaymentTimeoutWorker>()` trong `Commerce.Api/Program.cs`.

---

## Phase 6: Verification & Testing

- [x] **TASK-12 (Unit Tests)**: Cập nhật các Unit Tests trong `CartServiceTests.cs` và `OrderServiceTests.cs` kiểm chứng các ca kiểm thử: Hết hàng khi thêm vào giỏ, Giữ kho khi tạo đơn, Nhả kho khi hủy đơn.
- [x] **TASK-13 (Build & Test Gate)**: Chạy `dotnet build Commerce.sln -c Release` (0 error) và `dotnet test` (26/26 tests passed - 100%).
