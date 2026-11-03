# Feature Specification: Batch Order Expiration and Fulfillment Stock Release

**Feature Directory**: `specs/010-batch-order-expiration-release`  
**Created**: 2026-11-03  
**Status**: Completed  
**Input**: Phản hồi kỹ thuật từ người dùng / Lead Architect: Thay vì quét 1 danh sách đơn quá hạn (`Take(50)`) rồi chạy `foreach` gửi từng HTTP request riêng lẻ sang Fulfillment Service, chuyển sang gửi 1 mảng/batch các `orderIds` trong 1 HTTP request duy nhất và batch update database.

---

## 1. User Scenarios & Business Problems

### Problem Statement
Trong `Commerce.Api/Workers/PaymentTimeoutWorker.cs`:
- Background worker quét định kỳ 1 phút/lần các đơn hàng `PendingPayment` quá hạn thanh toán (`PaymentDeadlineAt < UtcNow`, tối đa 50 đơn).
- Hiện tại, worker lặp `foreach (var order in expiredOrders)` và thực thi:
  1. `await dbContext.SaveChangesAsync(ct)` 50 lần riêng rẽ.
  2. `await fulfillmentClient.ReleaseStockAsync(order.Id, ct)` 50 cuộc gọi HTTP POST nối tiếp nhau.
- **Hậu quả**: N+1 Network round-trips (50 requests $\times$ 30ms = 1.5s - 2.5s network I/O), nguy cơ nghẽn HTTP socket, lãng phí tài nguyên và làm chậm chu kỳ xử lý đơn quá hạn khi tải cao.

---

## 2. User Stories & Acceptance Criteria

### User Story 1 - Batch Stock Release Contract & Client Implementation (Priority: P1) 🎯 MVP
Là một hệ thống điều phối thương mại điện tử, tôi muốn `IFulfillmentClient` cung cấp phương thức `BatchReleaseStockAsync(List<Guid> orderIds)` để Commerce Service có thể gửi toàn bộ danh sách mã đơn hàng cần nhả kho trong 1 request duy nhất sang Fulfillment Service.

**Acceptance Criteria**:
1. **Given** danh sách `List<Guid> orderIds`, **When** `BatchReleaseStockAsync` được gọi, **Then** client gửi một HTTP POST request duy nhất tới `api/v1/fulfillment/inventory/batch-release` (hoặc `api/v1/inventories/batch-release`) với payload `{ "orderIds": [...] }`.
2. **Given** Fulfillment Service phản hồi thành công (HTTP 200/204), **When** xử lý response, **Then** client trả về `BatchReleaseStockResult` với `IsSuccess = true` và danh sách các `orderIds` đã được giải phóng kho.
3. **Given** Fulfillment Service bị lỗi mạng hoặc timeout, **When** `FailOpen = true` (chế độ phát triển/fallback), **Then** ghi log cảnh báo và không làm crash worker.
4. **Given** hàm đơn lẻ `ReleaseStockAsync(Guid orderId)` cũ, **When** khách hàng tự bấm hủy 1 đơn tại `OrderService.CancelOrderAsync`, **Then** hàm này vẫn hoạt động bình thường, không làm vỡ các use case hiện có.

---

### User Story 2 - PaymentTimeoutWorker Batch Processing Optimization (Priority: P1)
Là một Background Service chạy ngầm định kỳ, tôi muốn `PaymentTimeoutWorker` gom toàn bộ đơn quá hạn trong chu kỳ quét, cập nhật trạng thái đơn/voucher và phát lệnh nhả kho sang Fulfillment theo hình thức Batch để tối ưu hóa hiệu năng và triệt tiêu N+1 network/database calls.

**Acceptance Criteria**:
1. **Given** có $N$ đơn hàng quá hạn thanh toán ($N \le 50$), **When** worker xử lý chu kỳ, **Then** toàn bộ các đơn hàng và sub-orders được cập nhật trạng thái `Cancelled`, bản ghi `OrderCancellation` được tạo, voucher được giải phóng.
2. **Given** các thay đổi dữ liệu trong bộ nhớ, **When** commit xuống database, **Then** chỉ gọi `await dbContext.SaveChangesAsync(ct)` **1 lần duy nhất** cho toàn bộ batch thay vì gọi trong vòng lặp.
3. **Given** đã cập nhật database thành công, **When** nhả kho tại Fulfillment Service, **Then** worker chỉ gọi `await fulfillmentClient.BatchReleaseStockAsync(orderIds, ct)` **1 lần duy nhất**.
4. **Given** trường hợp danh sách đơn quá hạn rỗng ($N = 0$), **When** worker chạy, **Then** thoát nhanh (`return`) mà không gửi request rỗng sang Fulfillment.

---

## 3. Technical Requirements

- **TR-001**: Bổ sung `BatchReleaseStockResult` record vào `Commerce.Application/Interfaces/IFulfillmentClient.cs`.
- **TR-002**: Bổ sung phương thức `Task<BatchReleaseStockResult> BatchReleaseStockAsync(List<Guid> orderIds, CancellationToken ct = default)` vào `IFulfillmentClient`.
- **TR-003**: Cài đặt `BatchReleaseStockAsync` trong `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs` sử dụng `HttpClient.PostAsJsonAsync`.
- **TR-004**: Tái cấu trúc hàm `ProcessExpiredOrdersAsync` trong `Commerce.Api/Workers/PaymentTimeoutWorker.cs` để batch update DB và batch release stock.
- **TR-005**: Tuân thủ nghiêm ngặt Bounded Context Inward-Only, 100% RESTful Web API, không dùng RabbitMQ/Saga/gRPC.
- **TR-006**: Toàn bộ unit tests hiện có và unit tests mới viết bổ sung phải vượt qua 100% (`dotnet test Commerce.sln -c Release`).
