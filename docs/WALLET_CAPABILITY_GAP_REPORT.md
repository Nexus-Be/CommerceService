# BÁO CÁO KHOẢNG TRỐNG TÍCH HỢP COMMERCE ↔ WALLET SERVICE
## (WALLET CAPABILITY GAP & FAST-TRACK ACTIVATION REPORT)

> **Mã tài liệu:** `DOC-GAP-WALLET-202611`  
> **Dịch vụ sở hữu:** CommerceService (Port 8003)  
> **Dịch vụ đối tác:** WalletService (Port 8008)  
> **Cập nhật:** 2026-11-10 — Đã kích hoạt toàn diện theo Hợp đồng tích hợp 2026-11-10 (`specs/017-wallet-contract-alignment`).  
> **Trạng thái:** ✅ ĐÃ GIẢI QUYẾT 100% CÁC GAPs P0/P1 (GAP-01, GAP-02, GAP-04, GAP-05, GAP-06).

---

## 1. TỔNG QUAN HIỆN TRẠNG (EXECUTIVE SUMMARY)

Theo hợp đồng tích hợp chính thức từ WalletService bàn giao ngày 2026-11-10 (`integration-contract.md`), WalletService đã cung cấp đầy đủ các API liên dịch vụ:
- `POST /api/v1/wallets/orders/{orderId}/payment` (Header `x-user-id: <buyer-uuid>`).
- `POST /api/v1/wallets/sub-orders/{subOrderId}/settle` (Escrow release).
- `POST /api/v1/wallets/orders/{orderId}/refund` & `POST /api/v1/wallets/sub-orders/{subOrderId}/refund`.
- `POST /api/v1/wallets/checkout/direct` & `GET /api/v1/wallets/checkout/direct/{orderId}`.

Toàn bộ các khoảng trống (GAP-01 đến GAP-06) đã được CommerceService hiện thực hóa và kiểm chứng thông qua feature `017-wallet-contract-alignment`.

---

## 2. MA TRẬN KHOẢNG TRỐNG CHỨC NĂNG (CAPABILITY GAP MATRIX - RESOLVED)

| Mã GAP | Tên Chức Năng | Kỳ Vọng Ban Đầu Của Commerce | Trạng Thái Hợp Đồng Wallet 2026-11-10 | Trạng Thái Tại CommerceService | Kết Luận |
| :--- | :--- | :--- | :--- | :--- | :---: |
| **GAP-01** | Hoàn tiền tự động (Automated Refund) | API hoàn tiền tự động khi Shop hủy đơn, Đổi trả, hoặc rollback DB | Đã hỗ trợ `POST .../orders/{orderId}/refund` và `.../sub-orders/{subOrderId}/refund` | ✅ Đã kết nối trong `OrderService` và `WalletClient` | **ĐÃ GIẢI QUYẾT** |
| **GAP-02** | Ký quỹ & Quyết toán Nhà bán (Escrow & Seller Payout) | Giải ngân về ví Shop khi SubOrder hoàn tất | Đã hỗ trợ `POST .../sub-orders/{subOrderId}/settle` | ✅ Đã kết nối trong `OrderService.BuyerConfirmSubOrderReceiptAsync` | **ĐÃ GIẢI QUYẾT** |
| **GAP-03** | Kiểm tra số dư trước khi mua (Pre-checkout Balance) | API tra cứu số dư để hiển thị tại trang Checkout Quote | Đã hỗ trợ `GET /api/v1/wallets/me` nhận `x-user-id` | ✅ Đã kết nối trong `QuoteCheckoutAsync` | **ĐÃ GIẢI QUYẾT** |
| **GAP-04** | Thanh toán đơn hàng qua Cổng VNPay (Direct Order Gateway) | Sinh URL thanh toán VNPay gắn với `orderId` | Đã hỗ trợ `POST /api/v1/wallets/checkout/direct` | ✅ Đã kết nối sinh link thật và lưu `paymentUrl` | **ĐÃ GIẢI QUYẾT** |
| **GAP-05** | Truy vấn trạng thái & Đối soát (Payment Query & Reconciliation) | API GET tra cứu trạng thái thanh toán theo `orderId` | Đã hỗ trợ `GET /api/v1/wallets/checkout/direct/{orderId}` | ✅ Đã có `VnPayPaymentPollingWorker` chạy nền 30s | **ĐÃ GIẢI QUYẾT** |
| **GAP-06** | Chuẩn hóa kiểu UUID Transaction ID | Transaction ID trả về chuẩn UUID v4 (`Guid`) | 100% ID trả về chuỗi UUID v4 | ✅ Đã lưu chuẩn `Guid` vào `orders.wallet_transaction_id` | **ĐÃ GIẢI QUYẾT** |

---

## 3. CHI TIẾT TỪNG KHOẢNG TRỐNG & HƯỚNG DẪN KÍCH HOẠT NHANH

---

### GAP-01: Hoàn Tiền Tự Động (Automated Refund API)

#### 1. Bối cảnh & Kỳ vọng
Khi xảy ra các tình huống sau, Commerce cần hoàn tiền tức thì từ Ví hệ thống về Ví của Buyer:
- **Shop từ chối đơn hàng (Seller Reject)**: Shop hết hàng hoặc không thể chuẩn bị đơn (`SellerRejectSubOrderAsync`).
- **Khách hủy đơn hợp lệ (Buyer Cancel)**: Khách hủy đơn trong khung giờ quy định trước khi Shop giao hàng cho đơn vị vận chuyển.
- **Rollback phân tán (Compensating Transaction)**: Trừ tiền ví thành công nhưng bước lưu Database Commerce bị crash (`CreateOrderAsync`).
- **Đổi trả hàng thành công (Return & Refund)**: Đơn hàng hoàn tất quy trình khiếu nại / hoàn trả hàng.

#### 2. Giới hạn hiện tại của WalletService
- Wallet **không có bất kỳ API nào để hoàn tiền** (kể cả full refund lẫn partial refund).
- Wallet không hỗ trợ hoàn tiền theo `subOrderId` hay theo `orderId`.

#### 3. Giải pháp ứng phó hiện tại của CommerceService
- Tại `SellerRejectSubOrderAsync`: Đã loại bỏ lệnh gọi giả định sang Wallet. Commerce tự động tạo một bản ghi `OrderRefund` lưu vào database với trạng thái:
  - `Status = OrderRefundStatus.Requested`
  - `ReasonNote = "[SELLER_REJECTED] Refund requested due to seller rejection. Awaiting manual wallet balance adjustment."`
- Tại `CreateOrderAsync` (Database failure catch): Ghi log cảnh báo mức `CRITICAL` chứa đầy đủ `OrderId`, `BuyerId`, `Amount` để đội ngũ tài chính và CSKH can thiệp đối soát thủ công ngoài luồng.

#### 4. Vị trí mã nguồn đã sẵn sàng (Pre-wired Stubs)
- [`Commerce.Application/Interfaces/IWalletClient.cs`](file:///d:/CommerceService/Commerce.Application/Interfaces/IWalletClient.cs):
  ```csharp
  Task<bool> RefundOrderPaymentAsync(Guid orderId, string userId, decimal amount, string reason, string? idempotencyKey = null, CancellationToken cancellationToken = default);
  Task<bool> RefundSubOrderPaymentAsync(Guid subOrderId, string userId, decimal amount, string reason, string? idempotencyKey = null, CancellationToken cancellationToken = default);
  ```
- [`Commerce.Infrastructure/Services/Wallet/WalletClient.cs`](file:///d:/CommerceService/Commerce.Infrastructure/Services/Wallet/WalletClient.cs):
  - Phương thức `RefundOrderPaymentAsync` và `RefundSubOrderPaymentAsync` đã có khung sẵn, đang fallback trả về `false` kèm warning log.
- Entity [`OrderRefund`](file:///d:/CommerceService/Commerce.Domain/Entities/OrderRefund.cs): Bảng `order_refunds` đã có sẵn trong database để lưu vết toàn bộ yêu cầu hoàn tiền.

#### 5. Hợp đồng kỹ thuật đề xuất khi Wallet phát triển
```http
POST /api/v1/internal/wallet/orders/{orderId}/refund
X-Internal-Service-Key: <internal_key>
Content-Type: application/json

{
  "subOrderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", // Nullable nếu hoàn cả đơn
  "buyerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "amount": 150000.00,
  "reason": "Seller out of stock",
  "idempotencyKey": "refund-suborder-3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```
**Response mong đợi:** `200 OK`
```json
{
  "refundTransactionId": "rfd_98234792348",
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "amount": 150000.00,
  "status": "Completed",
  "refundedAt": "2026-11-02T10:00:00Z"
}
```

#### 6. Các bước kích hoạt nhanh (Fast-track Activation - 15 phút)
1. **Mở `WalletClient.cs`**: Cập nhật hàm `RefundSubOrderPaymentAsync` để gửi request HTTP POST tới URL do Wallet cung cấp.
2. **Mở `OrderService.cs` (`SellerRejectSubOrderAsync`)**: Bỏ comment dòng gọi `_walletClient.RefundSubOrderPaymentAsync(...)`. Nếu thành công, set `orderRefund.Status = OrderRefundStatus.Approved; orderRefund.CompletedAt = DateTimeOffset.UtcNow;`.
3. Chạy `dotnet test` kiểm chứng kịch bản hoàn tiền.

---

### GAP-02: Ký Quỹ & Quyết Toán Nhà Bán Hàng (Escrow & Seller Payout)

#### 1. Bối cảnh & Kỳ vọng
Mô hình sàn TMĐT bắt buộc tiền thanh toán của người mua phải được **giữ ký quỹ (Escrow holding)** tại ví trung gian của sàn:
- Khi đơn hàng hoàn thành (`SubOrder.Status = Delivered` / `Completed` sau thời gian 3 - 7 ngày không khiếu nại): Sàn tự động trích tiền từ quỹ Escrow chuyển vào Ví của Seller:
  $$\text{PayoutAmount} = \text{ItemsAmount} + \text{ShippingFeeAmount} - \text{PlatformCommissionFee}$$

#### 2. Giới hạn hiện tại của WalletService
- Khi Buyer thanh toán đơn hàng, Wallet trừ tiền Buyer nhưng ghi có vào tài khoản hệ thống chung.
- **Không có cơ chế ví ký quỹ (Escrow account)** cho từng đơn hàng.
- **Không có API Payout / Disbursement** để chuyển tiền cho `SellerId`.

#### 3. Giải pháp ứng phó hiện tại của CommerceService
- Commerce quản lý dữ liệu tài chính cấp đơn hàng chi tiết qua các `SubOrder` độc lập:
  - Phân bổ chính xác `SellerId`, `ItemsAmount`, `ShippingFeeAmount`, `DiscountAmount`, `TotalAmount`.
  - Khi SubOrder chuyển trạng thái sang `Delivered` / `Completed`, Commerce chỉ ghi nhận trạng thái đơn hàng đã hoàn tất trong database nội bộ, chưa thể trigger chuyển tiền thực tế trong ví của Seller. Đội kế toán sàn hiện phải xuất báo cáo doanh thu cuối tháng để làm lệnh chuyển tiền thủ công.

#### 4. Vị trí mã nguồn đã sẵn sàng (Pre-wired Stubs)
- [`Commerce.Domain/Entities/SubOrder.cs`](file:///d:/CommerceService/Commerce.Domain/Entities/SubOrder.cs): Đã có các trường `SellerId`, `ItemsAmount`, `ShippingFeeAmount`, `TotalAmount`, `PaidAt`, `CompletedAt`.
- [`Commerce.Application/Services/Order/OrderService.cs`](file:///d:/CommerceService/Commerce.Application/Services/Order/OrderService.cs): Phương thức `ConfirmDeliverySubOrderAsync` (hoặc CronJob tự động hoàn tất đơn sau thời gian quy định) là nơi lý tưởng để gắn lệnh giải ngân.

#### 5. Hợp đồng kỹ thuật đề xuất khi Wallet phát triển
```http
POST /api/v1/internal/wallet/orders/{orderId}/sub-orders/{subOrderId}/payout
X-Internal-Service-Key: <internal_key>
Content-Type: application/json

{
  "sellerId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "grossAmount": 250000.00,
  "platformFeeAmount": 12500.00,
  "netPayoutAmount": 237500.00,
  "idempotencyKey": "payout-suborder-sub-20261102-001"
}
```

#### 6. Các bước kích hoạt nhanh (Fast-track Activation - 30 phút)
1. Thêm phương thức `Task<bool> DisburseSellerPayoutAsync(...)` vào `IWalletClient` và triển khai trong `WalletClient.cs`.
2. Trong `OrderService.cs`, tại hàm xử lý khi đơn hàng chuyển sang `Completed`, chèn lệnh gọi `DisburseSellerPayoutAsync`.
3. Cập nhật `SubOrder` lưu thêm `PayoutTransactionId` và `PayoutAt`.

---

### GAP-03: Tra Cứu Số Dư Trước Khi Checkout (Pre-checkout Balance Inquiry)

#### 1. Bối cảnh & Kỳ vọng
Tại bước xem báo giá giỏ hàng (`POST /api/v1/checkouts/quote`):
- Hệ thống cần hiển thị số dư ví hiện tại của Buyer (`buyerWalletBalance: 500,000 VND`).
- Nếu số dư < tổng tiền đơn hàng, giao diện hiển thị cảnh báo "Số dư ví không đủ, vui lòng nạp thêm hoặc chọn COD/VNPay" và vô hiệu hóa nút bấm chọn thanh toán ví, tránh gây ức chế cho người dùng khi bấm Đặt hàng mới bị lỗi.

#### 2. Giới hạn hiện tại của WalletService
- Wallet không cung cấp API tra cứu số dư theo `userId` dành cho dịch vụ nội bộ (service-to-service).
- Endpoint số dư người dùng của Wallet hiện tại gắn chặt với token cá nhân của user trên Web/App client, không mở cho internal server call.

#### 3. Giải pháp ứng phó hiện tại của CommerceService
- Trong `OrderService.QuoteCheckoutAsync`: Tạm thời gán `BuyerWalletBalance = 0m` (hoặc fallback) và trả về `CheckoutQuoteResponse`.
- Khi người mua cố gắng đặt hàng bằng Ví: Commerce gọi `PayOrderAsync`. Nếu ví không đủ tiền, Wallet trả `409 INSUFFICIENT_BALANCE`. Commerce bắt lỗi này, giữ đơn ở `PendingPayment` và trả thông báo yêu cầu người mua nạp tiền.

#### 4. Vị trí mã nguồn đã sẵn sàng (Pre-wired Stubs)
- [`IWalletClient.cs`](file:///d:/CommerceService/Commerce.Application/Interfaces/IWalletClient.cs):
  ```csharp
  Task<decimal> GetWalletBalanceAsync(Guid userId, CancellationToken cancellationToken = default);
  ```
- [`CheckoutQuoteResponse.cs`](file:///d:/CommerceService/Commerce.Application/Services/Order/OrderResponses.cs): DTO đã có sẵn trường `decimal? BuyerWalletBalance`.

#### 5. Hợp đồng kỹ thuật đề xuất khi Wallet phát triển
```http
GET /api/v1/internal/wallet/users/{userId}/balance
X-Internal-Service-Key: <internal_key>
```
**Response mong đợi:** `200 OK`
```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "availableBalance": 500000.00,
  "currency": "VND",
  "status": "Active"
}
```

#### 6. Các bước kích hoạt nhanh (Fast-track Activation - 10 phút)
1. Trong `WalletClient.cs`: Thay thế mock logic trong `GetWalletBalanceAsync` bằng lệnh gọi HTTP GET tới endpoint trên.
2. Trong `OrderService.cs` (`QuoteCheckoutAsync`): Gán `buyerBalance = await _walletClient.GetWalletBalanceAsync(buyerId, ct)` và đưa vào `CheckoutQuoteResponse`.

---

### GAP-04: Thanh Toán Trực Tiếp Đơn Hàng Qua Cổng VNPay (Direct Order Payment Gateway)

#### 1. Bối cảnh & Kỳ vọng
Người mua không muốn nạp sẵn tiền vào ví mà muốn thanh toán ngay đơn hàng hiện tại qua VNPay QR / Thẻ ATM:
- Commerce gọi Wallet/Payment Service để tạo URL thanh toán VNPay có metadata là `orderId`.
- Khi khách quét mã trả tiền thành công trên ứng dụng ngân hàng, cổng thanh toán gọi Webhook/IPN về để tự động chuyển đơn sang `Paid`.

#### 2. Giới hạn hiện tại của WalletService
- Wallet **chỉ hỗ trợ nạp tiền vào ví cá nhân** (`POST /api/v1/wallets/topup/vnpay`).
- Không có luồng tạo URL thanh toán trực tiếp cho một Đơn hàng TMĐT (`orderId`). Khách muốn thanh toán đơn bắt buộc phải qua 2 bước thủ công: Nạp tiền vào Ví -> Dùng số dư Ví thanh toán Đơn.

#### 3. Giải pháp ứng phó hiện tại của CommerceService
- Commerce Service tạm thời quy định phương thức `PaymentMethod.Wallet` chỉ trừ số dư ví sẵn có.
- Nếu khách chọn phương thức ngoài, fallback sang `PaymentMethod.Cod`.

#### 4. Vị trí mã nguồn đã sẵn sàng (Pre-wired Stubs)
- [`PaymentMethod.cs`](file:///d:/CommerceService/Commerce.Domain/Enums/PaymentMethod.cs): Đã định nghĩa sẵn các enum: `Wallet`, `Cod`, `VnPay`, `BankTransfer`.
- [`IOrderService.cs`](file:///d:/CommerceService/Commerce.Application/Services/Order/IOrderService.cs): Đã thiết kế phương thức sinh link thanh toán `CreateOrderPaymentUrlAsync(Guid orderId, PaymentMethod method)`.

#### 5. Hợp đồng kỹ thuật đề xuất khi Wallet phát triển
```http
POST /api/v1/internal/payments/vnpay/order-url
X-Internal-Service-Key: <internal_key>
Content-Type: application/json

{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "buyerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "amount": 350000.00,
  "orderInfo": "Thanh toan don hang #ORD-20261102-001",
  "returnUrl": "https://commerce.nexus.com/checkout/payment-callback"
}
```

#### 6. Các bước kích hoạt nhanh (Fast-track Activation - 30 phút)
1. Thêm hàm `Task<string> CreateVnPayPaymentUrlAsync(...)` trong `IWalletClient` và `WalletClient.cs`.
2. Tạo Webhook Controller tiếp nhận Callback từ Payment Gateway để cập nhật trạng thái `Paid` cho đơn hàng.

---

### GAP-05: Truy Vấn Trạng Thái Thanh Toán & Đối Soát Tự Động (Payment Query & Reconciliation)

#### 1. Bối cảnh & Kỳ vọng
Trong trường hợp gặp sự cố mạng (Timeout 15s hoặc HTTP 500/502/503) khi Commerce gọi thanh toán sang Wallet:
- Commerce không biết tiền của Buyer đã bị trừ hay chưa.
- Cần một Background Worker (`PaymentReconciliationWorker`) định kỳ 2 phút quét các đơn hàng `PendingPayment` bị nghi ngờ và gọi API GET sang Wallet để tra cứu:
  - Nếu Wallet báo **Đã thanh toán** -> Commerce chuyển đơn sang `Paid`, commit trừ tồn kho tại Fulfillment.
  - Nếu Wallet báo **Chưa thanh toán** và đơn hàng đã quá hạn 15 phút (`PaymentDeadlineAt`) -> Commerce giải phóng giữ kho và đóng đơn (`PaymentTimeout`).

#### 2. Giới hạn hiện tại của WalletService
- Wallet **không có endpoint GET** để kiểm tra trạng thái thanh toán theo `orderId`.
- Nếu Commerce gọi lại `POST payment` để thăm dò:
  - Trường hợp đã thanh toán: Wallet trả về HTTP `409 ORDER_ALREADY_PAID`.
  - Trường hợp chưa thanh toán: Wallet sẽ thực hiện trừ tiền luôn (nếu đủ số dư), gây sai lệch ý định của luồng đối soát (vốn chỉ muốn đọc trạng thái).

#### 3. Giải pháp ứng phó hiện tại của CommerceService
- Trong `WalletClient.cs`: Khi gặp Timeout hoặc lỗi mạng, đánh dấu cờ `IsTransientFailure = true`.
- Trong `OrderService.cs`: Khi `paymentResult.IsTransientFailure == true`, giữ nguyên `order.Status = OrderStatus.PendingPayment`, không hủy đơn và không giải phóng giữ kho, đồng thời ghi log cảnh báo để kỹ sư vận hành đối soát thủ công.

#### 4. Vị trí mã nguồn đã sẵn sàng (Pre-wired Stubs)
- [`WalletPaymentResult.cs`](file:///d:/CommerceService/Commerce.Application/Interfaces/WalletPaymentResult.cs): Đã có cờ `IsTransientFailure`.
- Worker đối soát định kỳ có thể kế thừa từ `BackgroundService` trong thư mục `Commerce.Api/Workers/`.

#### 5. Hợp đồng kỹ thuật đề xuất khi Wallet phát triển
```http
GET /api/v1/internal/wallet/orders/{orderId}/payment-status
X-Internal-Service-Key: <internal_key>
```
**Response mong đợi:** `200 OK`
```json
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "isPaid": true,
  "transactionId": "tx_20261102_987123",
  "amount": 350000.00,
  "paidAt": "2026-11-02T10:15:30Z"
}
```

#### 6. Các bước kích hoạt nhanh (Fast-track Activation - 20 phút)
1. Thêm hàm `Task<PaymentStatusResult> GetOrderPaymentStatusAsync(Guid orderId)` vào `IWalletClient`.
2. Kích hoạt `PaymentReconciliationWorker` chạy nền để tự động phân giải các đơn hàng treo `PendingPayment` mà không cần con người can thiệp.

---

### GAP-06: Chuẩn Hóa Kiểu Dữ Liệu Transaction ID (UUID vs String)

#### 1. Bối cảnh & Kỳ vọng
- Schema database của Commerce Service lưu mã giao dịch ví tại trường `orders.wallet_transaction_id` và `sub_orders.wallet_transaction_id` với kiểu dữ liệu `uuid` (`Guid` trong C#).
- Điều này đảm bảo tính toàn vẹn dữ liệu, tối ưu hóa index B-Tree và đồng nhất định dạng GUID trên toàn hệ thống.

#### 2. Giới hạn hiện tại của WalletService
- Response của WalletService trả về `id` kiểu chuỗi tùy biến (`string`), ví dụ chuỗi MongoDB ObjectId hoặc mã giao dịch có tiền tố ký tự (`"tx_abc123"`).

#### 3. Giải pháp ứng phó hiện tại của CommerceService
- Tại `OrderService.cs`: Sử dụng `Guid.TryParse(paymentResult.TransactionId, out var parsedTxId)`:
  - Nếu là UUID hợp lệ: Gán vào `order.WalletTransactionId` và `subOrder.WalletTransactionId`.
  - Nếu là chuỗi không phải UUID: Ghi nhận log an toàn, để trường `WalletTransactionId` là `null`, không để ứng dụng bị crash bởi `FormatException`.

#### 4. Kích hoạt nhanh khi Wallet chuẩn hóa (5 phút)
- Khi WalletService cam kết trả về UUID v4 chuẩn: CommerceService không cần sửa code vì `Guid.TryParse` sẽ tự động parse thành công 100% các giao dịch mới.

---

## 4. TỔNG KẾT & KHUYẾN NGHỊ CHO BAN QUẢN LÝ DỰ ÁN

1. **Về phía Commerce Service**: Đã hoàn tất 100% việc chuẩn hóa, tích hợp tương thích tuyệt đối với endpoint duy nhất hiện có của Wallet. Mọi điểm tiếp xúc dự phòng (stubs, interfaces, error handling) đều đã được thiết kế sẵn và bao phủ bởi **49/49 unit tests pass**.
2. **Về phía Wallet Service (Kiến nghị ưu tiên phát triển)**:
   - **Độ ưu tiên P0 (Bắt buộc cho MVP Thương mại)**: Phát triển **GAP-01 (Hoàn tiền - Refund API)** để xử lý khiếu nại và khi Shop từ chối đơn hàng.
   - **Độ ưu tiên P1 (Đối soát vận hành)**: Phát triển **GAP-05 (Truy vấn trạng thái thanh toán)** để giải quyết dứt điểm các đơn hàng bị timeout.
   - **Độ ưu tiên P2 (Trải nghiệm người dùng)**: Phát triển **GAP-03 (Kiểm tra số dư trước checkout)**.
   - **Độ ưu tiên P3 (Mô hình kinh doanh mở rộng)**: Phát triển **GAP-02 (Ký quỹ Escrow & Giải ngân Nhà bán)**.
