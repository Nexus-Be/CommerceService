# 📢 BÁO CÁO BÀN GIAO & CẬP NHẬT HỆ THỐNG (RELEASE HANDOVER REPORT)

**Dự án**: `CommerceService` (Port `8003`, Database `commerce_db:5435`)  
**Tính năng**: `Feature 023 - Order Financial Lifecycle, Staff-Managed Platform Fee & Voucher Extensibility`  
**Đối tượng nhận**: Team Frontend (Web / Mobile App), Team Backend, QA & Product Owner  
**Thời gian hoàn thành**: 16/11/2026  

---

## 🎯 TỔNG QUAN THAY ĐỔI
Hệ thống `CommerceService` đã hoàn thành chuẩn hóa toàn diện vòng đời tài chính của đơn hàng, bảo vệ ví ký quỹ sàn (System Escrow), khai tử hoàn toàn cổng VNPay, bổ sung API quản lý tỷ lệ phí sàn độc quyền cho nhân viên (**Staff**), và chuẩn bị sẵn sàng kiến trúc cho tính năng Voucher (**Feature 024**).

---

## ⚠️ 1. ĐỐI VỚI FRONTEND (FE / MOBILE APP): CÁC ĐIỂM CẦN LƯU Ý & THAY ĐỔI NGAY

### 1.1. Khai tử hoàn toàn cổng thanh toán VNPay
* **Hành động từ FE**:
  * **Ẩn hoặc gỡ bỏ** nút/lựa chọn `"Thanh toán qua VNPay"` trên giao diện Checkout của Web và Mobile App.
  * Hiện tại hệ thống chỉ hỗ trợ **2 phương thức thanh toán chính thức**:
    1. `PaymentMethod.Wallet` (`0`): Thanh toán qua Ví điện tử cá nhân.
    2. `PaymentMethod.Cod` (`2`): Thanh toán tiền mặt khi nhận hàng (Cash on Delivery).
* **Nếu người dùng sử dụng app phiên bản cũ gửi `paymentMethod: "VnPay"` (hoặc `1`)**:
  * API `POST /api/v1/orders` sẽ **từ chối ngay lập tức** với mã lỗi HTTP 400:
    ```json
    {
      "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
      "title": "Bad Request",
      "status": 400,
      "errorCode": "UNSUPPORTED_PAYMENT_METHOD",
      "detail": "VNPay payment method is no longer supported. Please use Wallet or COD."
    }
    ```
  * FE cần bắt mã lỗi `UNSUPPORTED_PAYMENT_METHOD` để hiển thị popup hướng dẫn người dùng chọn Ví hoặc COD.

---

### 1.2. Nâng cấp trải nghiệm khi thanh toán Ví không đủ số dư (Safe Checkout)
* **Hành vi cũ (trước đây)**: Khi ví không đủ tiền, hệ thống vẫn xóa giỏ hàng và tạo đơn hàng ở trạng thái `PendingPayment` (dễ gây hoang mang vì khách tưởng đã mất hàng trong giỏ).
* **Hành vi mới (chuẩn hóa)**:
  * Khi ví không đủ số dư để thanh toán đơn hàng:
    1. Hệ thống **từ chối tạo đơn** với HTTP 400 và mã lỗi `INSUFFICIENT_WALLET_BALANCE`.
    2. **Giỏ hàng được giữ nguyên 100%** (không xóa sản phẩm).
    3. Tồn kho tạm giữ được giải phóng ngay lập tức.
  * **Payload lỗi trả về cho FE**:
    ```json
    {
      "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
      "title": "Bad Request",
      "status": 400,
      "errorCode": "INSUFFICIENT_WALLET_BALANCE",
      "detail": "Buyer wallet balance is insufficient to complete the payment."
    }
    ```
  * **Khuyến nghị FE**: Khi gặp lỗi `INSUFFICIENT_WALLET_BALANCE`, FE hiển thị popup:
    > *"Số dư ví của bạn không đủ để thanh toán đơn hàng. Vui lòng nạp thêm tiền vào ví hoặc chuyển sang hình thức COD."*  
    > Cung cấp nút chuyển hướng nhanh sang trang Nạp ví mà không làm mất sản phẩm trong giỏ hàng.

---

## 🛠️ 2. API QUẢN TRỊ PHÍ SÀN DÀNH CHO STAFF & ADMIN (MỚI)

Tỷ lệ phí sàn (`PlatformMarkupPercent`) không còn gán cứng mà có thể tùy chỉnh linh hoạt qua endpoint quản trị.

### 2.1. Phân quyền truy cập (RBAC)
* **Chỉ tài khoản có Role `staff` hoặc `admin`** mới được phép xem và điều chỉnh phí sàn.
* Mọi nỗ lực truy cập từ Người mua (`buyer`) hoặc Người bán (`seller`) đều bị chặn với HTTP 403 `FORBIDDEN`:
  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
    "title": "Forbidden",
    "status": 403,
    "errorCode": "FORBIDDEN",
    "detail": "Access denied. Required roles: staff, admin"
  }
  ```

### 2.2. Chi tiết API
* **1. Xem tỷ lệ phí sàn hiện tại**:
  * **Endpoint**: `GET /api/v1/admin/platform-fee`
  * **Header**: `Authorization: Bearer <STAFF_JWT>`, `x-user-roles: staff`
  * **Response (HTTP 200)**:
    ```json
    {
      "defaultPercent": 5.0,
      "updatedByRole": "staff",
      "updatedAt": "2026-11-16T12:00:00Z"
    }
    ```

* **2. Cập nhật tỷ lệ phí sàn**:
  * **Endpoint**: `PUT /api/v1/admin/platform-fee`
  * **Header**: `Authorization: Bearer <STAFF_JWT>`, `Content-Type: application/json`
  * **Request Body**:
    ```json
    {
      "defaultPercent": 7.5
    }
    ```
  * **Ràng buộc**: `defaultPercent` phải nằm trong khoảng từ `0.0%` đến `100.0%`. Nếu nhập ngoài khoảng, hệ thống trả về HTTP 400 `VAL_INVALID_INPUT`.
  * **Cơ chế lưu trữ**: Sử dụng bộ nhớ đệm phân tán `IDistributedCache` với cơ chế fallback tự động về cấu hình gốc trong `Commerce.Api/appsettings.json`, **không làm lệch Schema Database (Zero Schema Drift)**.

---

## 💰 3. CƠ CHẾ HẠCH TOÁN TÀI CHÍNH BẤT BIẾN & HOÀN TIỀN CHUẨN XÁC

### 3.1. Đẳng thức tài chính trên từng đơn con (SubOrder)
Mọi đơn con đều đảm bảo đẳng thức kế toán bất biến:
$$\boxed{\text{TotalAmount} = \text{SellerReceiveAmount} + \text{PlatformFeeAmount} + \text{ShippingFeeAmount}}$$

* `SellerReceiveAmount`: Số tiền thực nhận của Shop khi giao dịch hoàn tất (ItemsAmount - Giảm giá Shop).
* `PlatformFeeAmount`: Doanh thu hoa hồng sàn thu được (Markup - Trợ giá Voucher Sàn). Hỗ trợ đầy đủ trường hợp $\text{PlatformFeeAmount} < 0$ khi Sàn trợ giá voucher lớn hơn phí hoa hồng.
* `ShippingFeeAmount`: Cước vận chuyển thanh toán cho đơn vị giao hàng (GHN).

### 3.2. Hoàn tiền chuẩn hóa theo phương thức thanh toán
* **Đơn thanh toán qua Ví (`PaymentMethod.Wallet`)**:
  * Khi Buyer/Seller hủy đơn trước khi giao hàng: Hệ thống **tự động hoàn đúng số tiền `TotalAmount`** từ Ví ký quỹ hệ thống về Ví của Buyer, đồng thời giải phóng voucher đã áp dụng (`DiscountCodeUsages.Status = Released`).
* **Đơn thanh toán COD (`PaymentMethod.Cod`)**:
  * Khách hàng chưa trả tiền trước nên khi hủy đơn, hệ thống **bỏ qua hoàn toàn lệnh hoàn tiền ví**, triệt tiêu nguy cơ thất thoát dòng tiền của sàn.
* **Quyết toán cho Seller khi nhận hàng thành công (`ConfirmReceipt`)**:
  * Hệ thống chỉ trích xuất đúng `SellerReceiveAmount` sang ví của Shop. Toàn bộ phần phí sàn và phí ship được lưu lại an toàn tại Ví Hệ Thống.
  * Nếu `SellerReceiveAmount == 0` (đơn hàng được giảm giá 100%), hệ thống tự động đánh dấu hoàn tất và bỏ qua bước giải ngân ví.

---

## 🧩 4. KIẾN TRÚC MỞ RỘNG VOUCHER (CHUẨN BỊ CHO FEATURE 024)
* Đã tích hợp sẵn Interface `IVoucherDiscountCalculator` vào quy trình báo giá checkout (`OrderService.QuoteCheckoutAsync`).
* Phân định minh bạch 3 tầng voucher trong báo giá:
  * `ProductDiscountAmount`: Giảm giá trực tiếp trên giá sản phẩm.
  * `ShopDiscountAmount`: Shop Voucher (giảm trừ vào doanh thu của Shop).
  * `SystemDiscountAmount`: Sàn Voucher (Sàn tài trợ, khấu trừ vào hoa hồng phí sàn).
* Khi tiến hành phát triển **Feature 024 (Voucher & Promotion Allocation Engine)**, team có thể cắm trực tiếp logic phân bổ voucher mà không cần sửa đổi bảng Database hay làm gián đoạn luồng thanh toán hiện tại.

---

## 📊 5. KẾT QUẢ KIỂM CHỨNG & CHỈ SỐ CHẤT LƯỢNG

| Hạng mục kiểm thử | Trạng thái | Ghi chú |
|:---|:---:|:---|
| **Build Solution (`Release`)** | ✅ **Thành công 100%** | 0 Warning, 0 Error |
| **Unit Test Suite** | ✅ **208 / 208 Passed** | 100% tests vượt qua (`0 Failed`, `0 Skipped`) |
| **RBAC Security Test** | ✅ **Passed** | Chặn Buyer/Seller với 403, cho phép Staff với 200 |
| **Fail-Closed Wallet Balance Test** | ✅ **Passed** | Ví thiếu tiền $\rightarrow$ giỏ hàng giữ nguyên, tồn kho nhả lại |
| **COD Refund Bypass Test** | ✅ **Passed** | Hủy đơn COD không gọi hoàn tiền ví |
| **Voucher Reversion Test** | ✅ **Passed** | Hủy đơn tự động trả lại số lượt voucher |

---

## 📌 6. TÀI LIỆU KỸ THUẬT THAM KHẢO NỘI BỘ
* **Đặc tả nghiệp vụ chi tiết**: [specs/023-order-financial-lifecycle-and-voucher-extensibility/spec.md](file:///d:/CommerceService/specs/023-order-financial-lifecycle-and-voucher-extensibility/spec.md)
* **Kế hoạch triển khai**: [specs/023-order-financial-lifecycle-and-voucher-extensibility/plan.md](file:///d:/CommerceService/specs/023-order-financial-lifecycle-and-voucher-extensibility/plan.md)
* **Danh mục nhiệm vụ đã hoàn thành**: [specs/023-order-financial-lifecycle-and-voucher-extensibility/tasks.md](file:///d:/CommerceService/specs/023-order-financial-lifecycle-and-voucher-extensibility/tasks.md)
* **OpenAPI Contract Quản lý phí sàn**: [contracts/admin-platform-fee-api.yaml](file:///d:/CommerceService/specs/023-order-financial-lifecycle-and-voucher-extensibility/contracts/admin-platform-fee-api.yaml)
* **Hướng dẫn Curl & Quickstart**: [specs/023-order-financial-lifecycle-and-voucher-extensibility/quickstart.md](file:///d:/CommerceService/specs/023-order-financial-lifecycle-and-voucher-extensibility/quickstart.md)
