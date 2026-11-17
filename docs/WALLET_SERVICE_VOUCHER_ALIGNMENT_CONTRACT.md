# WALLET SERVICE — VOUCHER & ESCROW SETTLEMENT ALIGNMENT CONTRACT

> **Target Service**: `WalletService` (Port `8004`, `wallet_db`)  
> **Calling Service**: `CommerceService` (Port `8003`) via `IWalletClient` (`WalletClient.cs`)  
> **Protocol**: Synchronous REST over HTTP/JSON  
> **Security Headers**: `X-Internal-Service-Key`, `x-user-id`, W3C `traceparent`  
> **Status**: RATIFIED & IMPLEMENTED  
> **Last Updated**: 2026-11-17  

---

## 1. Mục đích Tài liệu

Tài liệu này xác lập sự thống nhất về mặt **dòng tiền (Cashflow)**, **sổ cái ký quỹ (Escrow Ledger)** và **hợp đồng API** giữa `CommerceService` và `WalletService` theo mô hình hoa hồng Shopee / TikTok Shop (Feature 024):
1. **Số tiền thanh toán trừ từ Ví Buyer (`TotalAmount`)** đã được giảm trừ đầy đủ 3 tầng voucher (Product, Shop, System) cộng phí vận chuyển. Khách hàng không bị thu thêm bất kỳ phụ phí nào.
2. **Số tiền giải ngân về Ví Seller (`SellerReceiveAmount`)** đã được trừ sẵn **5% phí hoa hồng sàn**. `WalletService` **TUYỆT ĐỐI KHÔNG ĐƯỢC tự động trừ thêm phí hoa hồng một lần nữa**.
3. **Số tiền hoàn trả khi Hủy đơn (`RefundAmount`)** là chính xác số tiền `TotalAmount` thực tế mà Buyer đã trả cho SubOrder đó.

---

## 2. Mô hình Kế toán & Bất biến Sổ cái Ký quỹ (Escrow Invariant)

Với mỗi đơn hàng con (`SubOrder`), `CommerceService` tính toán 5 giá trị tài chính độc lập:
- $\text{ItemsAmount}$: Giá trị hàng niêm yết gốc trên Catalog.
- $\text{NetSeller} = \text{ItemsAmount} - \text{ProductDiscount} - \text{ShopDiscount}$: Doanh thu thuần của Shop sau mã giảm giá do Shop tự chịu.
- $\text{PlatformFeeAmount} = \text{Math.Round}(\text{NetSeller} \times 5\%, 0, \text{MidpointRounding.AwayFromZero})$: Hoa hồng sàn khấu trừ từ Seller.
- $\text{SellerReceiveAmount} = \text{NetSeller} - \text{PlatformFeeAmount}$: Tiền thực nhận của Seller sau hoa hồng sàn.
- $\text{SystemDiscountAmount}$: Số tiền giảm do Voucher Sàn tài trợ.
- $\text{ShippingFeeAmount}$: Tiền ship giao hàng cho đơn vị vận chuyển (GHN).

### Bất biến kế toán cân bằng 100% từng đồng:
$$\boxed{\text{Số tiền Buyer nạp vào Ký quỹ} \equiv \text{Tiền giải ngân Ví Seller} + (\text{Hoa hồng sàn} - \text{Voucher sàn tài trợ}) + \text{Tiền ship trả ĐVVC}}$$

$$\text{TotalAmount} \equiv \text{SellerReceiveAmount} + (\text{PlatformFeeAmount} - \text{SystemDiscountAmount}) + \text{ShippingFeeAmount}$$

---

## 3. Các Điểm Giao tiếp API Giữa CommerceService và WalletService

### 3.1. Trừ tiền Ví Buyer khi Đặt hàng (`PayOrderAsync`)
- **Thời điểm gọi**: Khi Buyer đặt đơn chọn phương thức thanh toán Ví (`PaymentMethod = Wallet`).
- **Endpoint**: `POST http://wallet-service:8004/api/v1/internal/wallets/pay-order`
- **Payload CommerceService gửi sang**:
  ```json
  {
    "orderId": "8fa85f64-5717-4562-b3fc-2c963f66afa8",
    "buyerId": "1fa85f64-5717-4562-b3fc-2c963f66afa1",
    "amount": 960000.0,
    "description": "Payment for master order ORD-20261117-000001"
  }
  ```
- **Quy tắc WalletService**:
  - `amount` đã là số tiền thực tế Buyer cần trả sau khi trừ toàn bộ voucher:
    $$\text{amount} = \sum \text{ItemsAmount} - \sum \text{Discounts} + \sum \text{ShippingFee}$$
  - Trừ tiền từ số dư khả dụng (`AvailableBalance`) của Buyer và đưa toàn bộ số tiền này vào tài khoản trung gian ký quỹ (`EscrowHoldingBalance`).
- **Phản hồi thành công**:
  ```json
  {
    "isSuccess": true,
    "transactionId": "tx-8fa85f64-5717-4562-b3fc-2c963f66afa8",
    "balanceAfter": 540000.0
  }
  ```

---

### 3.2. Giải ngân tiền cho Seller khi Giao hàng Thành công (`SettleSellerSubOrderAsync`)
- **Thời điểm gọi**: 
  - Khách hàng bấm "Đã nhận được hàng" (`POST /api/v1/sub-orders/{id}/confirm-receipt`).
  - Hoặc Webhook GHN báo `Delivered` và hết thời gian khiếu nại (Escrow Lock Time).
- **Endpoint**: `POST http://wallet-service:8004/api/v1/internal/wallets/settle-seller-suborder`
- **Payload CommerceService gửi sang**:
  ```json
  {
    "subOrderId": "7fa85f64-5717-4562-b3fc-2c963f66afa7",
    "sellerId": "9fa85f64-5717-4562-b3fc-2c963f66afa9",
    "amount": 361000.0,
    "idempotencyKey": "settle-7fa85f64-5717-4562-b3fc-2c963f66afa7"
  }
  ```
- **LƯU Ý ĐẶC BIỆT DÀNH CHO WALLET TEAM**:
  - Số tiền `amount` (ví dụ: **361.000đ**) chính là `SellerReceiveAmount`.
  - **Phí hoa hồng sàn 5% (19.000đ) ĐÃ ĐƯỢC COMMERCESERVICE KHẤU TRỪ TRƯỚC**.
  - `WalletService` **chỉ việc cộng đúng 361.000đ vào ví của Seller**, trích từ tài khoản Ký quỹ (`EscrowHoldingBalance`).
  - **CẤM** viết code trừ tiếp 5% hoa hồng trên số tiền này (nếu trừ tiếp sẽ thành trừ 2 lần hoa hồng của Shop).

---

### 3.3. Hoàn tiền khi Đơn hàng bị Hủy hoặc Trả hàng (`RefundSubOrderPaymentAsync`)
- **Thời điểm gọi**:
  - Khách hàng hủy đơn trước khi giao (`CancelOrderAsync`).
  - Người bán từ chối phục vụ đơn (`SellerRejectSubOrderAsync`).
  - Giao hàng thất bại 3 lần và chuyển trạng thái `Cancelled`.
- **Endpoint**: `POST http://wallet-service:8004/api/v1/internal/wallets/refund-suborder`
- **Payload CommerceService gửi sang**:
  ```json
  {
    "subOrderId": "7fa85f64-5717-4562-b3fc-2c963f66afa7",
    "buyerId": "1fa85f64-5717-4562-b3fc-2c963f66afa1",
    "amount": 390000.0,
    "reason": "Buyer cancelled order before shipping.",
    "idempotencyKey": "refund-suborder-cancel-7fa85f64-5717-4562-b3fc-2c963f66afa7"
  }
  ```
- **Quy tắc WalletService**:
  - Hoàn trả chính xác số tiền `amount` (là `TotalAmount` của SubOrder đó: ví dụ 390.000đ) từ tài khoản Ký quỹ (`EscrowHoldingBalance`) quay lại số dư khả dụng (`AvailableBalance`) của Buyer.
  - Sử dụng `idempotencyKey` để bảo đảm nếu CommerceService gọi retry thì không bị hoàn tiền 2 lần.

---

## 4. Bảng Đối Soát Tài Chính Mẫu Cho Đội Ngũ Wallet

Giả sử giao dịch đơn hàng thực tế như sau:

| Mã định danh | Giá trị tính toán | Trách nhiệm xử lý của WalletService |
| :--- | :---: | :--- |
| **Master Order** (`TotalAmount`) | **960.000đ** | Trừ 960.000đ ví Buyer $\rightarrow$ Nhập vào Ký quỹ |
| **SubOrder A** (`TotalAmount`) | **390.000đ** | Ký quỹ giữ 390.000đ |
| ↳ `SellerReceiveAmount` (Shop A) | **361.000đ** | Khi hoàn thành: Xuất ký quỹ **361.000đ** nạp vào Ví Shop A |
| ↳ `PlatformFeeAmount` (Hoa hồng Shop A) | **19.000đ** | Khi đối soát: Sàn giữ lại trong doanh thu hoa hồng |
| ↳ `SystemDiscountAmount` (Voucher Sàn) | **-20.000đ** | Sàn chi từ quỹ Marketing bù cho đơn Shop A |
| ↳ `ShippingFeeAmount` (Ship Shop A) | **30.000đ** | Xuất ký quỹ thanh toán cho tài khoản Nhà vận chuyển GHN |
| **SubOrder B** (`TotalAmount`) | **570.000đ** | Ký quỹ giữ 570.000đ |
| ↳ `SellerReceiveAmount` (Shop B) | **541.500đ** | Khi hoàn thành: Xuất ký quỹ **541.500đ** nạp vào Ví Shop B |
| ↳ `PlatformFeeAmount` (Hoa hồng Shop B) | **28.500đ** | Khi đối soát: Sàn giữ lại trong doanh thu hoa hồng |
| ↳ `SystemDiscountAmount` (Voucher Sàn) | **-30.000đ** | Sàn chi từ quỹ Marketing bù cho đơn Shop B |
| ↳ `ShippingFeeAmount` (Ship Shop B) | **30.000đ** | Xuất ký quỹ thanh toán cho tài khoản Nhà vận chuyển GHN |

---

## 5. Checklist Kỹ thuật dành cho Wallet Service Team

- [ ] Đảm bảo API `POST /api/v1/internal/wallets/settle-seller-suborder` nhận trực tiếp số tiền `amount` và ghi có (Credit) nguyên vẹn số tiền đó vào ví người bán, không can thiệp trừ thêm hoa hồng.
- [ ] Xác nhận các API nội bộ hỗ trợ header `X-Internal-Service-Key` và W3C `traceparent`.
- [ ] Xác nhận tính Idempotent qua trường `idempotencyKey` trong cả hai lệnh Settle và Refund.
