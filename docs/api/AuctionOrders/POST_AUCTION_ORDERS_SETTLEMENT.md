# COMMERCE SERVICE - POST /api/v1/auction-orders/settlement

> **Feature / Use Case:** Auction Settlement Ingestion (Winner, Runner-up & Buy Now)
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/auction-orders/settlement`
- **Quyền hạn (RBAC Policy):** `AllowAnonymous` (Protected by `X-Internal-Service-Key`)
- **Bảo mật:** `InternalServiceKeyGuard.Check(Request, configuration)` xác thực qua header `X-Internal-Service-Key`.
- **Caller Service:** `AuctionService` (chốt phiên đấu giá hoặc phiên Mua ngay).
- **Controller:** `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/AuctionOrder/IAuctionOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)

### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `X-Internal-Service-Key` | Header | `string` | Có | Chuỗi bí mật nội bộ | Khóa bảo mật giao tiếp giữa các service |

### 2.2 Request Body Record DTO
- **Vị trí file:** `Commerce.Application/Services/AuctionOrder/AuctionOrderRequests.cs`
- **Tên C# Record:** `AuctionSettlementRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record AuctionSettlementRequest(
    [Required] Guid AuctionId,
    [Required] Guid WinnerId,
    [Required] Guid SellerId,
    Guid? WinningBidId, // Nullable for BUY_NOW
    [Required] Guid ProductId,
    Guid? SkuId,
    [Required, StringLength(255)] string ProductName,
    string? ProductImageUrl,
    string? SkuAttributes,
    [Range(0, double.MaxValue)] decimal WinningAmount,
    DateTimeOffset PaymentDeadlineAt,
    decimal? DepositAmount = null,
    AuctionOrderSource? Source = null,
    ShippingAddressInput? ShippingAddress = null,
    string? ContactPhone = null
);
```

- **JSON Request Body mẫu (BUY_NOW):**
```json
{
  "auctionId": "4f9c6d1a-8b2e-4e7a-9a1b-3c5d7e9f0a2b",
  "source": "BUY_NOW",
  "winnerId": "11111111-2222-3333-4444-555555555555",
  "sellerId": "99999999-8888-7777-6666-555555555555",
  "winningBidId": null,
  "productId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "productName": "Apple Watch Ultra 2",
  "winningAmount": 19000000.0,
  "depositAmount": 19000000.0,
  "paymentDeadlineAt": "2026-11-22T12:00:00Z"
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)

- **Vị trí file:** `Commerce.Application/Services/AuctionOrder/AuctionOrderResponses.cs`
- **Tên C# Record:** `AuctionSettlementResponse`
- **Mã HTTP thành công:** `201 Created` (hoặc `200 OK` nếu idempotent)
- **Khai báo C# Record:**
```csharp
public sealed record AuctionSettlementResponse(
    Guid OrderId,
    string OrderNo,
    Guid AuctionId,
    Guid WinnerId,
    string Status,
    DateTimeOffset PaymentDeadlineAt
);
```

- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "orderId": "7b8c9d0e-1f2a-3b4c-5d6e-7f8a9b0c1d2e",
  "orderNo": "AO-20261121-0001",
  "auctionId": "4f9c6d1a-8b2e-4e7a-9a1b-3c5d7e9f0a2b",
  "winnerId": "11111111-2222-3333-4444-555555555555",
  "status": "PendingPayment",
  "paymentDeadlineAt": "2026-11-22T12:00:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `INTERNAL_KEY_INVALID` | `Internal service authentication is required.` | Thiếu hoặc sai header `X-Internal-Service-Key` |
| `400 Bad Request` | `VAL_INVALID_SETTLEMENT` | `Winning bid ID is required for winner and second-chance settlements.` | `WinningBidId` bị thiếu/null khi nguồn không phải là BuyNow |
| `409 Conflict` | `ORDER_CONCURRENCY_CONFLICT` | `An auction order for this session already exists with different details.` | Trùng `AuctionId` nhưng dữ liệu thanh toán không khớp |
| `503 Service Unavailable` | `INTERNAL_CONFIGURATION_ERROR` | `Internal service authentication is not configured.` | Khóa bí mật nội bộ chưa được thiết lập |

### 4.1 Bảng Ánh xạ Enum liên quan
| Tên Enum C# | Giá trị DB (Integer) | Giá trị API (PascalCase) | Ý nghĩa nghiệp vụ |
| :--- | :---: | :--- | :--- |
| `AuctionOrderSource.Winner` | `0` | `"Winner"` | Thắng đấu giá bậc thang tiêu chuẩn |
| `AuctionOrderSource.SecondChance` | `1` | `"SecondChance"` | Runner-up nhận quyền mua khi winner quá hạn |
| `AuctionOrderSource.BuyNow` | `2` | `"BuyNow"` | Mua ngay với giá chốt phiên |
| `AuctionOrderStatus.PendingPayment` | `0` | `"PendingPayment"` | Chờ người mua thanh toán tiền hàng còn lại và phí ship |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Xác thực bảo mật nội bộ qua `InternalServiceKeyGuard.Check`.
2. **Bước 2:** Validate quy tắc nghiệp vụ: Nếu `source != AuctionOrderSource.BuyNow`, bắt buộc `WinningBidId` phải có giá trị.
3. **Bước 3:** Kiểm tra Idempotency: Nếu đơn cho `AuctionId` đã tồn tại, kiểm tra an toàn so sánh giá trị và `WinningBidId` (xử lý null-safe). Nếu khớp, trả về `200 OK`.
4. **Bước 4:** Tạo thực thể `AuctionOrder` với `RemainingAmount = Math.Max(0, WinningAmount - DepositAmount)`.
5. **Bước 5:** Lưu vào database và trả về `201 Created` kèm `AuctionSettlementResponse`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã thống nhất tài liệu tích hợp giữa AuctionService và CommerceService.
- [x] **Pre-Code:** Tài liệu lưu tại `docs/api/AuctionOrders/POST_AUCTION_ORDERS_SETTLEMENT.md`.
- [x] **Persistence:** Migration `20261121000000_MakeAuctionOrderWinningBidIdNullable` đã hoàn thiện.
- [x] **Application:** `AuctionOrderService.ReceiveSettlementAsync` hỗ trợ nullable `WinningBidId`.
- [x] **Tests:** `AuctionOrderServiceTests` bao phủ 100% kịch bản Buy Now, Winner, và Idempotency.
- [x] **Verify:** `dotnet test` và `dotnet build` đạt 100% pass rate.
