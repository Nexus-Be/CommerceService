# Tasks: Frontend Alignment & Integration Resilience Implementation

**Feature**: `029-fe-alignment-and-resilience-integration`  
**Status**: Ready to Implement  

---

## Task Group 1: Domain Entities & Extensions (P1)
- [X] **Task 1.1**: Tạo `Commerce.Domain/Entities/NotificationOutboxItem.cs` kế thừa `BaseEntity<Guid>`.
- [X] **Task 1.2**: Tạo `Commerce.Domain/Enums/NotificationOutboxStatus.cs` (Pending=0, AwaitingData=1, Sent=2, NeedsAttention=3).
- [X] **Task 1.3**: Bổ sung `ShopId` và `SellerRespondDeadlineAt` vào `Commerce.Domain/Entities/SubOrder.cs`.
- [X] **Task 1.4**: Bổ sung `DepositId`, `WeightGramsSnapshot`, `PayoutCompletedAt` vào `Commerce.Domain/Entities/AuctionOrder.cs`.
- [X] **Task 1.5**: Bổ sung `AuctionCallbackStatus`, `AuctionCallbackSentAt` vào `Commerce.Domain/Entities/OrderStatusHistory.cs`.
- [X] **Task 1.6**: Tạo `Commerce.Domain/Enums/AuctionOrderSourceExtensions.cs` với `ToWireValue()`.

---

## Task Group 2: Application Contracts, DTOs & Helpers (P1)
- [X] **Task 2.1**: Tạo `Commerce.Application/Interfaces/INotificationSettings.cs`.
- [X] **Task 2.2**: Cập nhật `Commerce.Application/Interfaces/ICommerceDbContext.cs` bổ sung `DbSet<NotificationOutboxItem> NotificationOutbox { get; }`.
- [X] **Task 2.3**: Mở rộng `IUserClient.cs` và `UserClientModels.cs` với `GetSellerShopAsync`, `SellerShopResult`, `UserPenaltyViolationTypes`.
- [X] **Task 2.4**: Cập nhật `IFulfillmentClient.cs` với `ShipmentCreationRequest`, `ShipmentCreationItem` và đổi chữ ký `CreateShipmentAsync`.
- [X] **Task 2.5**: Cập nhật `IAuctionClient.cs` với `OrderNo`, `TotalAmount`, `SellerRespondDeadlineAt` trong `AuctionPaymentStatusCallbackRequest`.
- [X] **Task 2.6**: Cập nhật `IWalletClient.cs` với `GetAuctionDepositHoldAsync`.
- [X] **Task 2.7**: Tạo `Commerce.Application/Services/Shipment/ShipmentRequestFactory.cs`.
- [X] **Task 2.8**: Tạo `Commerce.Application/Services/Order/DeliveryStatusTransitions.cs`.
- [X] **Task 2.9**: Tạo `Commerce.Application/Services/Order/OrderPaidNotices.cs`.
- [X] **Task 2.10**: Tạo `Commerce.Application/Services/Order/ReviewEligibility.cs` và cập nhật `IOrderService.cs` với `GetReviewEligibilityAsync`.
- [X] **Task 2.11**: Tạo `Commerce.Application/Services/AuctionOrder/AuctionPaymentCallbackFactory.cs` và cập nhật `IAuctionOrderService.cs` với `GetNotificationSnapshotAsync`.
- [X] **Task 2.12**: Cập nhật `TierSavingsDto` trong `DiscountDtos.cs` và tinh chỉnh `VoucherCalculationEngine.cs`, `DiscountService.cs` hỗ trợ độc lập voucher Shop và SKU.

---

## Task Group 3: Infrastructure Persistence & External Clients (P2)
- [X] **Task 3.1**: Tạo `Commerce.Infrastructure/Persistence/Configurations/NotificationOutboxItemConfiguration.cs`.
- [X] **Task 3.2**: Cập nhật configurations của `SubOrder`, `AuctionOrder`, `OrderStatusHistory`.
- [X] **Task 3.3**: Cập nhật `AppDbContext.cs` ánh xạ `NotificationOutbox` và cập nhật `TestDbContextFactory.cs`.
- [X] **Task 3.4**: Triển khai `UserClient.GetSellerShopAsync` trong `Commerce.Infrastructure/Services/User/UserClient.cs`.
- [X] **Task 3.5**: Triển khai `FulfillmentClient.CreateShipmentAsync` mới trong `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs`.
- [X] **Task 3.6**: Triển khai `WalletClient.GetAuctionDepositHoldAsync` trong `Commerce.Infrastructure/Services/Wallet/WalletClient.cs`.

---

## Task Group 4: Application Services Integration (P1)
- [X] **Task 4.1**: Cập nhật `AuctionOrderService.cs`:
  - Thêm `GetNotificationSnapshotAsync`.
  - Hỗ trợ quyền `Admin`/`Staff` trong `GetOrderDetailAsync`.
  - Lưu trữ `DepositId`, `WeightGramsSnapshot`.
- [X] **Task 4.2**: Cập nhật `OrderService.cs`:
  - Triển khai `GetReviewEligibilityAsync`.
  - Gọi `OrderPaidNotices.Enqueue` khi thanh toán thành công.
  - Sử dụng `DeliveryStatusTransitions.IsAllowed` khi cập nhật trạng thái vận chuyển từ webhook.
  - Cập nhật hàm tạo vận đơn sử dụng `ShipmentRequestFactory`.

---

## Task Group 5: API Presentation, Security & Background Workers (P1)
- [X] **Task 5.1**: Tạo `Commerce.Api/Security/InternalServiceKeyGuard.cs`.
- [X] **Task 5.2**: Cập nhật `Commerce.Api/Filters/RequireRoleAttribute.cs` bỏ qua khi có `[AllowAnonymous]`.
- [X] **Task 5.3**: Thêm endpoint `GET /api/v1/internal/review-eligibility` vào `Commerce.Api/Controllers/Public/OrdersController.cs`.
- [X] **Task 5.4**: Thêm endpoint `GET /api/v1/internal/auction-orders/{orderId:guid}/notification-snapshot` vào `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`.
- [X] **Task 5.5**: Tạo `Commerce.Api/Workers/NotificationOutboxWorker.cs`.
- [X] **Task 5.6**: Tạo `Commerce.Api/Workers/AuctionCallbackRetryWorker.cs`.
- [X] **Task 5.7**: Tạo `Commerce.Api/Workers/AuctionFinancialRetryWorker.cs`.
- [X] **Task 5.8**: Cập nhật `Commerce.Api/Program.cs` đăng ký DI options, HttpClient và 3 Hosted Services.

---

## Task Group 6: Database Migration & Model Snapshot (P2)
- [X] **Task 6.1**: Tạo Migration `20261117200000_AddNotificationOutboxAndAuctionSyncFields.cs`.
- [X] **Task 6.2**: Đồng bộ `AppDbContextModelSnapshot.cs`.

---

## Task Group 7: Unit Testing & Verification (P1)
- [X] **Task 7.1**: Bổ sung bộ 10 unit test files:
  - `NotificationOutboxWorkerTests.cs`
  - `OrderPaidNotificationTests.cs`
  - `AuctionOrderNotificationTests.cs`
  - `DeliveryStatusTransitionsTests.cs`
  - `ReviewEligibilityTests.cs`
  - `ShipmentCreationContractTests.cs`
  - `RequireRoleAllowAnonymousTests.cs`
  - `AuctionOrderSourceContractTests.cs`
  - `ShippingQuoteContractTests.cs`
  - `UserPenaltyContractTests.cs`
- [X] **Task 7.2**: Biên dịch toàn bộ giải pháp: `dotnet build Commerce.sln -c Release` (0 lỗi).
- [X] **Task 7.3**: Chạy kiểm thử: `dotnet test` (100% passed, bao gồm cả Specs 026-028).
