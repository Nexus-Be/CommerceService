# Tasks: Batch Order Expiration and Fulfillment Stock Release
## Phương án 3: Chuẩn Hóa Tối Ưu (The Golden Standard)

**Feature Directory**: `specs/010-batch-order-expiration-release`  
**Prerequisites**: `spec.md`, `plan.md`, `research.md`, `data-model.md`  

---

## Phase 1: Configuration & DI Setup
- [x] T001 [TR-001] Update `Commerce.Api/appsettings.json` and `Commerce.Api/appsettings.Development.json`:
  - Thêm section `"InternalSecurity": { "ServiceKey": "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e" }`.
- [x] T002 [TR-002] Update `Commerce.Infrastructure/DependencyInjection.cs`:
  - Đăng ký Header `X-Internal-Service-Key` mặc định trong `AddHttpClient<IFulfillmentClient, FulfillmentClient>`.

## Phase 2: Client Implementation (Infrastructure Layer)
- [x] T003 [TR-003] Update `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs`:
  - `BatchCheckStockAsync`: Gửi mảng JSON trần `skuIds` (`[uuid, uuid]`) sang `POST api/v1/inventories/by-skus`.
  - `ReserveStockAsync`: Trỏ đến `POST api/v1/internal/inventories/reservations` với payload `{ referenceId = orderId.ToString(), holdMinutes = ttlMinutes, items = items.Select(...) }`.
  - `CommitStockAsync`: Trỏ đến `POST api/v1/internal/inventories/reservations/{orderId}/commit` (Path param, Content rỗng). Thêm retry 3 lần với exponential backoff.
  - `ReleaseStockAsync`: Trỏ đến `POST api/v1/internal/inventories/reservations/{orderId}/release?reason=OrderCancelled` (Path param, Content rỗng).
  - Triệt tiêu silent fallback trả về `true` khi `_failOpen = false`.

## Phase 3: Background Worker Optimization (Api Layer)
- [x] T004 [TR-004] Refactor `Commerce.Api/Workers/PaymentTimeoutWorker.cs`:
  - Loại bỏ hoàn toàn cuộc gọi HTTP sang Fulfillment khi đơn quá hạn 15 phút (Zero-HTTP on expiration).
  - Duy trì cập nhật batch DB cho toàn bộ orders/suborders/vouchers với 1 lần `await dbContext.SaveChangesAsync(stoppingToken)`.

## Phase 4: Inbound Delivery Callback (Application & Api Layers)
- [x] T005 [TR-005] Update `Commerce.Application/Services/Order/IOrderService.cs` và `OrderService.cs`:
  - Bổ sung phương thức `Task UpdateSubOrderDeliveryStatusAsync(Guid subOrderId, string status, DateTimeOffset? deliveredAt, CancellationToken ct)`.
- [x] T006 [TR-005] Update `Commerce.Api/Controllers/Public/OrdersController.cs`:
  - Thêm endpoint `POST /api/v1/sub-orders/{subOrderId:guid}/delivery-status`.
  - Kiểm tra Header `X-Internal-Service-Key` (nếu thiếu hoặc sai thì trả về HTTP 403 `FORBIDDEN`).

## Phase 5: Test Suite & Verification
- [x] T007 [TR-006, TR-007] Cập nhật và bổ sung Unit Tests trong `tests/Commerce.UnitTests/`:
  - Test `FulfillmentClient` gọi đúng các route `reservations`, `commit`, `release`, và gửi mảng trần cho `by-skus`.
  - Test `PaymentTimeoutWorker` batch update DB mà không gọi HTTP sang Fulfillment.
  - Test `OrdersController` callback giao hàng kiểm tra key bảo mật chính xác.
- [x] T008 [TR-006, TR-007] Chạy kiểm chứng toàn bộ: `dotnet build Commerce.sln -c Release` (0 cảnh báo, 0 lỗi) và `dotnet test Commerce.sln -c Release` (100% passed).
