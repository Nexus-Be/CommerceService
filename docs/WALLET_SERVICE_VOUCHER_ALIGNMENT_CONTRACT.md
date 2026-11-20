# WALLET SERVICE — VOUCHER, PAYMENT & SETTLEMENT ALIGNMENT CONTRACT

> **Target Service**: `WalletService` (Port `8008`, `wallet_db`)  
> **Calling Service**: `CommerceService` (Port `8003`) via `IWalletClient` (`WalletClient.cs`)  
> **Protocol**: Synchronous REST over HTTP/JSON  
> **Security Headers**: `x-user-id` (chuyển tiếp từ Gateway), W3C `traceparent` (`X-Internal-Service-Key` tạm tắt đồng bộ)  
> **Status**: ✅ RATIFIED & PRODUCTION-ALIGNED (ĐÃ ĐỒNG BỘ 100% VỚI CODEBASE THỰC TẾ)  
> **Last Updated**: 2026-11-17  

---

## 1. Mục đích Tài liệu & Tuyên bố Phạm vi (Scope & Invariants)

Tài liệu này xác lập sự thống nhất chính thức giữa `CommerceService` (Port 8003) và `WalletService` (Port 8008) về:
1. **Dòng tiền giao dịch (Cashflow Flow)**:
   - **Số tiền thanh toán trừ từ Ví Buyer (`TotalAmount`)**: Đã giảm trừ toàn bộ voucher hợp lệ 2 tầng Shopee (Shop Voucher + Platform Voucher) và cộng tiền vận chuyển. Buyer thanh toán đúng số tiền này, không phụ phí ẩn.
   - **Số tiền giải ngân về Ví Seller (`SellerReceiveAmount`)**: Đã được `CommerceService` **khấu trừ trước 5% phí hoa hồng sàn**. `WalletService` **tuyệt đối KHÔNG ĐƯỢC trừ thêm hoa hồng một lần nữa**.
   - **Số tiền hoàn trả khi Hủy đơn (`RefundAmount`)**: Bằng chính xác `TotalAmount` của SubOrder/Order đó đã thanh toán.
2. **Quy định về Escrow (Ký quỹ)**:
   - **Giai đoạn Hiện tại**: Mô hình Sổ cái ký quỹ trung gian (`EscrowHoldingBalance`) được **TẠM HOÃN (DEFERRED)** theo quyết định của WalletService.
   - Tiền thanh toán của Buyer được ghi nợ trực tiếp (Direct Debit), và khi hoàn tất đơn hoặc hủy đơn thì ghi có trực tiếp (Direct Credit) vào ví Seller/Buyer.
   - `CommerceService` đóng vai trò là **Bộ điều phối trạng thái (State Orchestrator)**, chỉ kích hoạt giải ngân khi đơn hàng chuyển sang trạng thái hoàn tất (`Delivered`/`confirm-receipt`) hoặc hoàn tiền khi đơn bị hủy (`Cancelled`).
3. **Phân định Trách nhiệm Thẩm định (Single Source of Truth)**:
   - `CommerceService` là **Nguồn sự thật duy nhất** chịu trách nhiệm kiểm tra tính hợp lệ của đơn hàng, quyền hạn người mua/người bán, và trần số tiền hoàn trả (`Refund Cap`).
   - `WalletService` là **Dịch vụ thi hành giao dịch (Execution Service)** thực thi trừ tiền, cộng tiền theo đúng chỉ định từ `CommerceService`.

---

## 2. Mô hình Kế toán & Bất biến Tài chính (Shopee Commission Model)

Với mỗi đơn hàng con (`SubOrder`), `CommerceService` tính toán các giá trị tài chính chuẩn hóa:
- $\text{ItemsAmount}$: Tổng giá trị hàng theo giá niêm yết Catalog.
- $\text{ShopDiscountAmount}$: Tiền giảm giá cấp Shop (Voucher sản phẩm hoặc Voucher toàn shop do Shop chi trả).
- $\text{NetSeller} = \text{ItemsAmount} - \text{ShopDiscountAmount}$: Doanh thu thuần của Shop.
- $\text{PlatformFeeAmount} = \text{Math.Round}(\text{NetSeller} \times 5\%, 0, \text{MidpointRounding.AwayFromZero})$: Hoa hồng sàn 5% khấu trừ từ Seller.
- $\text{SellerReceiveAmount} = \text{NetSeller} - \text{PlatformFeeAmount}$: Tiền thực nhận của Seller sau hoa hồng sàn.
- $\text{SystemDiscountAmount}$: Tiền giảm giá do Voucher Sàn tài trợ (Sàn bù tiền).
- $\text{ShippingFeeAmount}$: Tiền cước vận chuyển trả cho Đơn vị vận chuyển (GHN).

### Bất biến kế toán cân bằng tuyệt đối từng đồng:
$$\boxed{\text{TotalAmount} \equiv \text{SellerReceiveAmount} + (\text{PlatformFeeAmount} - \text{SystemDiscountAmount}) + \text{ShippingFeeAmount}}$$

*Giải thích*:
- Tiền Buyer trả = Tiền Seller nhận + (Hoa hồng sàn giữ lại - Tiền sàn bù voucher) + Cước ship GHN.
- Khách hàng thiếu tiền do Voucher sàn thì Sàn trích quỹ Marketing bù vào, đảm bảo Seller nhận đủ `SellerReceiveAmount`.

---

## 3. Chi tiết Hợp đồng Giao tiếp REST API (Đồng bộ Port 8008)

### 3.1. Trừ tiền Ví Buyer khi Đặt hàng (`PayOrderAsync`)
- **Thời điểm gọi**: Khi Buyer đặt đơn hàng Master Order chọn phương thức Ví (`PaymentMethod.Wallet`).
- **Endpoint**: `POST http://wallet-service:8008/api/v1/wallets/orders/{orderId}/payment`
- **Headers**:
  ```http
  x-user-id: 1fa85f64-5717-4562-b3fc-2c963f66afa1
  Content-Type: application/json
  traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
  ```
- **Payload CommerceService gửi sang**:
  ```json
  {
    "amount": 960000.0
  }
  ```
- **Quy tắc WalletService**:
  - `orderId` được lấy từ route URI, `buyerId` được lấy từ header `x-user-id`.
  - Trừ đúng số tiền `amount` khỏi số dư khả dụng (`AvailableBalance`) của Buyer.
  - Nếu số dư không đủ, trả về HTTP 400/409 kèm ProblemDetails mã lỗi `INSUFFICIENT_BALANCE`.
- **Phản hồi thành công (HTTP 200/201)**:
  ```json
  {
    "id": "tx-8fa85f64-5717-4562-b3fc-2c963f66afa8",
    "orderId": "8fa85f64-5717-4562-b3fc-2c963f66afa8",
    "amount": 960000.0,
    "balanceAfter": 540000.0,
    "createdAt": "2026-11-17T07:15:00Z"
  }
  ```

---

### 3.2. Giải ngân tiền cho Seller khi Hoàn thành Đơn (`SettleSellerSubOrderAsync`)
- **Thời điểm gọi**: 
  - Buyer nhấn "Đã nhận được hàng" (`POST /api/v1/sub-orders/{id}/confirm-receipt`).
  - Hoặc Webhook GHN báo `Delivered` và hết thời hạn khiếu nại.
- **Endpoint**: `POST http://wallet-service:8008/api/v1/wallets/sub-orders/{subOrderId}/settle`
- **Headers**:
  ```http
  Content-Type: application/json
  traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
  ```
- **Payload CommerceService gửi sang**:
  ```json
  {
    "sellerId": "9fa85f64-5717-4562-b3fc-2c963f66afa9",
    "amount": 361000.0
  }
  ```
- **LƯU Ý BẮT BUỘC DÀNH CHO WALLET SERVICE**:
  - `amount` là `SellerReceiveAmount` (ví dụ: **361.000đ**).
  - Phí sàn 5% (19.000đ) **ĐÃ ĐƯỢC COMMERCESERVICE KHẤU TRỪ TRƯỚC**.
  - WalletService **chỉ việc cộng đúng 361.000đ vào ví của `sellerId`**.
  - **CẤM** tự động khấu trừ thêm 5% hoa hồng trên số tiền này.
- **Cơ chế Idempotency**:
  - WalletService sử dụng `subOrderId` trên URL path làm khóa định danh giao dịch (`TransactionReference`).
  - Nếu CommerceService retry gọi lại cùng một `subOrderId` đã settle thành công, WalletService trả về kết quả thành công trước đó (hoặc HTTP 409 `SETTLEMENT_ALREADY_COMPLETED`), không ghi có 2 lần vào ví Seller.
- **Phản hồi thành công (HTTP 200/201)**:
  ```json
  {
    "isSuccess": true,
    "subOrderId": "7fa85f64-5717-4562-b3fc-2c963f66afa7",
    "sellerId": "9fa85f64-5717-4562-b3fc-2c963f66afa9",
    "amount": 361000.0,
    "transactionId": "tx-settle-7fa85f64"
  }
  ```

---

### 3.3. Hoàn tiền SubOrder khi Hủy hoặc Trả hàng (`RefundSubOrderPaymentAsync`)
- **Thời điểm gọi**:
  - Buyer hủy đơn trước khi giao (`CancelOrderAsync`).
  - Seller từ chối đơn hàng (`SellerRejectSubOrderAsync`).
  - Giao hàng thất bại 3 lần (`DeliveryFailed`).
- **Endpoint**: `POST http://wallet-service:8008/api/v1/wallets/sub-orders/{subOrderId}/refund`
- **Headers**:
  ```http
  Content-Type: application/json
  traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
  ```
- **Payload CommerceService gửi sang**:
  ```json
  {
    "userId": "1fa85f64-5717-4562-b3fc-2c963f66afa1",
    "amount": 390000.0,
    "reason": "Buyer cancelled sub-order before delivery."
  }
  ```
- **Quy tắc WalletService**:
  - Cộng lại chính xác số tiền `amount` vào số dư khả dụng (`AvailableBalance`) của `userId` (Buyer).
  - Sử dụng `subOrderId` trên route path để bảo đảm tính Idempotent: nếu nhận nhiều yêu cầu hoàn tiền cho cùng một `subOrderId`, không hoàn tiền vượt mức.
- **Phản hồi thành công (HTTP 200/201)**:
  ```json
  {
    "isSuccess": true,
    "subOrderId": "7fa85f64-5717-4562-b3fc-2c963f66afa7",
    "refundedAmount": 390000.0
  }
  ```

---

### 3.4. Hoàn tiền Master Order (`RefundOrderPaymentAsync`)
- **Thời điểm gọi**: Khi toàn bộ Master Order bị hủy ngay sau khi thanh toán thành công.
- **Endpoint**: `POST http://wallet-service:8008/api/v1/wallets/orders/{orderId}/refund`
- **Headers**:
  ```http
  Content-Type: application/json
  traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
  ```
- **Payload CommerceService gửi sang**:
  ```json
  {
    "userId": "1fa85f64-5717-4562-b3fc-2c963f66afa1",
    "amount": 960000.0,
    "reason": "Master order cancelled by customer."
  }
  ```
- **Phản hồi thành công (HTTP 200/201)**:
  ```json
  {
    "isSuccess": true,
    "orderId": "8fa85f64-5717-4562-b3fc-2c963f66afa8",
    "refundedAmount": 960000.0
  }
  ```

---

### 3.5. Tra cứu Số dư Ví Buyer (`GetWalletBalanceAsync`)
- **Thời điểm gọi**: Khi kiểm tra trước số dư trong màn hình Quote Checkout (`QuoteCheckoutAsync`).
- **Endpoint**: `GET http://wallet-service:8008/api/v1/wallets/me`
- **Headers**:
  ```http
  x-user-id: 1fa85f64-5717-4562-b3fc-2c963f66afa1
  ```
- **Phản hồi thành công (HTTP 200)**:
  ```json
  {
    "balance": 1500000.0
  }
  ```

---

## 4. Bảng Đối Soát Dòng Tiền Mẫu (Multi-Shop Order Audit Matrix)

Đơn hàng Master gồm 2 Shop (Shop A và Shop B):

| Khoản mục | Giá trị tính toán | Trách nhiệm xử lý của WalletService |
| :--- | :---: | :--- |
| **Master Order** (`TotalAmount`) | **960.000đ** | Trừ **960.000đ** từ ví của Buyer khi đặt đơn (`PayOrderAsync`) |
| **SubOrder A** (`TotalAmount`) | **390.000đ** | Giá trị thanh toán thuộc về gói hàng Shop A |
| ↳ `SellerReceiveAmount` (Shop A) | **361.000đ** | Khi giao thành công: Wallet **cộng đúng 361.000đ** vào ví Shop A |
| ↳ `PlatformFeeAmount` (Hoa hồng 5%) | **19.000đ** | Sàn giữ lại trong báo cáo doanh thu tài chính |
| ↳ `SystemDiscountAmount` (Voucher Sàn) | **-20.000đ** | Sàn bù tiền voucher cho Shop A |
| ↳ `ShippingFeeAmount` (Cước ship) | **30.000đ** | Thanh toán cho đơn vị vận chuyển GHN |
| **SubOrder B** (`TotalAmount`) | **570.000đ** | Giá trị thanh toán thuộc về gói hàng Shop B |
| ↳ `SellerReceiveAmount` (Shop B) | **541.500đ** | Khi giao thành công: Wallet **cộng đúng 541.500đ** vào ví Shop B |
| ↳ `PlatformFeeAmount` (Hoa hồng 5%) | **28.500đ** | Sàn giữ lại trong báo cáo doanh thu tài chính |
| ↳ `SystemDiscountAmount` (Voucher Sàn) | **-30.000đ** | Sàn bù tiền voucher cho Shop B |
| ↳ `ShippingFeeAmount` (Cước ship) | **30.000đ** | Thanh toán cho đơn vị vận chuyển GHN |

---

## 5. Tổng kết Trạng thái Đồng thuận Giữa Hai Team

| Hạng mục | Trạng thái đồng thuận | Ghi chú giải pháp |
| :--- | :---: | :--- |
| **1. Cổng kết nối (Port 8008)** | ✅ ĐÃ KHỚP | Cả hai service cùng cấu hình port `8008` (không dùng 8004 vì 8004 là AuctionService). |
| **2. Bỏ Escrow Holding Balance** | ✅ ĐÃ ĐỒNG THUẬN | Tạm hoãn sổ cái ký quỹ, áp dụng ghi nợ/ghi có trực tiếp vào ví theo chỉ định của CommerceService. |
| **3. Cấu trúc Endpoint REST** | ✅ ĐÃ KHỚP | Dùng đúng route `/api/v1/wallets/orders/...` và `/api/v1/wallets/sub-orders/...`. |
| **4. Header xác thực (`x-user-id`)** | ✅ ĐÃ KHỚP | Không đòi hỏi `X-Internal-Service-Key`, truyền `x-user-id` cho các route của người dùng. |
| **5. Cơ chế Idempotency** | ✅ ĐÃ ĐỒNG THUẬN | Dựa trên `orderId`/`subOrderId` trên URL path, không truyền trường riêng trong request body. |
| **6. Quyền thẩm định hoàn tiền** | ✅ ĐÃ ĐỒNG THUẬN | `CommerceService` là Single Source of Truth kiểm tra điều kiện và trần hoàn tiền trước khi gọi lệnh. |
| **7. Không trừ hoa hồng 2 lần** | ✅ NGUYÊN TẮC BẤT BIẾN | WalletService cộng nguyên vẹn `amount` nhận được trong lệnh Settle vào ví Seller. |
