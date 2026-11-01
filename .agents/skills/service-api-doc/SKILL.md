---
name: service-api-doc
description: Pre-code API documentation standard for CommerceService (port 8003). Mandates drafting a service-level technical API doc before coding, referencing existing domain entities, enums, DTOs, and error patterns for system-wide consistency. Governs pure DTO contracts, validation attributes, and RFC 9110 error matrices inside CommerceService.
---

# CommerceService API Pre-Code Documentation Skill

> **Scope:** CommerceService repository only (`src/Services/Commerce/`). 100% Inward-Only. Never reference the root workspace or sibling services.

This skill governs the **mandatory pre-code documentation lifecycle** within CommerceService. Before any C# code is written or modified for a REST endpoint, the developer or AI agent must create a technical API specification inside `docs/api/`.

---

## 1. Ranh giới Tài liệu CommerceService

* **Vị trí lưu trữ:** `docs/api/{Feature}/{HTTP_METHOD}_{ENDPOINT_SLUG}.md` (ví dụ `docs/api/Orders/POST_ORDERS.md`).
* **Thời điểm lập:** **BẮT BUỘC TRƯỚC KHI VIẾT CODE (Pre-Code)**.
* **Định dạng dữ liệu trả về:** **Pure DTO** hoặc `PagedData<T>` (tuyệt đối không bọc qua `ApiResponse<T>`).
* **Rào chắn Inward-Only:** Chỉ trỏ tới tài nguyên nội bộ của CommerceService (`Commerce.Domain`, `Commerce.Application`, `Commerce.Infrastructure`, `Commerce.Api`). Không tham chiếu tới `docs/api/` ở Root.

---

## 2. Quy trình 3 Bước Tra cứu Source of Truth Trước Khi Lập Doc

### Bước 1: Tra cứu Thực thể Domain (`Commerce.Domain/Entities/`)
- Mở `Commerce.Domain/Entities/` (ví dụ `Cart.cs`, `CartItem.cs`, `Order.cs`, `SubOrder.cs`, `OrderItem.cs`).
- Ràng buộc: Tiền tệ `HasPrecision(18, 2)`, định dạng UUID cho khóa chính/khóa ngoại.

### Bước 2: Tra cứu Bảng mã Enum (`Commerce.Domain/Enums/`)
- Mở `Commerce.Domain/Enums/` (ví dụ `OrderStatus.cs`, `PaymentStatus.cs`).
- Ghi nhận số nguyên DB (`0`, `1`, `2`...) và chuỗi PascalCase API (`PendingPayment`, `Paid`, `Cancelled`, `Delivered`).
- Điền bảng ánh xạ vào Mục 4.1 của doc.

### Bước 3: Tra cứu Mẫu DTO & Xử lý Lỗi Có Sẵn (`Commerce.Application/`)
- Rà soát các tính năng có sẵn tại `Commerce.Application/Services/` (ví dụ `Carts/`, `Orders/`).
- Kế thừa quy ước đặt tên DTO: `{Action}Request`, `{Action}Response`.
- Kế thừa mã lỗi đã có (`ORDER_NOT_FOUND`, `CART_NOT_FOUND`, `VAL_INVALID_INPUT`, `UNAUTHORIZED`, `FORBIDDEN`).

---

## 3. Các Rào Chắn Bất Biến tại CommerceService

1. **Pure DTO:** Controller tại CommerceService trả về DTO thuần, cấm thêm `ResultFilter` hay bọc `ApiResponse<T>`.
2. **Plural Route:** `/api/v1/carts`, `/api/v1/orders`, `/api/v1/sub-orders`, `/api/v1/checkouts`.
3. **Mã lỗi & Message:** 100% tiếng Anh, Sentence case cho `message`, SCREAMING_SNAKE_CASE cho `errorCode` (cấm tiền tố `ERR_`).
4. **Quyền sở hữu người mua (Buyer Ownership):** `buyerId` luôn đọc từ `HttpContext.GetUserId()`. Cấm tin tưởng `buyerId` từ request query hay body.
5. **Giao tiếp liên dịch vụ qua Typed REST Client:** Gọi Wallet hoặc Catalog qua Typed Client nội bộ có timeout và retry.

---

## 4. Quy trình Thực hiện (Workflow)

1. Sao chép `docs/api/SERVICE_API_DOC_TEMPLATE.md` vào `docs/api/{Feature}/{HTTP_METHOD}_{ENDPOINT_SLUG}.md`.
2. Điền đầy đủ 5 phần của template dựa trên kết quả tra cứu Source of Truth.
3. Hoàn thiện mục 5.1 (Pre-Code Plan) mô tả luồng kiểm tra giỏ hàng, tính giá, gọi trừ tiền ví và tạo đơn.
4. Triển khai code C# tuân thủ 100% hợp đồng đã đặc tả trong doc.
5. Chạy `dotnet test Commerce.sln -c Release` (hoặc build `Commerce.sln`) đạt 100% pass, tích checklist và chuyển trạng thái doc sang `✅ VERIFIED`.
