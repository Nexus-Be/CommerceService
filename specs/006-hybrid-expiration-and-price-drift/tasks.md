# Implementation Tasks: Hybrid Payment Expiration & Asymmetric Price Drift Policy

**Feature Identifier**: `006-hybrid-expiration-and-price-drift`  
**Status**: Ready  
**Date**: 2026-11-02  

---

## Phase 1: Contracts & DTOs
- [x] Task 1.1: Định nghĩa `PriceDropDto` và bổ sung `List<PriceDropDto>? AppliedPriceDrops` vào `CreateOrderResponse` tại `Commerce.Application/Services/Order/OrderResponses.cs`.

## Phase 2: Asymmetric Price Drift Implementation
- [x] Task 2.1: Cập nhật luồng kiểm tra giá trong `OrderService.CreateOrderAsync`:
  - Phân loại `priceIncreases` và `priceDrops`.
  - Nếu có món tăng giá: Cập nhật snapshot giá mới vào `CartItems`, lưu DB, và ném `PRICE_CHANGED` (HTTP 409).
  - Nếu chỉ có món giảm giá hoặc không đổi: Tự động cập nhật giá rẻ hơn, tính lại tổng tiền, lưu danh sách `appliedPriceDrops` và tiếp tục tạo đơn.
- [x] Task 2.2: Đảm bảo sau khi đơn được lưu (`PendingPayment`), giá trong `SubOrderItems` được đóng băng vĩnh viễn (Post-Checkout Price Lock).

## Phase 3: Hybrid Expiration (Passive Guard & Active Sweeper)
- [x] Task 3.1: Viết phương thức dùng chung `ExpireOrderIfOverdueAsync` trong `OrderService.cs` (hủy đơn, nhả kho Fulfillment, giải phóng voucher `DiscountCodeUsages`).
- [x] Task 3.2: Tích hợp Passive Guard vào `GetOrderDetailAsync` và `GetSubOrderDetailAsync`.
- [x] Task 3.3: Nâng cấp `PaymentTimeoutWorker.cs` hỗ trợ quét cả `AuctionOrders` quá hạn 24 giờ và giải phóng voucher.

## Phase 4: Unit Testing & Verification
- [x] Task 4.1: Viết test case `CreateOrderAsync_ShouldAutoApplyPriceDrop_WhenItemPriceDecreases` trong `OrderServiceTests.cs`.
- [x] Task 4.2: Viết test case `CreateOrderAsync_ShouldBlockAndUpdateSnapshot_WhenAnyItemPriceIncreases` trong `OrderServiceTests.cs`.
- [x] Task 4.3: Viết test case `GetOrderDetailAsync_ShouldCancelOrderLazily_WhenPaymentOverdue` trong `OrderServiceTests.cs`.
- [x] Task 4.4: Kiểm chứng build solution `dotnet build Commerce.sln -c Release`.
- [x] Task 4.5: Chạy toàn bộ test suite `dotnet test` bảo đảm 100% pass (52/52 passed).
- [x] Task 4.6: Cập nhật tài liệu kỹ thuật `docs/COMMERCE_API_CONTRACT.md` và `docs/ENGINEERING_HANDBOOK.md`.
