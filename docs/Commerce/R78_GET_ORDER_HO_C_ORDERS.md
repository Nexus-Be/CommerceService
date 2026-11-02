# [GET] /api/v1/orders

> **Phân hệ thụ lý:** `CommerceService`  
> **Tên tính năng:** `Orders`  
> **Mã tham chiếu Excel:** ``ORD-07``  
> **Trạng thái tài liệu:** `B_none (Chờ hoàn thiện & nghiệm thu)`  
> **Trạng thái triển khai:** `⏳ IN_PROGRESS`  

---

## 1. MÔ TẢ NGHIỆP VỤ & PHÂN QUYỀN

* **Mô tả chức năng:** Lấy danh sách lịch sử đơn hàng của người mua (Buyer Orders List)
* **HTTP Method:** `GET`
* **Đường dẫn chuẩn:** `/api/v1/orders`
* **Đường dẫn gốc trong Excel:** `/api/v1/order (hoặc /api/v1/orders)` (Chuẩn hóa sang `/api/v1/orders` theo quy chuẩn 100% số nhiều.)
* **Quyền hạn truy cập (RBAC):** `Buyer`
* **Yêu cầu Idempotency:** Không áp dụng (Idempotent theo HTTP spec)

---

## 2. DẤU VẾT MÃ NGUỒN (CODE TRACE & EXECUTION PATH)

Mọi thay đổi trong hàm nghiệp vụ (thêm validation, ném exception, thêm enum) bắt buộc phải cập nhật đồng bộ vào tài liệu này:

* **Microservice thụ lý:** `src/Services/Commerce/`
* **Cổng Gateway điều phối:** `src/Services/Gateway/Gateway/appsettings.json` (Route: `commerce-orders-route`)
* **Controller Action:** `Commerce.Api/Controllers/OrdersController.cs` $\rightarrow$ `GetOrders()`
* **Application Interface:** `Commerce.Application/Services/Orders/IOrderService.cs` $\rightarrow$ `GetOrdersAsync()`
* **Service Implementation:** `Commerce.Application/Services/Orders/OrderService.cs` $\rightarrow$ `GetOrdersAsync()`
* **Thực thể & Bảng DB:** `Commerce.Domain/Entities/Order.cs` $\rightarrow$ Bảng `Orders` trong PostgreSQL
* **Typed HTTP Client (nếu có):** N/A (Giao tiếp nội bộ trực tiếp qua DbContext)

---

## 3. QUY CHUẨN VALIDATION & THAM SỐ ĐẦU VÀO

### 3.1. Route Parameters (Tham số đường dẫn)
*Không có tham số đường dẫn.*

### 3.2. Query Parameters (Tham số truy vấn)
| Tên tham số | Kiểu dữ liệu | Mặc định | Ràng buộc & Khoảng giá trị |
|:---|:---|:---:|:---|
| `page` | `integer` | `1` | Số trang (tối thiểu 1) |
| `pageSize` | `integer` | `20` | Kích thước trang ($1 \le pageSize \le 100$) |

### 3.3. Request Body (JSON Payload)
```json
Header:
{
  "Authorization": "Bearer <JWT>"
}

Query:
{
  "page": "int (default: 1)",
  "pageSize": "int (default: 20)",
  "status": "string (optional: PendingPayment | Processing | Shipping | Delivered | Completed | Cancelled)",
  "fromDate": "datetime (optional)",
  "toDate": "datetime (optional)"
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
        "orderId": "6a1b2c3d-7777-4e8f-9a0b-1c2d3e4f5a07",
        "orderNo": "ORD-20261026-0001",
        "buyerId": "11111111-1111-1111-1111-000000000004",
        "status": "PendingPayment",
        "totalAmount": 480000,
        "paymentMethod": "Wallet",
        "shippingAddress": "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
        "contactPhone": "0901234567",
        "createdAt": "2026-10-26T00:00:00Z",
        "subOrders": [
          {
            "subOrderId": "b1c2d3e4-8888-4f9a-8b0c-2d3e4f5a6b08",
            "subOrderNo": "SUB-20261026-0001",
            "sellerId": "5e9d1c2b-7f3a-4d8e-9c1b-2a4f6d8e0b12",
            "shopName": "Shop Cong Nghe",
            "status": "PendingPayment",
            "itemsAmount": 500000,
            "shippingFee": 30000,
            "discountAmount": 50000,
            "totalAmount": 480000,
            "items": [
              {
                "orderItemId": "c2d3e4f5-9999-4a0b-9c1d-3e4f5a6b7c09",
                "productId": "9f8e7d6c-2222-4b3c-8d4e-5f6a7b8c9d02",
                "productName": "Chuot gaming khong day",
                "skuId": "7c6b5a49-3333-4c4d-9e5f-6a7b8c9d0e03",
                "skuCode": "MOUSE-WL-01",
                "quantity": 1,
                "unitPrice": 500000,
                "lineTotal": 500000,
                "imageUrl": "https://img.example.com/mouse.png"
              }
            ]
          }
        ]
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
| `403` | `FORBIDDEN` | "User does not have Customer role permission." | Điều kiện nghiệp vụ đặc thù: FORBIDDEN. |
| `401` | `UNAUTHORIZED` | "Authentication token is missing or invalid." | Điều kiện nghiệp vụ đặc thù: UNAUTHORIZED. |
| `403` | `FORBIDDEN` | "You do not have permission to perform this action." | Người dùng không đủ quyền RBAC theo quy định. |
| `404` | `ORDER_NOT_FOUND` | "Order not found with the specified ID." | Không tìm thấy bản ghi trong Database tương ứng. |
| `500` | `INTERNAL_SERVER_ERROR` | "An unexpected error occurred. Please try again later." | Lỗi hệ thống nội bộ chưa được xử lý. |

---

## 6. DANH MỤC ENUM LIÊN QUAN (DOMAIN ENUMS)

Bảng đối chiếu toàn diện giữa trường dữ liệu trong Payload (JSON Field), định dạng lưu trữ Database và chuỗi trả về API để Frontend hiển thị và binding giao diện chính xác:

| Tên trường Payload (JSON Field) | Kiểu C# Enum | Giá trị DB (`integer`) | Giá trị API (`string PascalCase`) | Ý nghĩa nghiệp vụ & Hướng dẫn hiển thị Frontend |
|:---|:---|:---:|:---|:---|
| `status` | `OrderStatus` | `0` | `PendingPayment` | **Chờ thanh toán**: Đơn hàng vừa tạo, chờ quét QR hoặc thanh toán ví/VNPay. Badge vàng (Warning). |
| `status` | `OrderStatus` | `1` | `PaymentProcessing` | **Đang xử lý giao dịch**: Cổng thanh toán đang đối soát tiền. Badge xanh dương (Info). |
| `status` | `OrderStatus` | `2` | `Paid` | **Đã thanh toán thành công**: Tiền đã ghi nhận, chuyển sang các đơn con (SubOrders) cho Shop chuẩn bị hàng. Badge xanh lá (Success). |
| `status` | `OrderStatus` | `3` | `Cancelled` | **Đã hủy đơn**: Người mua hủy hoặc quá hạn thanh toán. Badge đỏ (Danger). |
| `status` | `OrderStatus` | `4` | `Expired` | **Hết hạn thanh toán**: Quá thời gian quy định (15-30 phút) mà không thanh toán. Badge xám (Muted). |
| `status` | `SubOrderStatus` | `0` | `PendingPayment` | **Chờ thanh toán chung**: Đơn cha chưa thanh toán. Badge vàng (Warning). |
| `status` | `SubOrderStatus` | `1` | `Paid` | **Đã thanh toán**: Đang chờ Seller xác nhận đơn (trong vòng 24h). Badge xanh dương (Info). |
| `status` | `SubOrderStatus` | `2` | `Confirmed` | **Shop đã tiếp nhận**: Seller bấm xác nhận chuẩn bị gói hàng. Badge xanh lá nhạt (Primary). |
| `status` | `SubOrderStatus` | `3` | `Shipping` | **Đang giao hàng**: Đơn vị vận chuyển đã lấy hàng và đang chuyển phát. Badge xanh dương đậm (Info). |
| `status` | `SubOrderStatus` | `4` | `Delivered` | **Đã giao hàng**: Khách đã nhận được kiện hàng từ bưu tá. Badge tím (Purple). |
| `status` | `SubOrderStatus` | `5` | `Completed` | **Đã hoàn tất trọn vẹn**: Khách bấm hoàn tất hoặc hết thời gian khiếu nại, tiền mở khóa cho Seller. Badge xanh lá (Success). |
| `status` | `SubOrderStatus` | `6` | `Cancelled` | **Đã hủy**: Shop từ chối hoặc người mua yêu cầu hủy hợp lệ. Badge đỏ (Danger). |
| `status` | `SubOrderStatus` | `7` | `Refunded` | **Đã hoàn tiền**: Tiền hàng đã hoàn trả về ví cho người mua. Badge cam (Warning). |
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
- [ ] **6. Định tuyến Gateway:** Route YARP `commerce-orders-route` đã được cấu hình trong `appsettings.json` và test thông suốt.
- [ ] **7. Xác nhận của Sếp (Human Approval):** Đã kiểm thử thực tế và cho phép chuyển sang `docs/api/A_Active/`.

> **Trạng thái nghiệm thu:** `[ ⏳ IN_PROGRESS (Lưu tại B_none) ]`
