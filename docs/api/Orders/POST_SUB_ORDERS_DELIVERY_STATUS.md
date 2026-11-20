# COMMERCE SERVICE - POST /api/v1/sub-orders/{subOrderId}/delivery-status

> **Feature / Use Case:** Logistics Delivery Webhook Ingestion & Status Transition
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/sub-orders/{subOrderId:guid}/delivery-status`
- **Quyền hạn (RBAC Policy):** `AllowAnonymous` (Protected by `X-Internal-Service-Key`)
- **Bảo mật:** `InternalServiceKeyGuard.Check(Request, configuration)` xác thực qua header `X-Internal-Service-Key`.
- **Caller Service:** `FulfillmentService` / GHN Webhook Dispatcher.
- **Controller:** `Commerce.Api/Controllers/Public/OrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)

### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `subOrderId` | Route | `Guid` | Có | UUIDv4 format | Mã đơn hàng con hoặc đơn đấu giá |
| `X-Internal-Service-Key` | Header | `string` | Có | Chuỗi bí mật nội bộ | Khóa bảo mật giao tiếp nội bộ |

### 2.2 Request Body Record DTO
- **Vị trí file:** `Commerce.Application/Services/Order/OrderRequests.cs`
- **Tên C# Record:** `UpdateDeliveryStatusRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record UpdateDeliveryStatusRequest(
    [Required] string Status,
    DateTimeOffset? DeliveredAt = null,
    string? CarrierCode = null,
    string? TrackingCode = null,
    string? Note = null,
    string? Location = null,
    string? PodUrl = null,
    string? ShipperName = null,
    string? ShipperPhone = null
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "status": "Returning",
  "carrierCode": "GHN",
  "trackingCode": "GHN-SGN-8849201",
  "note": "Package returning to seller after 3 unsuccessful delivery attempts.",
  "location": "Tan Binh Hub, Ho Chi Minh City"
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)

- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "subOrderId": "02000000-0000-0000-0000-000000000001",
  "status": "Returning",
  "updatedAt": "2026-11-21T03:40:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `INTERNAL_KEY_INVALID` | `Internal service authentication is required.` | Thiếu hoặc sai header `X-Internal-Service-Key` |
| `404 Not Found` | `SUB_ORDER_NOT_FOUND` | `SubOrder was not found.` | Đơn hàng con không tồn tại trong hệ thống |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `One or more validation errors occurred.` | Thiếu trạng thái giao hàng `Status` |
| `503 Service Unavailable` | `INTERNAL_CONFIGURATION_ERROR` | `Internal service authentication is not configured.` | Khóa bảo mật nội bộ chưa được cấu hình |

### 4.1 Bảng Ánh xạ Trạng thái Giao vận
| Giá trị Webhook | Trạng thái SubOrder C# | Hành động hệ thống |
| :--- | :--- | :--- |
| `ReadyToPick` / `Picking` / `Packed` | Không đổi (`Confirmed`) | Ghi lịch sử chuẩn bị đơn hàng |
| `HandedOver` / `InTransit` / `Shipped` | `SubOrderStatus.Shipped` (3) | Chuyển sang Đang giao hàng, lưu thời điểm giao |
| `Delivered` | `SubOrderStatus.Delivered` (4) | Chuyển sang Đã giao hàng, đồng bộ đơn hàng cha |
| `DeliveryFailed` | Giữ nguyên `Shipped` (3) | **Interim attempt**: Ghi lịch sử giao thất bại, không hủy đơn sớm |
| `Returning` | `SubOrderStatus.Returning` (8) | Ghi nhận kiện hàng đang trong quá trình quay đầu về kho người bán |
| `Returned` | `SubOrderStatus.Cancelled` (6) | Chuyển sang Đã hủy, nhả kho, hoàn tiền ví nếu đã trả trước, và **trừ 5 điểm uy tín Buyer** |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Xác thực `X-Internal-Service-Key` qua `InternalServiceKeyGuard`.
2. **Bước 2:** Tra cứu `SubOrder` (hoặc `AuctionOrder` fallback).
3. **Bước 3:** Kiểm tra thứ tự chuyển trạng thái qua `DeliveryStatusTransitions.IsAllowed`. Bỏ qua các sự kiện trùng lặp trong 2 giây.
4. **Bước 4:** Cập nhật trạng thái tương ứng:
   - Với `"Returning"`: chuyển sang `SubOrderStatus.Returning`, cập nhật `UpdatedAt`, ghi `OrderStatusHistory`.
   - Với `"DeliveryFailed"`: giữ nguyên `Shipped`, chỉ ghi `OrderStatusHistory`.
   - Với `"Returned"`: chuyển sang `Cancelled`, tạo `OrderCancellation`, gọi `_walletClient.RefundSubOrderPaymentAsync` nếu đã thanh toán, gọi `_fulfillmentClient.ReleaseSubOrderStockAsync`, và gọi `_userClient.AdjustReputationAsync` (trừ 5 điểm uy tín người mua).
5. **Bước 5:** Trả về `200 OK` kèm thông tin xác nhận cập nhật.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã thống nhất với tài liệu kỹ thuật của Fulfillment Service và GHN Gateway.
- [x] **Pre-Code:** Tài liệu lưu tại `docs/api/Orders/POST_SUB_ORDERS_DELIVERY_STATUS.md`.
- [x] **Enum:** `SubOrderStatus.Returning = 8` đã khai báo và cấu hình DB.
- [x] **Application:** `OrderService.UpdateSubOrderDeliveryStatusAsync` xử lý đầy đủ các nhánh.
- [x] **Tests:** `OrderDeliveryStatusTests` kiểm thử 100% các nhánh `Returning`, `DeliveryFailed`, `Returned` và trừ điểm uy tín.
- [x] **Verify:** `dotnet test` vượt qua 100% pass rate.
