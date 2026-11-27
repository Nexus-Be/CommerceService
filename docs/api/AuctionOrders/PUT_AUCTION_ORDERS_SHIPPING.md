# COMMERCE SERVICE - PUT /api/v1/auction-orders/{auctionOrderId}/shipping

> **Feature / Use Case:** Submit Auction Shipping Address & Quote Fee
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `PUT`
- **Route Endpoint:** `/api/v1/auction-orders/{auctionOrderId}/shipping`
- **Quyền hạn (RBAC Policy):** Buyer, Admin, Staff (`[RequireRole("buyer", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Winner sở hữu đơn đấu giá
- **Controller:** `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/AuctionOrder/IAuctionOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `auctionOrderId` | Route | `Guid` | Có | UUIDv4 | ID đơn đấu giá |

### 2.2 Request Body Record DTO
- **Tên C# Record:** `SubmitAuctionShippingRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record SubmitAuctionShippingRequest(
    [Required] ShippingAddressInput ShippingAddress
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "shippingAddress": {
    "receiverName": "Nguyen Van A",
    "contactPhone": "0901234567",
    "street": "123 Le Loi",
    "ward": "Ben Nghe",
    "province": "Ho Chi Minh"
  }
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `AuctionOrderDetailResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "orderId": "7b8c9d0e-1f2a-3b4c-5d6e-7f8a9b0c1d2e",
  "totalAmount": 19050000.0,
  "shippingFeeAmount": 50000.0
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `404 Not Found` | `AUCTION_ORDER_NOT_FOUND` | `Auction order not found.` | Không tìm thấy |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Winner nhập địa chỉ nhận hàng.
2. **Bước 2:** Gọi GHN tính cước phí.
3. **Bước 3:** Chốt TotalAmount = RemainingAmount + ShippingFee.
4. **Bước 4:** Trả về AuctionOrderDetailResponse.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/AuctionOrders/PUT_AUCTION_ORDERS_SHIPPING.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
