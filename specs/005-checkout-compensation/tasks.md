# Implementation Tasks: Checkout Database Failure Rollback & Compensation Mechanism

**Feature Identifier**: `005-checkout-compensation`  
**Status**: Ready  
**Date**: 2026-11-02  

---

## Phase 1: Contract & Interface Extensions
- [x] Task 1.1: Bổ sung phương thức `RefundOrderPaymentAsync` vào interface `IWalletClient` tại `Commerce.Application/Interfaces/IWalletClient.cs`.
- [x] Task 1.2: Hiện thực phương thức `RefundOrderPaymentAsync` tại `Commerce.Infrastructure/Services/Wallet/WalletClient.cs` (gọi REST `POST api/v1/wallets/refund` với Idempotency Key).

## Phase 2: Application Layer Compensation Logic
- [x] Task 2.1: Thêm cơ chế bắt lỗi `try ... catch (Exception dbEx)` quanh bước lưu đơn `_dbContext.SaveChangesAsync` trong `Commerce.Application/Services/Order/OrderService.cs`.
- [x] Task 2.2: Triển khai luồng bù trừ kho `_fulfillmentClient.ReleaseStockAsync(orderId, CancellationToken.None)` khi bắt ngoại lệ DB.
- [x] Task 2.3: Triển khai luồng bù trừ tiền ví `_walletClient.RefundOrderPaymentAsync` khi `paid == true` với mã Idempotency Key bảo vệ `$"compensation-db-{orderId}"`.
- [x] Task 2.4: Ghi log `CRITICAL` và ném ngoại lệ chuẩn hóa `CommerceDomainException("ORDER_CREATION_FAILED", ..., 500)`.

## Phase 3: Unit Testing & Verification
- [x] Task 3.1: Viết test case mô phỏng database failure trong `tests/Commerce.UnitTests/Application/OrderServiceTests.cs`.
- [x] Task 3.2: Kiểm chứng build solution với `dotnet build Commerce.sln -c Release`.
- [x] Task 3.3: Chạy toàn bộ test suite với `dotnet test` bảo đảm 100% test pass (48/48 passed).
- [x] Task 3.4: Cập nhật tài liệu kỹ thuật `docs/ENGINEERING_HANDBOOK.md` và `docs/COMMERCE_API_CONTRACT.md`.
