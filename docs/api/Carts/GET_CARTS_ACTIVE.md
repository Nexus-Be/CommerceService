# COMMERCE SERVICE - GET /api/v1/carts/active

> **Feature / Use Case:** Retrieve Active Cart by Buyer ID (Support/Admin)
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `GET`
- **Route Endpoint:** `/api/v1/carts/active`
- **Quyền hạn (RBAC Policy):** Buyer, Admin, Staff (`[RequireRole("buyer", "seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Buyer chỉ xem của mình; Admin/Staff tra cứu theo buyerId
- **Controller:** `Commerce.Api/Controllers/Public/CartsController.cs`
- **Application Contract:** `Commerce.Application/Services/Cart/ICartService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `buyerId` | Query | `Guid?` | Không | UUIDv4 | ID người mua |

### 2.2 Request Body Record DTO
Không có (Phương thức không yêu cầu body payload).

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `CartResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "cartId": "ca100000-0000-0000-0000-000000000001",
  "buyerId": "11111111-2222-3333-4444-555555555555",
  "totalItems": 0,
  "totalAmount": 0.0,
  "items": [],
  "rowVersion": 1,
  "updatedAt": "2026-11-21T06:00:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `403 Forbidden` | `FORBIDDEN` | `You do not have permission to access this resource.` | Buyer tra cứu buyerId người khác |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Kiểm tra quyền sở hữu buyerId.
2. **Bước 2:** Lấy giỏ hàng active của buyer.
3. **Bước 3:** Trả về CartResponse.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Carts/GET_CARTS_ACTIVE.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
