# COMMERCE SERVICE - POST /api/v1/orders/{orderId}/accept

> **Feature / Use Case:** Seller Accept Sub-Order & Generate Shipment
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/orders/{orderId}/accept`
- **Quyền hạn (RBAC Policy):** Buyer, Seller, Admin, Staff (`[RequireRole("buyer", "seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Seller sở hữu shop
- **Controller:** `Commerce.Api/Controllers/Public/OrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `orderId` | Route | `Guid` | Có | UUIDv4 | ID đơn cha |

### 2.2 Request Body Record DTO
- **Tên C# Record:** `SellerAcceptOrderRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record SellerAcceptOrderRequest([Required] Guid SubOrderId, string? Note = null);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "subOrderId": "02000000-0000-0000-0000-000000000001"
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `SellerAcceptOrderResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "subOrderId": "02000000-0000-0000-0000-000000000001",
  "subOrderNo": "SUB-20261127-000001",
  "status": "Confirmed",
  "acceptedAt": "2026-11-27T09:00:00Z",
  "trackingCode": "GHN-SGN-1029384",
  "carrierCode": "GHN",
  "labelPrintUrl": "https://dev-online-gateway.ghn.vn/a5/public-api/printA5?token=GHN-SGN-1029384"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `INVALID_STATUS` | `Sub-order cannot be accepted in its current status.` | Đơn chưa thanh toán (Wallet) hoặc không ở trạng thái cho phép duyệt |
| `403 Forbidden` | `FORBIDDEN` | `You do not have permission to manage this sub-order.` | Không phải Seller sở hữu đơn con |
| `404 Not Found` | `SUB_ORDER_NOT_FOUND` | `SubOrder not found.` | Không tìm thấy đơn con |
| `422 Unprocessable Entity` | `SELLER_WAREHOUSE_MISSING` | `The seller has no active warehouse yet, so no shipment can be created. Set up the warehouse, then retry.` | Seller chưa có kho hàng |
| `422 Unprocessable Entity` | `SELLER_WAREHOUSE_INCOMPLETE` | `The seller's warehouse has no pickup location yet, so no shipment can be created. Complete the warehouse address, then retry.` | Kho thiếu địa chỉ GHN |
| `503 Service Unavailable` | `FULFILLMENT_SERVICE_UNAVAILABLE` | `Unable to create shipment with fulfillment service.` | Lỗi kết nối Fulfillment/GHN (giữ nguyên trạng thái đơn) |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Seller duyệt đơn con, xác thực quyền sở hữu và trạng thái hợp lệ.
2. **Bước 2:** Gọi Fulfillment/GHN tạo vận đơn chuyển phát trước khi đổi trạng thái; nếu thất bại trả lỗi ngay.
3. **Bước 3:** Lưu `TrackingCode`, `CarrierCode`, `LabelPrintUrl` và chuyển `SubOrder` sang `Confirmed`.
4. **Bước 4:** Trả về `SellerAcceptOrderResponse`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Orders/POST_ORDERS_ACCEPT.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
