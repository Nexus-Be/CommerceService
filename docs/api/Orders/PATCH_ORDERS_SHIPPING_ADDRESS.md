# COMMERCE SERVICE - PATCH /api/v1/orders/{orderId}/shipping-address

> **Feature / Use Case:** Change Master Order Shipping Address
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `PATCH`
- **Route Endpoint:** `/api/v1/orders/{orderId}/shipping-address`
- **Quyền hạn (RBAC Policy):** Buyer, Admin, Staff (`[RequireRole("buyer", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Buyer sở hữu đơn
- **Controller:** `Commerce.Api/Controllers/Public/OrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `orderId` | Route | `Guid` | Có | UUIDv4 | ID đơn cha |

### 2.2 Request Body Record DTO
- **Tên C# Record:** `ChangeShippingAddressRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record ChangeShippingAddressRequest(
    ShippingAddressInput? ShippingAddress = null,
    string? ContactPhone = null
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "contactPhone": "0912345678"
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `ChangeShippingAddressResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "targetId": "01000000-0000-0000-0000-000000000001",
  "newAddressText": "123 Nguyen Hue"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `ORDER_ALREADY_SHIPPED` | `Cannot change address after shipment is dispatched.` | Đã gửi hàng |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Xác minh tất cả đơn con chưa xuất kho.
2. **Bước 2:** Cập nhật địa chỉ nhận hàng cho toàn bộ đơn hàng cha.
3. **Bước 3:** Trả về ChangeShippingAddressResponse.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Orders/PATCH_ORDERS_SHIPPING_ADDRESS.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
