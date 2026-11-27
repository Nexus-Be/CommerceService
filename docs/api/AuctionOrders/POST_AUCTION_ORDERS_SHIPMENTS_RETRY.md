# COMMERCE SERVICE - POST /api/v1/auction-orders/{id}/shipments/retry

> **Feature / Use Case:** Retry Failed Shipment Creation with GHN
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/auction-orders/{id}/shipments/retry`
- **Quyền hạn (RBAC Policy):** Seller, Admin, Staff (`[RequireRole("seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Seller sở hữu đơn
- **Controller:** `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/AuctionOrder/IAuctionOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `id` | Route | `Guid` | Có | UUIDv4 | ID đơn đấu giá |

### 2.2 Request Body Record DTO
Không có (Phương thức không yêu cầu body payload).

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `RetryShipmentResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "orderId": "7b8c9d0e-1f2a-3b4c-5d6e-7f8a9b0c1d2e",
  "trackingCode": "GHN-RETRY-01"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `SHIPMENT_ALREADY_EXISTS` | `Shipment has already been created for this auction order.` | Đã có mã vận đơn ở Commerce hoặc đã tồn tại bên Fulfillment (tự đồng bộ lại nếu thiếu) |
| `400 Bad Request` | `INVALID_STATUS` | `Only paid or confirmed auction orders can have carrier shipments created.` | Trạng thái đơn không hợp lệ |
| `503 Service Unavailable` | `FULFILLMENT_SERVICE_UNAVAILABLE` | `Unable to reach fulfillment service to verify shipment status.` | Lỗi kết nối Fulfillment/GHN |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Kiểm tra `TrackingCode` cục bộ và gọi `GET /api/v1/internal/shipments/order/{auctionOrderId}` sang Fulfillment; nếu đã có vận đơn thì đồng bộ lại và báo `SHIPMENT_ALREADY_EXISTS`.
2. **Bước 2:** Nếu chưa có vận đơn, gọi Fulfillment/GHN tạo vận đơn mới.
3. **Bước 3:** Lưu `TrackingCode`, `CarrierCode`, `LabelPrintUrl` và đặt trạng thái `Confirmed`.
4. **Bước 4:** Trả về `RetryAuctionShipmentResponse`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/AuctionOrders/POST_AUCTION_ORDERS_SHIPMENTS_RETRY.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
