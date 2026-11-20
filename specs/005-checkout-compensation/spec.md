# Feature Specification: Checkout Database Failure Rollback & Compensation Mechanism

**Feature Identifier**: `005-checkout-compensation`  
**Status**: In Progress  
**Priority**: CRITICAL  
**Date**: 2026-11-02  

---

## 1. Executive Summary & Problem Statement

Trong quy trình mua hàng tại Commerce Service (`POST /api/v1/orders` - `CreateOrderAsync`), hệ thống thực hiện phối hợp phân tán giữa 3 dịch vụ:
1. `FulfillmentService` (`:8005`): Giữ kho (`ReserveStockAsync`) và commit trừ kho (`CommitStockAsync`).
2. `WalletService` (`:8008`): Trừ tiền ví trực tiếp (`CheckoutDirectPaymentAsync`).
3. `PostgreSQL` (`:5435`): Lưu trữ thực thể `Order`, `SubOrder`, `SubOrderItem` và dọn dẹp `CartItems`.

### Lỗ hổng kỹ thuật (Vulnerability)
Tại thời điểm hiện tại, lệnh `_dbContext.SaveChangesAsync(ct)` nằm ở cuối phương thức `CreateOrderAsync` và không có khối bắt ngoại lệ (Exception Handling) kèm cơ chế bù trừ giao dịch (Compensation Rollback).
Nếu cơ sở dữ liệu gặp sự cố tại bước lưu (Database Timeout, Deadlock, Connection Drop, Constraint Violation):
- Đơn hàng **KHÔNG** tồn tại trong hệ thống Commerce.
- Tiền trong ví của người mua **ĐÃ BỊ TRỪ** (nếu chọn phương thức `Wallet`).
- Tồn kho vật lý tại kho GHN/Fulfillment **ĐÃ BỊ TRỪ HOẶC KHÓA** vĩnh viễn.

Hậu quả: Người mua bị mất tiền oan uổng mà không có đơn hàng để khiếu nại, nhà bán hàng bị treo tồn kho, sàn thất thoát dữ liệu đối soát.

---

## 2. User Stories & Acceptance Criteria

### User Story 1: Tự động Hoàn tiền Ví khi Lưu Database Thất bại
> **Là một** Người mua hàng (Buyer),  
> **Tôi muốn** nếu hệ thống gặp sự cố lưu đơn hàng sau khi ví của tôi đã bị trừ tiền,  
> **Thì** số tiền vừa trừ phải được tự động hoàn trả 100% về ví của tôi ngay lập tức kèm mã Idempotency Key an toàn.

#### Scenario 1.1: Database lỗi khi thanh toán qua Ví
- **GIVEN** Người mua đã lấy Quote hợp lệ và chọn phương thức thanh toán `Wallet`.
- **AND** `_walletClient.CheckoutDirectPaymentAsync` trừ ví thành công số tiền `350.000 VNĐ`.
- **WHEN** `_dbContext.SaveChangesAsync()` ném ra `DbUpdateException` hoặc `TimeoutException`.
- **THEN** Hệ thống bắt ngoại lệ, lập tức gọi `_walletClient.RefundOrderPaymentAsync` hoàn lại `350.000 VNĐ` cho người mua với `idempotencyKey = "compensation-db-{orderId}"`.
- **AND** Ném ngoại lệ `CommerceDomainException("ORDER_CREATION_FAILED", ...)` với mã HTTP 500.

---

### User Story 2: Tự động Nhả Kho Vật lý khi Lưu Database Thất bại
> **Là một** Nhà bán hàng (Seller),  
> **Tôi muốn** nếu đơn hàng không thể tạo thành công vào hệ thống,  
> **Thì** toàn bộ số lượng tồn kho đã giữ (Reserved) hoặc đã trừ (Committed) tại Fulfillment Service phải được hoàn trả lại kho ngay lập tức.

#### Scenario 2.1: Database lỗi đối với đơn COD hoặc Wallet
- **GIVEN** Người mua đặt đơn COD hoặc Wallet, hệ thống đã gọi `ReserveStockAsync` và `CommitStockAsync` tại Fulfillment Service.
- **WHEN** Lệnh `_dbContext.SaveChangesAsync()` ném lỗi.
- **THEN** Hệ thống lập tức gọi `_fulfillmentClient.ReleaseStockAsync(orderId, ct)` để giải phóng toàn bộ số lượng hàng bị khóa.
- **AND** Ghi nhận log cảnh báo mức `CRITICAL` chứa đầy đủ metadata (OrderId, BuyerId, TotalAmount) để đối soát.

---

## 3. Bounded Context & Guardrails
- **Inward-Only**: Toàn bộ thay đổi nằm trong phạm vi nội bộ `CommerceService`.
- **No-Repository Pattern**: Thao tác trực tiếp qua `ICommerceDbContext` / `AppDbContext`.
- **Clean Architecture**: Bổ sung `RefundOrderPaymentAsync` vào `IWalletClient` (Application layer), hiện thực tại `WalletClient` (Infrastructure layer).
- **Zero Financial Loss**: Bảo đảm bất biến tài chính: Không bao giờ xảy ra tình trạng "trừ tiền mà không sinh đơn".
