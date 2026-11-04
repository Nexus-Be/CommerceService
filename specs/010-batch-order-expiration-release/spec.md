# Feature Specification: Batch Order Expiration and Fulfillment Stock Release
## Phương án 3: Chuẩn Hóa Tối Ưu (The Golden Standard)

**Feature Directory**: `specs/010-batch-order-expiration-release`  
**Created**: 2026-11-03  
**Status**: In Progress  
**Input**: Phản hồi kỹ thuật từ người dùng / Lead Architect: Chuẩn hóa toàn diện cơ chế giao tiếp liên dịch vụ giữa CommerceService và FulfillmentService, phân tách rõ trách nhiệm tự chủ dữ liệu, triệt tiêu N+1 network calls và loại bỏ hoàn toàn các cuộc gọi HTTP dư thừa khi đơn quá hạn thanh toán.

---

## Clarifications

### Session 2026-11-03
- Q: Khi đơn hàng quá hạn 15 phút thanh toán, CommerceService sẽ xử lý việc giải phóng tồn kho với FulfillmentService theo cơ chế nào? → A: Option A (Tách biệt độc lập: FulfillmentService tự động quét nhả kho sau 15 phút qua Background Service `AutoExpireReservationsAsync`. `PaymentTimeoutWorker` của CommerceService chỉ cập nhật trạng thái đơn nội bộ sang `Cancelled` và giải phóng voucher trong DB Commerce với 1 lần `SaveChangesAsync` duy nhất, không thực hiện cuộc gọi HTTP nào sang Fulfillment. CommerceService chỉ gọi API nhả kho `POST /api/v1/internal/inventories/reservations/{orderId}/release?reason=...` khi Buyer chủ động bấm nút "Hủy đơn" trên App).
- Q: Endpoint nhận thông báo giao hàng thành công từ Fulfillment `POST /api/v1/sub-orders/{subOrderId}/delivery-status` sẽ áp dụng cơ chế xác thực nào để bảo vệ dữ liệu đơn hàng? → A: Option A (Xác thực qua Internal Service Key: Bắt buộc request từ Fulfillment phải đính kèm Header `X-Internal-Service-Key`. Nếu thiếu hoặc sai key, CommerceService trả về HTTP 403 `FORBIDDEN` để chống giả mạo trạng thái đơn).
- Q: Khi người mua đã thanh toán tiền thành công nhưng cuộc gọi `CommitStock` sang FulfillmentService bị rớt mạng hoặc timeout, CommerceService sẽ xử lý phục hồi như thế nào? → A: Option A (Tự động Retry + Đối soát nền: Thực hiện retry tức thì 3 lần với exponential backoff. Nếu vẫn mất mạng, lưu cờ để Background Worker tiếp tục retry định kỳ cho đến khi nhận HTTP 200 OK từ Fulfillment, tận dụng cam kết Safe Idempotency của Fulfillment để tránh bán trùng hàng).

---

## 1. User Scenarios & Business Problems

### Problem Statement
Trước đây trong `PaymentTimeoutWorker.cs` và `FulfillmentClient.cs`:
1. **Lệch Model Binding & Route**:
   - `POST /api/v1/inventories/by-skus`: Fulfillment nhận mảng trần `[uuid]`, Commerce gửi bọc `{ skuIds: [...] }` gây lỗi 400.
   - `POST /api/v1/internal/inventories/reservations`: Yêu cầu Header `X-Internal-Service-Key`, DTO nhận `referenceId` và `holdMinutes`, trong khi Commerce đang gọi sai route cũ.
   - `Commit` và `Release`: Fulfillment nhận `referenceId` trên URL path, không nhận request body.
2. **N+1 Network round-trips & Thừa thãi liên dịch vụ**:
   - Background worker lặp `foreach (var order in expiredOrders)` và thực thi SaveChanges + HTTP calls riêng rẽ.
   - Cả 2 service cùng chạy worker quét để làm cùng 1 việc nhả kho, tạo ra các cuộc gọi HTTP dư thừa gây nghẽn socket và nguy cơ sập dây chuyền.
3. **Thiếu Endpoint Nhận Webhook Giao Hàng Thành Công**:
   - Fulfillment khi nhận Webhook GHN giao thành công cần báo về Commerce để kết thúc vòng đời SubOrder, nhưng Commerce chưa có endpoint callback được bảo vệ an toàn.
4. **Rủi ro Mất Đồng Bộ Khi Commit Thất Bại**:
   - Khi khách đã trả tiền nhưng mạng rớt lúc gọi `CommitStock`, nếu không có cơ chế tự động thử lại thì sau 15 phút Fulfillment sẽ tự nhả kho, khiến khách mất hàng dù đã thanh toán.

---

## 2. User Stories & Acceptance Criteria

### User Story 1 - Đồng bộ 100% Client Giao tiếp Kho (`FulfillmentClient`) (Priority: P1) 🎯 MVP
Là một hệ thống điều phối thương mại điện tử, tôi muốn `IFulfillmentClient` gọi đúng các route nội bộ của Fulfillment Service với Header `X-Internal-Service-Key` và cấu trúc Request/Response chuẩn hóa.

**Acceptance Criteria**:
1. **Given** danh sách `skuIds`, **When** `BatchCheckStockAsync` được gọi, **Then** client gửi một HTTP POST request với body là mảng JSON trần `[ "guid1", "guid2" ]` tới `api/v1/inventories/by-skus`.
2. **Given** yêu cầu giữ kho cho đơn hàng mới, **When** `ReserveStockAsync` được gọi, **Then** client gửi HTTP POST tới `api/v1/internal/inventories/reservations` với Header `X-Internal-Service-Key` và payload `{ referenceId: orderId, holdMinutes: 15, items: [{ skuId, quantity }] }`.
3. **Given** đơn hàng đã thanh toán thành công, **When** `CommitStockAsync` được gọi, **Then** client gửi HTTP POST tới `api/v1/internal/inventories/reservations/{orderId}/commit` (URL path parameter, body rỗng) kèm Header `X-Internal-Service-Key`.
4. **Given** cuộc gọi `CommitStockAsync` gặp sự cố mạng hoặc timeout, **When** thực thi trừ kho, **Then** client tự động retry tối đa 3 lần với exponential backoff (200ms, 400ms, 800ms) trước khi trả về kết quả lỗi để kích hoạt đối soát nền.
5. **Given** người mua chủ động bấm "Hủy đơn" trên App, **When** `ReleaseStockAsync` được gọi, **Then** client gửi HTTP POST tới `api/v1/internal/inventories/reservations/{orderId}/release?reason=OrderCancelled` (URL path parameter, body rỗng) kèm Header `X-Internal-Service-Key`.
6. **Given** dịch vụ kho gặp sự cố mạng hoặc trả về 5xx/4xx, **When** `_failOpen = false`, **Then** client quăng lỗi chuẩn RFC 9110 (`STOCK_CHECK_UNAVAILABLE` hoặc `STOCK_RESERVATION_FAILED`), không ngầm định còn hàng.

---

### User Story 2 - Tối Ưu Hóa PaymentTimeoutWorker (Zero-HTTP on Expiration) (Priority: P1)
Là một Background Service chạy ngầm định kỳ, tôi muốn `PaymentTimeoutWorker` gom toàn bộ đơn quá hạn trong chu kỳ quét, cập nhật trạng thái đơn/voucher trong database Commerce bằng 1 lần `SaveChangesAsync` duy nhất và không gửi request nhả kho dư thừa sang Fulfillment (để Fulfillment tự quét nhả kho độc lập).

**Acceptance Criteria**:
1. **Given** có $N$ đơn hàng quá hạn thanh toán ($N \le 50$), **When** worker xử lý chu kỳ, **Then** toàn bộ các đơn hàng và sub-orders được cập nhật trạng thái `Cancelled`, bản ghi `OrderCancellation` được tạo, voucher được giải phóng.
2. **Given** các thay đổi dữ liệu trong bộ nhớ, **When** commit xuống database, **Then** chỉ gọi `await dbContext.SaveChangesAsync(ct)` **1 lần duy nhất** cho toàn bộ batch.
3. **Given** quy tắc phân chia trách nhiệm (Separation of Concerns), **When** worker hủy đơn quá hạn, **Then** worker **không thực hiện cuộc gọi HTTP nào** sang Fulfillment Service, tin tưởng vào Background Service `AutoExpireReservationsAsync` của kho đã tự nhả hàng sau 15 phút.
4. **Given** trường hợp danh sách đơn quá hạn rỗng ($N = 0$), **When** worker chạy, **Then** thoát nhanh (`return`) mà không tốn tài nguyên DB.

---

### User Story 3 - Bảo Vệ Endpoint Callback Giao Hàng Thành Công (Priority: P2)
Là một quản trị viên hệ thống, tôi muốn endpoint `POST /api/v1/sub-orders/{subOrderId}/delivery-status` được bảo vệ bằng `X-Internal-Service-Key` để chỉ máy chủ Fulfillment mới có thể cập nhật trạng thái đơn hàng sang `Delivered`.

**Acceptance Criteria**:
1. **Given** request từ Fulfillment thiếu Header `X-Internal-Service-Key` hoặc sai giá trị key, **When** gọi endpoint callback, **Then** hệ thống trả về HTTP 403 `FORBIDDEN`.
2. **Given** request hợp lệ với key chính xác và status `Delivered`, **When** xử lý callback, **Then** cập nhật `SubOrder.Status = SubOrderStatus.Delivered` và lưu thời gian `DeliveredAt`.

---

## 3. Technical Requirements

- **TR-001**: Cấu hình `InternalSecurity:ServiceKey` trong `appsettings.json` và `appsettings.Development.json` khớp với key của Fulfillment (`eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e`).
- **TR-002**: Đăng ký Header `X-Internal-Service-Key` mặc định cho `HttpClient` của `IFulfillmentClient` trong `Commerce.Infrastructure/DependencyInjection.cs`.
- **TR-003**: Cập nhật các phương thức `BatchCheckStockAsync`, `ReserveStockAsync`, `CommitStockAsync`, `ReleaseStockAsync` trong `Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs`. Bổ sung Polly / retry policy 3 lần cho `CommitStockAsync`.
- **TR-004**: Tối ưu hóa `PaymentTimeoutWorker.cs` để batch update DB và loại bỏ HTTP calls dư thừa khi hết hạn.
- **TR-005**: Thêm action `UpdateSubOrderDeliveryStatus` vào `OrdersController.cs` và `OrderService.cs` với kiểm tra `X-Internal-Service-Key`.
- **TR-006**: Tuân thủ nghiêm ngặt Bounded Context Inward-Only, 100% RESTful Web API, không dùng RabbitMQ/Saga/gRPC.
- **TR-007**: Toàn bộ unit tests hiện có và unit tests mới viết bổ sung phải vượt qua 100% (`dotnet test Commerce.sln -c Release`).
