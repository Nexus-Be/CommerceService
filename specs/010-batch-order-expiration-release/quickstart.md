# Quickstart & Verification Guide: Phương án 3 (The Golden Standard)

**Feature**: `specs/010-batch-order-expiration-release`  
**Date**: 2026-11-03  

---

## 1. Mục Đích Kiểm Chứng

Tài liệu này cung cấp các kịch bản kiểm chứng tự động và thủ công nhằm xác nhận rằng:
1. `FulfillmentClient` giao tiếp thành công 100% với các route C# của `FulfillmentService` trên nhánh `dev`.
2. Header `X-Internal-Service-Key` được tự động đính kèm và xác thực thông suốt.
3. `PaymentTimeoutWorker` tối ưu hóa batch update DB và triệt tiêu cuộc gọi HTTP dư thừa khi hết hạn 15m.
4. Endpoint `POST /api/v1/sub-orders/{subOrderId}/delivery-status` được bảo vệ bằng key và cập nhật trạng thái đơn chính xác.

---

## 2. Các Kịch Bản Kiểm Chứng (Scenarios)

### Kịch Bản 1: Kiểm Tra Batch Check Tồn Kho (Raw Array Model Binding)
- **Hành vi:** Gọi `FulfillmentClient.BatchCheckStockAsync(skuIds)`.
- **Kỳ vọng:**
  - Gửi request `POST api/v1/inventories/by-skus` với payload là JSON mảng `["guid1", "guid2"]`.
  - Phản hồi HTTP 200 OK từ Fulfillment với DTO `InventoryItemResponse` chứa `availableQuantity`.

---

### Kịch Bản 2: Kiểm Tra Khóa Giữ Kho & Trừ Kho (Reserve & Commit)
- **Hành vi:**
  1. Khách đặt hàng $\rightarrow$ `FulfillmentClient.ReserveStockAsync`:
     - Gửi `POST api/v1/internal/inventories/reservations` kèm Header `X-Internal-Service-Key`.
     - Payload: `{ referenceId, holdMinutes: 15, items: [{ skuId, quantity }] }`.
     - Kỳ vọng: Nhận 200 OK với `ExpiresAt`.
  2. Khách thanh toán $\rightarrow$ `FulfillmentClient.CommitStockAsync`:
     - Gửi `POST api/v1/internal/inventories/reservations/{orderId}/commit` (Body rỗng).
     - Kỳ vọng: Nhận 200 OK.
  3. Retry lần 2 $\rightarrow$ Nhận 200 OK (Safe Idempotency).

---

### Kịch Bản 3: Quá Hạn 15m (PaymentTimeoutWorker Zero-HTTP Test)
- **Hành vi:** Cho 3 đơn hàng `PendingPayment` quá hạn thời gian thanh toán.
- **Kỳ vọng:**
  - `PaymentTimeoutWorker` quét và cập nhật cả 3 đơn sang `Cancelled`.
  - `DbContext.SaveChangesAsync` chỉ được gọi **1 lần duy nhất** cho toàn bộ 3 đơn.
  - **Không có cuộc gọi HTTP nào** được phát sang Fulfillment Service.

---

### Kịch Bản 4: Callback Nhận Trạng Thái Giao Hàng Thành Công
- **Hành vi:**
  - Test 4A: Gửi request thiếu `X-Internal-Service-Key` $\rightarrow$ Nhận HTTP 403 `FORBIDDEN`.
  - Test 4B: Gửi request có key hợp lệ $\rightarrow$ Nhận HTTP 200 OK, `SubOrder.Status` chuyển sang `Delivered`.

---

## 3. Lệnh Build & Test Xác Nhận

```powershell
# 1. Build toàn bộ Solution chế độ Release
dotnet build Commerce.sln -c Release

# 2. Chạy toàn bộ Unit Tests
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release
```
