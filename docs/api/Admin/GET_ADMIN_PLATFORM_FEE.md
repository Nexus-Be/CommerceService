# COMMERCE SERVICE - GET /api/v1/admin/platform-fee

> **Feature / Use Case:** Retrieve Platform Commission Fee Configuration
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `GET`
- **Route Endpoint:** `/api/v1/admin/platform-fee`
- **Quyền hạn (RBAC Policy):** `Admin`, `Staff` (`[Authorize(Roles = "admin,staff")]`)
- **Controller:** `Commerce.Api/Controllers/Admin/AdminPlatformFeeController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)

### 2.1 Route Parameters & Query Parameters
Không có (Endpoint tra cứu cấu hình hệ thống).

### 2.2 Request Body Record DTO
Không có (Phương thức GET).

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)

- **Vị trí file:** `Commerce.Api/Controllers/Admin/AdminPlatformFeeController.cs`
- **Tên C# Record:** `PlatformFeeResponse`
- **Mã HTTP thành công:** `200 OK`
- **Khai báo C# Record:**
```csharp
public sealed record PlatformFeeResponse(
    decimal DefaultPercent,
    string UpdatedByRole,
    DateTimeOffset UpdatedAt
);
```

- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "defaultPercent": 5.0,
  "updatedByRole": "admin",
  "updatedAt": "2026-11-20T10:00:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Không có JWT hợp lệ |
| `403 Forbidden` | `FORBIDDEN` | `You do not have permission to access or modify this resource.` | Người gọi không phải là Admin hoặc Staff |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Kiểm tra vai trò `admin` hoặc `staff` của người gọi.
2. **Bước 2:** Đọc cấu hình phí sàn từ bảng `PlatformFeeConfig` trong DB (hoặc giá trị mặc định 5%).
3. **Bước 3:** Trả về `PlatformFeeResponse` dạng Pure DTO với mã `200 OK`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã xác nhận chuẩn phí sàn và kiểm soát phân quyền.
- [x] **Pre-Code:** Tài liệu lưu tại `docs/api/Admin/GET_ADMIN_PLATFORM_FEE.md`.
- [x] **Api:** `AdminPlatformFeeController.GetPlatformFee` trả về Pure DTO.
- [x] **Tests:** `AdminPlatformFeeTests` kiểm thử phân quyền và lấy cấu hình.
- [x] **Verify:** `dotnet test` và `dotnet build` đạt 100%.
