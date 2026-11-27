# COMMERCE SERVICE - PUT /api/v1/admin/platform-fee

> **Feature / Use Case:** Update Platform Commission Fee Configuration
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `PUT`
- **Route Endpoint:** `/api/v1/admin/platform-fee`
- **Quyền hạn (RBAC Policy):** Admin (`[Authorize(Roles = "admin")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Chỉ Quản trị viên cấp cao (Admin)
- **Controller:** `Commerce.Api/Controllers/Admin/AdminPlatformFeeController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
Không có tham số route hay query bắt buộc.

### 2.2 Request Body Record DTO
- **Tên C# Record:** `UpdatePlatformFeeRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record UpdatePlatformFeeRequest(
    [Range(0, 100)] decimal DefaultPercent
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "defaultPercent": 6.5
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `PlatformFeeResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "defaultPercent": 6.5,
  "updatedByRole": "admin",
  "updatedAt": "2026-11-21T06:00:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `Fee percent must be between 0 and 100.` | Tỷ lệ phí ngoài khoảng 0 - 100% |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Thiếu JWT hợp lệ |
| `403 Forbidden` | `FORBIDDEN` | `You do not have permission to access or modify this resource.` | Người gọi không phải Admin |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Xác thực vai trò Admin của người gọi.
2. **Bước 2:** Kiểm tra giá trị phần trăm hợp lệ từ 0% đến 100%.
3. **Bước 3:** Cập nhật hoặc tạo mới bản ghi PlatformFeeConfig trong DB.
4. **Bước 4:** Trả về PlatformFeeResponse với mã 200 OK.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Admin/PUT_ADMIN_PLATFORM_FEE.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
