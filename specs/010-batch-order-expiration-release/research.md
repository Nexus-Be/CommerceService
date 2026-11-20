# Research & Technical Decisions: Phương án 3 (The Golden Standard)

**Feature**: `specs/010-batch-order-expiration-release`  
**Date**: 2026-11-03  
**Status**: Resolved  

---

## 1. Decision: Tách biệt độc lập luồng Hết hạn Thanh toán (Zero-HTTP on Expiration)

- **Quyết định**: Khi đơn hàng quá hạn 15 phút, `PaymentTimeoutWorker` của CommerceService chỉ cập nhật trạng thái đơn sang `Cancelled` và giải phóng voucher trong DB nội bộ bằng 1 lần `SaveChangesAsync`. **Không gọi HTTP sang Fulfillment.**
- **Lý do (Rationale)**:
  - Phía FulfillmentService đã có Background Service `AutoExpireReservationsAsync` quét định kỳ mỗi 60 giây, tự động nhả `reserved_quantity` về `available_quantity` cho mọi phiếu có `expiresAt < UtcNow`.
  - Việc Commerce gom mảng bắn HTTP sang Fulfillment là dư thừa (redundant), tạo nguy cơ nghẽn mạng I/O và làm sập dây chuyền nếu mạng chập chờn.
- **Giải pháp thay thế đã xem xét (Alternatives considered)**:
  - *Gọi HTTP BatchRelease:* Đòi hỏi Fulfillment phải code thêm endpoint `batch-release`, tạo thêm điểm chạm mạng không cần thiết.
  - *Gọi HTTP tuần tự từng đơn:* N+1 request gây nghẽn nghiêm trọng (50 đơn = 50 requests).

---

## 2. Decision: Model Binding mảng trần cho `POST /api/v1/inventories/by-skus`

- **Quyết định**: `FulfillmentClient` gửi request body dạng mảng JSON trần: `[ "guid-1", "guid-2" ]`.
- **Lý do**:
  - Source code thực tế trên nhánh `dev` của Fulfillment tại `Public/InventoriesController.cs` khai báo `[FromBody] IReadOnlyList<Guid> skuIds`.
  - Nếu gửi bọc `{ "skuIds": [...] }`, ASP.NET Core của Fulfillment sẽ trả về `400 Bad Request`.
- **Giải pháp thay thế**:
  - Yêu cầu Fulfillment sửa DTO: Khuyến nghị Fulfillment hỗ trợ thêm DTO bọc cho tương lai, nhưng để tương thích ngay trên `dev`, Commerce gửi mảng trần.

---

## 3. Decision: Quản lý vòng đời Phiếu giữ kho theo chuẩn RESTful State Machine

- **Quyết định**:
  - **Reserve**: `POST /api/v1/internal/inventories/reservations` với `{ referenceId, holdMinutes, items: [{ skuId, quantity }] }`.
  - **Commit**: `POST /api/v1/internal/inventories/reservations/{orderId}/commit` (Path param, Content rỗng).
  - **Release**: `POST /api/v1/internal/inventories/reservations/{orderId}/release?reason=...` (Path param, Content rỗng, chỉ gọi khi Buyer bấm Hủy).
- **Lý do**:
  - Trùng khớp 100% với Controller C# của Fulfillment (`InternalInventoryReservationsController.cs`).
  - Request body rỗng khi Commit/Release giúp giảm payload về 0 bytes, tăng tốc độ xử lý và đảm bảo tính toàn vẹn dữ liệu (trừ đúng những gì đã khóa lúc Reserve).
  - Tận dụng cam kết **Safe Idempotency (`200 OK`)** đã cài đặt trong `CommitReservationAsync` của Fulfillment.

---

## 4. Decision: Bảo mật liên dịch vụ bằng `X-Internal-Service-Key`

- **Quyết định**:
  - Cấu hình key `eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e` trong `appsettings.json` của Commerce.
  - Gắn header mặc định `X-Internal-Service-Key` cho mọi request của `FulfillmentClient`.
  - Bắt buộc kiểm tra header này tại endpoint `POST /api/v1/sub-orders/{subOrderId}/delivery-status`.
- **Lý do**:
  - `InternalServiceAuthMiddleware.cs` của Fulfillment bắt buộc có header này trên mọi route `/api/v1/internal/*`, nếu thiếu sẽ bị trả về `403 Forbidden`.
  - Bảo vệ endpoint hoàn tất đơn hàng tại Commerce chống lại việc người ngoài giả mạo xác nhận giao hàng.
