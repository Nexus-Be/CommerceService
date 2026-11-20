# Implementation Plan: Frontend Alignment & Integration Resilience

**Feature**: `029-fe-alignment-and-resilience-integration`  
**Target Projects**:
- `Commerce.Domain`
- `Commerce.Application`
- `Commerce.Infrastructure`
- `Commerce.Api`
- `tests/Commerce.UnitTests`

---

## 1. Architecture Alignment

Tuân thủ nghiêm ngặt mô hình 4 tầng Clean Architecture và các quy chuẩn bất biến của CommerceService:
1. **Không dùng Repository Pattern**: Sử dụng trực tiếp `ICommerceDbContext` / `AppDbContext`.
2. **Feature Co-location**:
   - Tầng Application: Đặt trong `Services/Order`, `Services/AuctionOrder`, `Services/Discount`, `Services/Shipment`.
   - Tầng Api: Các endpoint internal đặt trực tiếp trong `OrdersController.cs` và `AuctionOrdersController.cs`.
3. **Inward-Only**: Toàn bộ DTO, Contract, Tests đều tự cung tự cấp bên trong CommerceService.
4. **Không làm suy giảm các tính năng Spec 026 - 028**: Giữ nguyên toàn bộ logic của `RepayOrderAsync`, `GetOrderRefundsAsync`, `GetSubOrderFinancialBreakdownAsync`, và `IsEscrowSettled`.

---

## 2. Layer-by-Layer Implementation Breakdown

### 2.1 Layer 1: Domain (`Commerce.Domain`)
- **Entities**:
  - `Entities/NotificationOutboxItem.cs`: Khai báo model outbox.
  - Cập nhật `Entities/SubOrder.cs`: Thêm `ShopId`, `SellerRespondDeadlineAt`.
  - Cập nhật `Entities/AuctionOrder.cs`: Thêm `DepositId`, `WeightGramsSnapshot`, `PayoutCompletedAt`.
  - Cập nhật `Entities/OrderStatusHistory.cs`: Thêm `AuctionCallbackStatus`, `AuctionCallbackSentAt`.
- **Enums & Extensions**:
  - `Enums/NotificationOutboxStatus.cs`: `Pending`, `AwaitingData`, `Sent`, `NeedsAttention`.
  - `Enums/AuctionOrderSourceExtensions.cs`: Extension `ToWireValue()`.

### 2.2 Layer 2: Application (`Commerce.Application`)
- **Interfaces**:
  - `Interfaces/INotificationSettings.cs`: Cung cấp cờ `Enabled`.
  - Cập nhật `Interfaces/ICommerceDbContext.cs`: Thêm `DbSet<NotificationOutboxItem> NotificationOutbox { get; }`.
  - Cập nhật `Interfaces/IUserClient.cs` & `UserClientModels.cs`:
    - `Task<SellerShopResult> GetSellerShopAsync(Guid sellerUserId, CancellationToken ct = default)`.
    - `SellerShopResult(bool Found, Guid? ShopId)`.
    - `UserPenaltyViolationTypes`.
  - Cập nhật `Interfaces/IFulfillmentClient.cs`:
    - DTOs: `ShipmentCreationRequest`, `ShipmentCreationItem`.
    - Đổi chữ ký: `Task<ShipmentCreationResult> CreateShipmentAsync(ShipmentCreationRequest request, CancellationToken ct = default)`.
  - Cập nhật `Interfaces/IAuctionClient.cs`:
    - Bổ sung `OrderNo`, `TotalAmount`, `SellerRespondDeadlineAt` vào `AuctionPaymentStatusCallbackRequest`.
  - Cập nhật `Interfaces/IWalletClient.cs`:
    - `Task<WalletAuctionHoldStatus?> GetAuctionDepositHoldAsync(Guid depositId, CancellationToken ct = default)`.
- **Services & Helpers**:
  - `Services/Shipment/ShipmentRequestFactory.cs`: Factory tạo `ShipmentCreationRequest` cho SubOrder và AuctionOrder.
  - `Services/Order/DeliveryStatusTransitions.cs`: State transition guard cho webhook giao hàng.
  - `Services/Order/OrderPaidNotices.cs`: Helper enqueue outbox khi thanh toán đơn thường.
  - `Services/Order/ReviewEligibility.cs`: Helper và DTO cho review eligibility.
  - `Services/AuctionOrder/AuctionPaymentCallbackFactory.cs`: Factory chuẩn hóa callback thanh toán đấu giá.
  - Cập nhật `Services/Discount/DiscountDtos.cs`: Thêm `ProductDiscount` vào `TierSavingsDto`.
  - Cập nhật `Services/Discount/VoucherCalculationEngine.cs` & `DiscountService.cs`: Hỗ trợ voucher Product và Shop độc lập.
  - Cập nhật `Services/AuctionOrder/AuctionOrderService.cs`:
    - Thêm `GetNotificationSnapshotAsync`.
    - Cho phép `Admin`/`Staff` xem chi tiết đơn đấu giá.
    - Lưu `DepositId`, `WeightGramsSnapshot`.
  - Cập nhật `Services/Order/OrderService.cs`:
    - Thêm `GetReviewEligibilityAsync`.
    - Enqueue notification outbox khi thanh toán thành công.
    - Sử dụng `DeliveryStatusTransitions` trong `UpdateSubOrderDeliveryStatusAsync`.

### 2.3 Layer 3: Infrastructure (`Commerce.Infrastructure`)
- **Configurations**:
  - `Configurations/NotificationOutboxItemConfiguration.cs`.
  - Cập nhật configurations cho `SubOrder`, `AuctionOrder`, `OrderStatusHistory`.
- **DbContext**:
  - Cập nhật `AppDbContext.cs` thêm `DbSet<NotificationOutboxItem>`.
- **Client Implementations**:
  - `User/UserClient.cs`: Triển khai `GetSellerShopAsync`.
  - `Fulfillment/FulfillmentClient.cs`: Triển khai `CreateShipmentAsync` với `ShipmentCreationRequest`.
  - `Wallet/WalletClient.cs`: Triển khai `GetAuctionDepositHoldAsync`.

### 2.4 Layer 4: Presentation & Hosting (`Commerce.Api`)
- **Security & Filters**:
  - `Security/InternalServiceKeyGuard.cs`: So sánh `X-Internal-Service-Key` bằng `FixedTimeEquals`.
  - Cập nhật `Filters/RequireRoleAttribute.cs`: Bỏ qua nếu có `[AllowAnonymous]`.
- **Controllers**:
  - `Controllers/Public/OrdersController.cs`:
    - Thêm `GET /api/v1/internal/review-eligibility`.
    - Bảo vệ webhook delivery bằng `InternalServiceKeyGuard`.
  - `Controllers/Public/AuctionOrdersController.cs`:
    - Thêm `GET /api/v1/internal/auction-orders/{orderId:guid}/notification-snapshot`.
    - Cập nhật `GetOrderDetail` hỗ trợ Role `Admin`/`Staff`.
- **Background Workers**:
  - `Workers/NotificationOutboxWorker.cs`.
  - `Workers/AuctionCallbackRetryWorker.cs`.
  - `Workers/AuctionFinancialRetryWorker.cs`.
- **Program & DI**:
  - Đăng ký DI options, HttpClient cho Notification và 3 hosted services mới.

### 2.5 Layer 5: Testing (`tests/Commerce.UnitTests`)
- Bổ sung các file unit test từ bản FE:
  - `AuctionOrderNotificationTests.cs`
  - `DeliveryStatusTransitionsTests.cs`
  - `NotificationOutboxWorkerTests.cs`
  - `OrderPaidNotificationTests.cs`
  - `AuctionOrderSourceContractTests.cs`
  - `RequireRoleAllowAnonymousTests.cs`
  - `ReviewEligibilityTests.cs`
  - `ShipmentCreationContractTests.cs`
  - `ShippingQuoteContractTests.cs`
  - `UserPenaltyContractTests.cs`
- Đảm bảo các tests hiện có (Specs 026-028) không bị ảnh hưởng.
