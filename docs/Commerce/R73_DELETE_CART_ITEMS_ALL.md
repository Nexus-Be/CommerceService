# [DELETE] /api/v1/carts/items/all

> **Phân hệ thụ lý:** `CommerceService`  
> **Tên tính năng:** `Carts`  
> **Mã tham chiếu Excel:** ``CART-05``  
> **Trạng thái tài liệu:** `B_none (Chờ hoàn thiện & nghiệm thu)`  
> **Trạng thái triển khai:** `⏳ IN_PROGRESS`  

---

## 1. MÔ TẢ NGHIỆP VỤ & PHÂN QUYỀN

* **Mô tả chức năng:** xóa toàn bộ sản phẩm ra khỏi giỏ
* **HTTP Method:** `DELETE`
* **Đường dẫn chuẩn:** `/api/v1/carts/items/all`
* **Đường dẫn gốc trong Excel:** `/api/v1/cart/items/all` (Chuẩn hóa từ '/api/v1/cart/items/all' sang chuẩn 100% số nhiều '/api/v1/carts/items/all'.)
* **Quyền hạn truy cập (RBAC):** `Buyer`
* **Yêu cầu Idempotency:** Không áp dụng (Idempotent theo HTTP spec)

---

## 2. DẤU VẾT MÃ NGUỒN (CODE TRACE & EXECUTION PATH)

Mọi thay đổi trong hàm nghiệp vụ (thêm validation, ném exception, thêm enum) bắt buộc phải cập nhật đồng bộ vào tài liệu này:

* **Microservice thụ lý:** `src/Services/Commerce/`
* **Cổng Gateway điều phối:** `src/Services/Gateway/Gateway/appsettings.json` (Route: `commerce-carts-route`)
* **Controller Action:** `Commerce.Api/Controllers/CartsController.cs` $\rightarrow$ `RemoveCartItem()`
* **Application Interface:** `Commerce.Application/Services/Carts/ICartService.cs` $\rightarrow$ `RemoveCartItemAsync()`
* **Service Implementation:** `Commerce.Application/Services/Carts/CartService.cs` $\rightarrow$ `RemoveCartItemAsync()`
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

Body:
{"all": "boolean"}
```


---

## 4. PHONG BÌ DỮ LIỆU ĐẦU RA (RESPONSE ENVELOPE)

### 4.1. Phản hồi Không có Body (`204 NoContent`)
Áp dụng cho thao tác xóa hoặc cập nhật không cần trả payload dữ liệu.
```http
HTTP/1.1 204 No Content
```

---

## 5. MA TRẬN MÃ LỖI (RFC 9110 ERROR MATRIX)

Frontend bắt buộc dựa vào cột `errorCode` để map và hiển thị giao diện phù hợp. Khi backend function bổ sung lệnh ném lỗi, phải cập nhật ngay vào bảng này:

| HTTP Status | errorCode (SCREAMING_SNAKE_CASE) | message (100% English Sentence case) | Điều kiện kích hoạt trong Code Function |
|:---:|:---|:---|:---|
| `400` | `VAL_INVALID_INPUT` | "One or more validation errors occurred." | Dữ liệu đầu vào vi phạm FluentValidation hoặc model binding. |
| `401` | `UNAUTHORIZED` | "Authentication token is missing, expired, or invalid." | Người dùng chưa đăng nhập hoặc JWT không hợp lệ. |
| `404` | `CART_NOT_FOUND` | "User cart was not found." | Điều kiện nghiệp vụ đặc thù: CART_NOT_FOUND. |
| `401` | `UNAUTHORIZED` | "Token is missing, expired, or invalid." | Điều kiện nghiệp vụ đặc thù: UNAUTHORIZED. |
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
