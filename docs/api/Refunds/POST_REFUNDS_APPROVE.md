# COMMERCE SERVICE - POST /api/v1/refunds/{id}/approve

> **Feature / Use Case:** Approve Order Refund & Credit Wallet
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/refunds/{id}/approve`
- **Quyền hạn (RBAC Policy):** Seller, Admin, Staff (`[RequireRole("seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Seller sở hữu shop hoặc Admin/Staff
- **Controller:** `Commerce.Api/Controllers/Public/RefundsController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `id` | Route | `Guid` | Có | UUIDv4 | Mã hoàn tiền |

### 2.2 Request Body Record DTO
- **Tên C# Record:** `ApproveRefundRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record ApproveRefundRequest(string? Note = null);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "note": "Refund approved."
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `RefundDetailResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "id": "r1000000-0000-0000-0000-000000000001",
  "status": "Completed"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `INVALID_REFUND_STATUS` | `Only pending refund requests can be approved.` | Không ở trạng thái Requested |
| `400 Bad Request` | `WALLET_REFUND_FAILED` | `Failed to process refund payment via wallet service.` | Lỗi hoàn tiền ví |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Xác minh quyền duyệt.
2. **Bước 2:** Gọi WalletClient hoàn tiền ví Buyer.
3. **Bước 3:** Cập nhật SubOrder sang Refunded và Refund sang Completed.
4. **Bước 4:** Trả về RefundDetailResponse.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Refunds/POST_REFUNDS_APPROVE.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
