# COMMERCE SERVICE - GET /api/v1/internal/orders/dispute-eligibility

> **Feature / Use Case:** Verify Dispute Eligibility for UserService
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `GET`
- **Route Endpoint:** `/api/v1/internal/orders/dispute-eligibility`
- **Quyền hạn (RBAC Policy):** AllowAnonymous (`X-Internal-Service-Key`)
- **Quy tắc Sở hữu (Resource Ownership):** S2S từ UserService
- **Controller:** `Commerce.Api/Controllers/Public/OrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `orderOrSubOrderId` | Query | `Guid` | Có | UUIDv4 | ID đơn |
| `reporterId` | Query | `Guid` | Có | UUIDv4 | Người khiếu nại |
| `reportedUserId` | Query | `Guid` | Có | UUIDv4 | Người bị khiếu nại |

### 2.2 Request Body Record DTO
Không có (Phương thức không yêu cầu body payload).

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `DisputeEligibilityDto`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "isEligible": true,
  "orderType": "SubOrder",
  "status": "Delivered"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `INTERNAL_KEY_INVALID` | `Internal service authentication is required.` | Sai key |
| `404 Not Found` | `ORDER_NOT_FOUND` | `Order or sub-order was not found or users do not match.` | Không khớp |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Xác thực khóa bảo mật.
2. **Bước 2:** Xác minh quan hệ 2 chiều giữa 2 user.
3. **Bước 3:** Kiểm tra trạng thái đơn phải là Delivered hoặc Completed.
4. **Bước 4:** Trả về DisputeEligibilityDto.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Orders/GET_INTERNAL_ORDERS_DISPUTE_ELIGIBILITY.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
