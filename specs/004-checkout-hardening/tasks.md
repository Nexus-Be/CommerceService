# Implementation Tasks: Checkout Hardening & Remediation (8 Issues & Edge Cases)

**Feature Directory**: `specs/004-checkout-hardening`  
**Spec Reference**: `specs/004-checkout-hardening/spec.md`  
**Plan Reference**: `specs/004-checkout-hardening/plan.md`  

---

## Phase 1: Interfaces & DTO Updates

- [x] **TASK-01 (IWalletClient)**: Bổ sung `GetWalletBalanceAsync` và `RefundSubOrderPaymentAsync` vào `IWalletClient.cs`.
- [x] **TASK-02 (IFulfillmentClient)**: Bổ sung `ReleaseSubOrderStockAsync` vào `IFulfillmentClient.cs`.

---

## Phase 2: Infrastructure Implementations

- [x] **TASK-03 (WalletClient)**: Hiện thực `GetWalletBalanceAsync` (gọi `GET /api/v1/wallets/me/balance`) và `RefundSubOrderPaymentAsync` (gọi `POST /api/v1/wallets/refund`) trong `WalletClient.cs`.
- [x] **TASK-04 (FulfillmentClient)**:
  - Sửa `CommitStockAsync` để trả về `false` khi có lỗi/exception, triệt tiêu silent fallback.
  - Hiện thực `ReleaseSubOrderStockAsync` trong `FulfillmentClient.cs`.
- [x] **TASK-05 (DI Setup)**: Đăng ký `services.AddDistributedMemoryCache()` trong `Commerce.Infrastructure/DependencyInjection.cs`.

---

## Phase 3: Cart Service Hardening

- [x] **TASK-06 (Cart UpdateItemQuantity Stock Check - Bug C)**: Nâng cấp `CartService.UpdateItemQuantityAsync`: Kiểm tra tồn kho khả dụng từ Fulfillment trước khi cập nhật số lượng món hàng trong giỏ.

---

## Phase 4: Order Service Hardening

- [x] **TASK-07 (Distributed Cache & Atomic Pop - Issue 1 & Issue 2)**: Chuyển `_quoteCache` sang `IDistributedCache`, áp dụng Atomic Pop tại đầu hàm `CreateOrderAsync`.
- [x] **TASK-08 (Price Drift Protection - Issue 3)**: Kiểm tra lại giá và trạng thái khả dụng của SKU từ `CatalogService` trong `CreateOrderAsync`. Ném lỗi `PRICE_CHANGED` nếu lệch giá.
- [x] **TASK-09 (Non-silent Stock Commit Handling - Issue 4)**: Xử lý kết quả trả về của `CommitStockAsync` trong `CreateOrderAsync`.
- [x] **TASK-10 (Real Wallet Balance - Issue 5)**: Lấy số dư ví thực tế từ `_walletClient.GetWalletBalanceAsync` trong `QuoteCheckoutAsync`.
- [x] **TASK-11 (Targeted Sub-Order Stock Release - Bug A)**: Sửa `SellerRejectSubOrderAsync` để chỉ nhả đúng các SKU của `SubOrder` đó.
- [x] **TASK-12 (Automatic Refund on Seller Reject - Bug B)**: Sửa `SellerRejectSubOrderAsync` để tự động tạo `OrderRefund` và gọi `_walletClient.RefundSubOrderPaymentAsync` khi đơn đã thanh toán.

---

## Phase 5: Verification & Testing

- [x] **TASK-13 (Unit Tests)**: Cập nhật Unit Tests trong `CartServiceTests.cs` và `OrderServiceTests.cs` kiểm chứng các ca: kiểm tra kho khi update quantity, atomic pop quote, ném lỗi khi giá thay đổi, hoàn tiền khi seller reject.
- [x] **TASK-14 (Build & Test Verification)**: Chạy `dotnet build Commerce.sln -c Release` (0 error) và `dotnet test` (27/27 passed - 100%).
