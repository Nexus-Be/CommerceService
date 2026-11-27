# COMMERCE SERVICE - POST /api/v1/auction-orders/settlement

> **Feature / Use Case:** Auction Settlement Ingestion
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/auction-orders/settlement`
- **Quyền hạn (RBAC Policy):** AllowAnonymous (`X-Internal-Service-Key`)
- **Quy tắc Sở hữu (Resource Ownership):** S2S từ AuctionService
- **Controller:** `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/AuctionOrder/IAuctionOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
Không có tham số route hay query bắt buộc.

### 2.2 Request Body Record DTO
- **Tên C# Record:** `AuctionSettlementRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record AuctionSettlementRequest(
    [Required] Guid AuctionId,
    [Required] Guid WinnerId,
    [Required] Guid SellerId,
    Guid? WinningBidId,
    [Required] Guid ProductId,
    [Range(0, double.MaxValue)] decimal WinningAmount,
    DateTimeOffset PaymentDeadlineAt,
    decimal? DepositAmount = null,
    AuctionOrderSource? Source = null
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "auctionId": "4f9c6d1a-8b2e-4e7a-9a1b-3c5d7e9f0a2b",
  "source": "BUY_NOW",
  "winnerId": "11111111-2222-3333-4444-555555555555",
  "sellerId": "99999999-8888-7777-6666-555555555555",
  "winningBidId": null,
  "winningAmount": 19000000.0,
  "depositAmount": 19000000.0
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `AuctionSettlementResponse`
- **Mã HTTP thành công:** `201 Created`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "orderId": "7b8c9d0e-1f2a-3b4c-5d6e-7f8a9b0c1d2e",
  "orderNo": "AO-20261121-0001",
  "status": "PendingPayment"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `INTERNAL_KEY_INVALID` | `Internal service authentication is required.` | Sai key |
| `400 Bad Request` | `VAL_INVALID_SETTLEMENT` | `Winning bid ID is required for winner and second-chance settlements.` | Thiếu WinningBidId khi không phải BuyNow |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Xác thực khóa bảo mật nội bộ.
2. **Bước 2:** Kiểm tra idempotency an toàn (null-safe).
3. **Bước 3:** Tạo AuctionOrder ở trạng thái PendingPayment.
4. **Bước 4:** Trả về 201 Created.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/AuctionOrders/POST_AUCTION_ORDERS_SETTLEMENT.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
