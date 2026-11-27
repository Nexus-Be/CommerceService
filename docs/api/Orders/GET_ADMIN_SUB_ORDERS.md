# COMMERCE SERVICE - GET /api/v1/admin/sub-orders

> **Feature / Use Case:** Admin Centralized Sub-Orders Query
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `GET`
- **Route Endpoint:** `/api/v1/admin/sub-orders`
- **Quyền hạn (RBAC Policy):** Admin, Staff (`[RequireRole("admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Quản trị viên và nhân viên sàn
- **Controller:** `Commerce.Api/Controllers/Public/OrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `page` | Query | `int` | Không | Min: 1 | Trang |
| `pageSize` | Query | `int` | Không | Range: 1-100 | Số bản ghi |
| `sellerId` | Query | `Guid?` | Không | UUIDv4 | Lọc shop |
| `status` | Query | `SubOrderStatus?` | Không | Enum | Trạng thái |

### 2.2 Request Body Record DTO
Không có (Phương thức không yêu cầu body payload).

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `PagedData<SubOrderSummaryResponse>`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "items": [],
  "pagination": {
    "pageIndex": 1,
    "pageSize": 20,
    "totalCount": 0,
    "totalPages": 0,
    "hasPreviousPage": false,
    "hasNextPage": false
  }
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `403 Forbidden` | `FORBIDDEN` | `You do not have permission to access this resource.` | Không phải admin/staff |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Tra cứu danh sách đơn con toàn sàn.
2. **Bước 2:** Áp dụng lọc mã vận đơn, người bán, trạng thái.
3. **Bước 3:** Phân trang database.
4. **Bước 4:** Trả về PagedData<SubOrderSummaryResponse>.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Orders/GET_ADMIN_SUB_ORDERS.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
