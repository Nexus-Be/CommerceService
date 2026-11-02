# [GET] /api/v1/carts/items/select

> **Phân hệ thụ lý:** `CommerceService`  
> **Tên tính năng:** `Carts`  
> **Mã tham chiếu Excel:** ``CART-06``  
> **Trạng thái tài liệu:** `B_none (Chờ hoàn thiện & nghiệm thu)`  
> **Trạng thái triển khai:** `⏳ IN_PROGRESS`  

---

## 1. MÔ TẢ NGHIỆP VỤ & PHÂN QUYỀN

* **Mô tả chức năng:** buyer select ccasc item bên trong cart và trả ra giá tiền
* **HTTP Method:** `GET`
* **Đường dẫn chuẩn:** `/api/v1/carts/items/select`
* **Đường dẫn gốc trong Excel:** `/api/v1/cart/items/select` (Chuẩn hóa từ '/api/v1/cart/items/select' sang chuẩn 100% số nhiều '/api/v1/carts/items/select'.)
* **Quyền hạn truy cập (RBAC):** `Buyer`
* **Yêu cầu Idempotency:** Không áp dụng (Idempotent theo HTTP spec)

---

## 2. DẤU VẾT MÃ NGUỒN (CODE TRACE & EXECUTION PATH)

Mọi thay đổi trong hàm nghiệp vụ (thêm validation, ném exception, thêm enum) bắt buộc phải cập nhật đồng bộ vào tài liệu này:

* **Microservice thụ lý:** `src/Services/Commerce/`
* **Cổng Gateway điều phối:** `src/Services/Gateway/Gateway/appsettings.json` (Route: `commerce-carts-route`)
* **Controller Action:** `Commerce.Api/Controllers/CartsController.cs` $\rightarrow$ `SelectCartItems()`
* **Application Interface:** `Commerce.Application/Services/Carts/ICartService.cs` $\rightarrow$ `SelectCartItemsAsync()`
* **Service Implementation:** `Commerce.Application/Services/Carts/CartService.cs` $\rightarrow$ `SelectCartItemsAsync()`
* **Thực thể & Bảng DB:** `Commerce.Domain/Entities/Cart.cs` $\rightarrow$ Bảng `Carts` trong PostgreSQL
* **Typed HTTP Client (nếu có):** N/A (Giao tiếp nội bộ trực tiếp qua DbContext)

---

## 3. QUY CHUẨN VALIDATION & THAM SỐ ĐẦU VÀO

### 3.1. Route Parameters (Tham số đường dẫn)
*Không có tham số đường dẫn.*

### 3.2. Query Parameters (Tham số truy vấn)
*Không có tham số truy vấn.*

### 3.3. Request Body (JSON Payload)
```json
Header:
{
  "Authorization": "Bearer <JWT>"
}

Query:
{
"cartItemIds": ["uuid"]
}
```


---

## 4. PHONG BÌ DỮ LIỆU ĐẦU RA (RESPONSE ENVELOPE)

### 4.1. Phản hồi Phân trang (`ApiResponse<PagedData<T>>`)
Áp dụng cho API tra cứu danh sách có phân trang:
```json
200 OK:
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "cartItemId": "uuid",
        "issues": [
          "PRICE_CHANGED|OUT_OF_STOCK|INACTIVE|SELLER_SUSPENDED|QTY_EXCEEDS_STOCK"
        ],
        "currentUnitPrice": "decimal",
        "availableQty": "int"
      }
    ],
    "pagination": {
      "pageIndex": 1,
      "pageSize": 20,
      "totalCount": 100,
      "totalPages": 5,
      "hasPreviousPage": false,
      "hasNextPage": true
    }
  },
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "timestamp": "2026-10-26T00:00:00Z"
}
```

---

## 5. MA TRẬN MÃ LỖI (RFC 9110 ERROR MATRIX)

Frontend bắt buộc dựa vào cột `errorCode` để map và hiển thị giao diện phù hợp. Khi backend function bổ sung lệnh ném lỗi, phải cập nhật ngay vào bảng này:

| HTTP Status | errorCode (SCREAMING_SNAKE_CASE) | message (100% English Sentence case) | Điều kiện kích hoạt trong Code Function |
|:---:|:---|:---|:---|
| `400` | `VAL_INVALID_INPUT` | "One or more validation errors occurred." | Dữ liệu đầu vào vi phạm FluentValidation hoặc model binding. |
| `401` | `UNAUTHORIZED` | "Authentication token is missing, expired, or invalid." | Người dùng chưa đăng nhập hoặc JWT không hợp lệ. |
| `401` | `UNAUTHORIZED` | "Token is missing, expired, or invalid." | Điều kiện nghiệp vụ đặc thù: UNAUTHORIZED. |
| `400` | `VAL_INVALID_INPUT` | "Cart item IDs are required." | Điều kiện nghiệp vụ đặc thù: VAL_INVALID_INPUT. |
| `403` | `FORBIDDEN` | "You do not have permission to perform this action." | Người dùng không đủ quyền RBAC theo quy định. |
| `404` | `CART_NOT_FOUND` | "Cart not found with the specified ID." | Không tìm thấy bản ghi trong Database tương ứng. |
| `500` | `INTERNAL_SERVER_ERROR` | "An unexpected error occurred. Please try again later." | Lỗi hệ thống nội bộ chưa được xử lý. |

---

## 6. DANH MỤC ENUM LIÊN QUAN (DOMAIN ENUMS)

Bảng đối chiếu toàn diện giữa trường dữ liệu trong Payload (JSON Field), định dạng lưu trữ Database và chuỗi trả về API để Frontend hiển thị và binding giao diện chính xác:

| Tên trường Payload (JSON Field) | Kiểu C# Enum | Giá trị DB (`integer`) | Giá trị API (`string PascalCase`) | Ý nghĩa nghiệp vụ & Hướng dẫn hiển thị Frontend |
|:---|:---|:---:|:---|:---|
| `status` | `CartStatus` | `0` | `Active` | **Giỏ hàng đang sử dụng**: Chứa các mặt hàng người dùng đang chọn. Badge xanh lá. |
| `status` | `CartStatus` | `1` | `CheckedOut` | **Đã thanh toán**: Toàn bộ món hàng đã chuyển thành đơn hàng. Badge xanh dương. |
| `status` | `CartStatus` | `2` | `Abandoned` | **Bỏ quên / Hết hạn**: Giỏ hàng không hoạt động trong thời gian dài. Badge xám. |

---

## 7. CHECKLIST HOÀN THÀNH & NGHIỆM THU (COMPLETION CHECKLIST)

- [ ] **1. Tầng Application:** `ICartService.cs` và `CartService.cs` đã được triển khai hoàn chỉnh.
- [ ] **2. Tầng Api:** `CartsController.cs` ghép cặp 1-to-1, route số nhiều, summary XML ngắn gọn.
- [ ] **3. Bắt lỗi & Validation:** Toàn bộ mã lỗi trong ma trận ở Mục 5 đã được kiểm soát trong code.
- [ ] **4. Enum Mapping:** Toàn bộ enum ở Mục 6 được lưu integer trong DB và serialize PascalCase ở API.
- [ ] **5. Kiểm thử tự động:** `dotnet test` vượt qua 100% các bài test (Kèm tên file test: `CartsTests.cs`).
- [ ] **6. Định tuyến Gateway:** Route YARP `commerce-carts-route` đã được cấu hình trong `appsettings.json` và test thông suốt.
- [ ] **7. Xác nhận của Sếp (Human Approval):** Đã kiểm thử thực tế và cho phép chuyển sang `docs/api/A_Active/`.

> **Trạng thái nghiệm thu:** `[ ⏳ IN_PROGRESS (Lưu tại B_none) ]`
