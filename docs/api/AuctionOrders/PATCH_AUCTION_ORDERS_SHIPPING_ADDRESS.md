# COMMERCE SERVICE - PATCH /api/v1/auction-orders/{auctionOrderId}/shipping-address

> **Feature / Use Case:** Change Auction Shipping Address Before Dispatch
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `PATCH`
- **Route Endpoint:** `/api/v1/auction-orders/{auctionOrderId}/shipping-address`
- **Quyền hạn (RBAC Policy):** Buyer, Admin, Staff (`[RequireRole("buyer", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Winner sở hữu đơn
- **Controller:** `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/AuctionOrder/IAuctionOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `auctionOrderId` | Route | `Guid` | Có | UUIDv4 | ID đơn đấu giá |

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
  "targetId": "7b8c9d0e-1f2a-3b4c-5d6e-7f8a9b0c1d2e",
  "newAddressText": "123 Le Loi"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `ORDER_ALREADY_SHIPPED` | `Cannot change shipping address once package is shipped.` | Đã gửi hàng |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Xác minh đơn chưa xuất kho.
2. **Bước 2:** Cập nhật địa chỉ nhận hàng và số điện thoại liên hệ.
3. **Bước 3:** Trả về ChangeShippingAddressResponse.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/AuctionOrders/PATCH_AUCTION_ORDERS_SHIPPING_ADDRESS.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
