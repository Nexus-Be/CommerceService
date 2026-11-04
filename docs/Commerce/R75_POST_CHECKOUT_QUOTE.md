# [POST] /api/v1/checkouts/quote

> **Phân hệ thụ lý:** `CommerceService`  
> **Tên tính năng:** `Orders`  
> **Mã tham chiếu Excel:** ``CHK-01 (khung) + FUL-12 + CART-07 + CAT-24``  
> **Trạng thái tài liệu:** `B_none (Chờ hoàn thiện & nghiệm thu)`  
> **Trạng thái triển khai:** `⏳ IN_PROGRESS`  

---

## 1. MÔ TẢ NGHIỆP VỤ & PHÂN QUYỀN

* **Mô tả chức năng:** buyer nhập địa chi, mã giảm giá và coi trước thông tin sau giảm và thông tin tiền ship tính trước
* **HTTP Method:** `POST`
* **Đường dẫn chuẩn:** `/api/v1/checkouts/quote`
* **Đường dẫn gốc trong Excel:** `/api/v1/checkout/quote` (Chuẩn hóa từ '/api/v1/checkout/quote' sang chuẩn 100% số nhiều '/api/v1/checkouts/quote'.)
* **Quyền hạn truy cập (RBAC):** `Buyer`
* **Yêu cầu Idempotency:** Tùy chọn (Khuyến nghị gửi header `Idempotency-Key: UUIDv4` để tránh gửi lặp)

---

## 2. DẤU VẾT MÃ NGUỒN (CODE TRACE & EXECUTION PATH)

Mọi thay đổi trong hàm nghiệp vụ (thêm validation, ném exception, thêm enum) bắt buộc phải cập nhật đồng bộ vào tài liệu này:

* **Microservice thụ lý:** `src/Services/Commerce/`
* **Cổng Gateway điều phối:** `src/Services/Gateway/Gateway/appsettings.json` (Route: `commerce-checkouts-route`)
* **Controller Action:** `Commerce.Api/Controllers/OrdersController.cs` $\rightarrow$ `QuoteCheckout()`
* **Application Interface:** `Commerce.Application/Services/Orders/IOrderService.cs` $\rightarrow$ `QuoteCheckoutAsync()`
* **Service Implementation:** `Commerce.Application/Services/Orders/OrderService.cs` $\rightarrow$ `QuoteCheckoutAsync()`
* **Thực thể & Bảng DB:** `Commerce.Domain/Entities/Order.cs` $\rightarrow$ Bảng `Orders` trong PostgreSQL
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
{
"cartItemIds": ["uuid"],
"shippingAddress": {
"receiverName": "string",
"street": "string",
"ward": "string",
"district": "string",
"province": "string",
"ghnDistrictId": "int",
"ghnWardCode": "string"
},
"contactPhone": "string",
"paymentMethod": "WALLET|VNPAY",
"carrierCode": "string",
"vouchers": {
"system": "string",
"shops": [{"sellerId": "uuid", "code": "string"}],
"products": [{"skuId": "uuid", "code": "string"}]
}
}
```


---

## 4. PHONG BÌ DỮ LIỆU ĐẦU RA (RESPONSE ENVELOPE)

### 4.1. Phản hồi Thành công Đơn lẻ (`ApiResponse<T>`)
Áp dụng cho API trả về 1 đối tượng hoặc kết quả thực thi:
```json
200 OK:
{
  "success": true,
  "statusCode": 200,
  "data": {
    "orders": [
      {
        "sellerId": "uuid",
        "items": "array",
        "itemsAmount": "decimal",
        "productDiscountAmount": "decimal",
        "shopDiscountAmount": "decimal",
        "platformMarkupAmount": "decimal",
        "systemDiscountAmount": "decimal",
        "shippingFeeAmount": "decimal",
        "totalAmount": "decimal",
        "shippingOptions": [
          {
            "fee": "decimal",
            "serviceType": "Standard|Express|SameDay",
            "estimatedDeliveryAt": "datetime"
          }
        ],
        "quoteId": "string"
      }
    ],
    "grandTotal": "decimal",
    "appliedVouchers": "array",
    "invalidVouchers": [
      {
        "code": "string",
        "reason": "string"
      }
    ],
    "walletBalance": "decimal",
    "warnings": [
      "SHIPPING_QUOTE_UNAVAILABLE"
    ]
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
| `404` | `VOUCHER_INVALID` | "One or more vouchers are invalid or expired." | Điều kiện nghiệp vụ đặc thù: VOUCHER_INVALID. |
| `401` | `UNAUTHORIZED` | "Token is missing, expired, or invalid." | Điều kiện nghiệp vụ đặc thù: UNAUTHORIZED. |
| `400` | `CART_EMPTY` | "No valid items selected for checkout." | Điều kiện nghiệp vụ đặc thù: CART_EMPTY. |
| `400` | `VAL_INVALID_ADDRESS` | "Shipping address is incomplete." | Điều kiện nghiệp vụ đặc thù: VAL_INVALID_ADDRESS. |
| `403` | `FORBIDDEN` | "You do not have permission to perform this action." | Người dùng không đủ quyền RBAC theo quy định. |
| `404` | `ORDER_NOT_FOUND` | "Order not found with the specified ID." | Không tìm thấy bản ghi trong Database tương ứng. |
| `500` | `INTERNAL_SERVER_ERROR` | "An unexpected error occurred. Please try again later." | Lỗi hệ thống nội bộ chưa được xử lý. |

---

## 6. DANH MỤC ENUM LIÊN QUAN (DOMAIN ENUMS)

Bảng đối chiếu toàn diện giữa trường dữ liệu trong Payload (JSON Field), định dạng lưu trữ Database và chuỗi trả về API để Frontend hiển thị và binding giao diện chính xác:

| Tên trường Payload (JSON Field) | Kiểu C# Enum | Giá trị DB (`integer`) | Giá trị API (`string PascalCase`) | Ý nghĩa nghiệp vụ & Hướng dẫn hiển thị Frontend |
|:---|:---|:---:|:---|:---|
| `paymentMethod` | `PaymentMethod` | `0` | `Wallet` | **Ví tiền Nexus**: Trừ tiền trực tiếp vào số dư ví khả dụng của tài khoản. |
| `paymentMethod` | `PaymentMethod` | `1` | `VnPay` | **Cổng thanh toán VNPay**: Quét mã QR VNPAY-QR hoặc thẻ ATM / Visa / Mastercard. |
| `paymentMethod` | `PaymentMethod` | `2` | `Cod` | **Thanh toán khi nhận hàng (COD)**: Trả tiền mặt cho shipper khi nhận kiện hàng. |

---

## 7. CHECKLIST HOÀN THÀNH & NGHIỆM THU (COMPLETION CHECKLIST)

- [ ] **1. Tầng Application:** `IOrderService.cs` và `OrderService.cs` đã được triển khai hoàn chỉnh.
- [ ] **2. Tầng Api:** `OrdersController.cs` ghép cặp 1-to-1, route số nhiều, summary XML ngắn gọn.
- [ ] **3. Bắt lỗi & Validation:** Toàn bộ mã lỗi trong ma trận ở Mục 5 đã được kiểm soát trong code.
- [ ] **4. Enum Mapping:** Toàn bộ enum ở Mục 6 được lưu integer trong DB và serialize PascalCase ở API.
- [ ] **5. Kiểm thử tự động:** `dotnet test` vượt qua 100% các bài test (Kèm tên file test: `OrdersTests.cs`).
- [ ] **6. Định tuyến Gateway:** Route YARP `commerce-checkouts-route` đã được cấu hình trong `appsettings.json` và test thông suốt.
- [ ] **7. Xác nhận của Sếp (Human Approval):** Đã kiểm thử thực tế và cho phép chuyển sang `docs/api/A_Active/`.

> **Trạng thái nghiệm thu:** `[ ⏳ IN_PROGRESS (Lưu tại B_none) ]`
