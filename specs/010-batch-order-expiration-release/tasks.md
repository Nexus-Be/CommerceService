# Tasks: Batch Order Expiration and Fulfillment Stock Release

**Feature Directory**: `specs/010-batch-order-expiration-release`  
**Prerequisites**: `spec.md`, `plan.md`  

---

## Phase 1: Contract Definition (Application Layer)
- [x] T001 [TR-001, TR-002] Update `Commerce.Application/Interfaces/IFulfillmentClient.cs`:
  - Thêm record `BatchReleaseStockResult(List<Guid> SucceededOrderIds, List<Guid> FailedOrderIds, bool IsSuccess = true)`.
  - Thêm method `Task<BatchReleaseStockResult> BatchReleaseStockAsync(List<Guid> orderIds, CancellationToken ct = default)` vào `IFulfillmentClient`.

## Phase 2: Client Implementation (Infrastructure Layer)
- [x] T002 [TR-003] Update `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs`:
  - Cài đặt `BatchReleaseStockAsync(List<Guid> orderIds, CancellationToken ct = default)`.
  - Gửi HTTP POST tới `api/v1/fulfillment/inventory/batch-release` mang theo payload `{ orderIds }`.
  - Hỗ trợ parsing `succeededOrderIds`, `failedOrderIds` linh hoạt từ JSON response hoặc map mặc định.
  - Xử lý `_failOpen` và exception logging an toàn.

## Phase 3: Background Worker Optimization (Api Layer)
- [x] T003 [TR-004] Refactor `Commerce.Api/Workers/PaymentTimeoutWorker.cs`:
  - Gom toàn bộ danh sách `orderIds` từ `expiredOrders`.
  - Cập nhật trạng thái `Cancelled` cho tất cả orders và sub-orders trong bộ nhớ.
  - Gom toàn bộ `OrderCancellation` records và giải phóng voucher usages trong batch.
  - Commit database bằng **1 cuộc gọi duy nhất** `await dbContext.SaveChangesAsync(ct)`.
  - Nhả kho Fulfillment bằng **1 cuộc gọi duy nhất** `await fulfillmentClient.BatchReleaseStockAsync(orderIds, ct)`.

## Phase 4: Test Suite & Verification
- [x] T004 [TR-006] Viết unit tests kiểm thử luồng Batch Release trong `tests/Commerce.UnitTests/`:
  - Kiểm thử `FulfillmentClient.BatchReleaseStockAsync` gửi request đúng cấu trúc batch.
  - Kiểm thử `PaymentTimeoutWorker` gom batch, gọi `SaveChangesAsync` 1 lần và gọi `BatchReleaseStockAsync` đúng danh sách IDs.
- [x] T005 [TR-005, TR-006] Chạy `dotnet build Commerce.sln -c Release` và `dotnet test Commerce.sln -c Release` đảm bảo 0 cảnh báo, 0 lỗi và 100% tests vượt qua.
