# COMMERCE SERVICE - GET /api/v1/refunds

> **Feature / Use Case:** List Order Refund Requests
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `GET`
- **Route Endpoint:** `/api/v1/refunds`
- **Quyền hạn (RBAC Policy):** `Buyer`, `Seller`, `Admin`, `Staff` (`[RequireRole("buyer", "seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Buyer xem các yêu cầu hoàn tiền của mình; Seller xem các yêu cầu hoàn tiền thuộc Shop của mình; Admin/Staff xem toàn sàn.
- **Controller:** `Commerce.Api/Controllers/Public/RefundsController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)

### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `page` | Query | `int` | Không | Min: 1, Default: 1 | Số trang phân trang |
| `pageSize` | Query | `int` | Không | Range: 1 - 100, Default: 20 | Số bản ghi mỗi trang |
| `status` | Query | `RefundStatus?` | Không | PascalCase string | Lọc theo trạng thái hoàn tiền |
| `subOrderId` | Query | `Guid?` | Không | UUIDv4 format | Lọc theo đơn hàng con cụ thể |

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)

- **Vị trí file:** `Commerce.Application/Services/Order/OrderResponses.cs`
- **Tên C# Record:** `PagedData<RefundDetailResponse>`
- **Mã HTTP thành công:** `200 OK`
- **Khai báo C# Record:**
```csharp
public sealed record RefundDetailResponse(
    Guid Id,
    Guid SubOrderId,
    string SubOrderNo,
    Guid BuyerId,
    Guid SellerId,
    decimal Amount,
    string Reason,
    string Status,
    string? SellerNote,
    string? RejectReason,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt
);
```

- **JSON Response Body mẫu (camelCase):**
```json
{
  "items": [
    {
      "id": "r1000000-0000-0000-0000-000000000001",
      "subOrderId": "02000000-0000-0000-0000-000000000001",
      "subOrderNo": "SUB-20261121-0001",
      "buyerId": "11111111-2222-3333-4444-555555555555",
      "sellerId": "99999999-8888-7777-6666-555555555555",
      "amount": 850000.0,
      "reason": "Damaged goods in transit confirmed by support",
      "status": "Completed",
      "sellerNote": "Refund approved by seller",
      "rejectReason": null,
      "requestedAt": "2026-11-21T01:00:00Z",
      "completedAt": "2026-11-21T01:15:00Z"
    }
  ],
  "pagination": {
    "pageIndex": 1,
    "pageSize": 20,
    "totalCount": 1,
    "totalPages": 1,
    "hasPreviousPage": false,
    "hasNextPage": false
  }
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Không có JWT hợp lệ |
| `403 Forbidden` | `FORBIDDEN` | `You do not have permission to access or modify this resource.` | Vai trò người dùng không hợp lệ |

### 4.1 Bảng Ánh xạ Enum liên quan
| Tên Enum C# | Giá trị DB (Integer) | Giá trị API (PascalCase) | Ý nghĩa nghiệp vụ |
| :--- | :---: | :--- | :--- |
| `RefundStatus.Requested` | `0` | `"Requested"` | Khách hàng đã gửi yêu cầu, chờ Seller/Admin duyệt |
| `RefundStatus.Completed` | `1` | `"Completed"` | Hoàn tiền thành công vào ví khách hàng |
| `RefundStatus.Rejected` | `2` | `"Rejected"` | Người bán hoặc Quản trị viên từ chối yêu cầu hoàn tiền |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Trích xuất `currentUserId` và vai trò (`admin`, `staff`, `seller`, `buyer`) từ JWT context.
2. **Bước 2:** Lập query trên `CommerceDbContext.OrderRefunds` kèm điều kiện phân quyền:
   - Nếu là Seller: chỉ lấy các bản ghi có `SubOrder.SellerId == currentUserId`.
   - Nếu là Buyer: chỉ lấy các bản ghi có `RequestedBy == currentUserId`.
   - Nếu là Admin/Staff: lấy toàn sàn.
3. **Bước 3:** Áp dụng bộ lọc `status` và `subOrderId` nếu có.
4. **Bước 4:** Phân trang trực tiếp tại database qua `.Skip().Take()`, lấy tổng số qua `.CountAsync()`.
5. **Bước 5:** Trả về `PagedData<RefundDetailResponse>` kèm metadata phân trang.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã xác nhận chuẩn phân trang database bắt buộc `PagedData<T>`.
- [x] **Pre-Code:** Tài liệu lưu tại `docs/api/Refunds/GET_REFUNDS.md`.
- [x] **Application:** `OrderService.ListRefundsAsync` hoàn thiện.
- [x] **Tests:** Unit tests phân quyền và phân trang hoàn tiền đạt 100%.
- [x] **Verify:** `dotnet test` và `dotnet build` đạt 0 lỗi.
