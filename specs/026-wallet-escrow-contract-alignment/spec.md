# Feature Specification: Wallet Escrow Contract Alignment & Advanced Financial Error Handling

**Feature Branch**: `026-wallet-escrow-contract-alignment`  
**Created**: 2026-11-17  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  
**Input**: Phân tích hợp đồng tích hợp WalletService từ file `commerce-wallet-handoff.md` (17/11/2026), chuẩn hóa cơ chế ký quỹ Escrow, đóng băng cổng thanh toán Stripe trực tiếp, và quy định bất biến không hoàn tiền sau khi giao hàng thành công.

---

## 1. Executive Summary & Architectural Goals

`CommerceService` và `WalletService` tương tác tài chính 100% qua chuẩn RESTful API. Đặc tả này chuẩn hóa giao thức kết nối dựa trên bàn giao chính thức của WalletService ngày 17/11/2026:

1. **Chuẩn hóa Ký quỹ Đơn mẹ (Master Order Escrow Mapping)**:
   - Khi Người mua thanh toán đơn hàng bằng ví (`PaymentMethod.Wallet`), tiền ký quỹ `OrderEscrow` được Wallet khởi tạo theo định danh Đơn hàng mẹ (`orderId`).
   - Khi Người mua hủy toàn bộ đơn hàng trước khi giao, CommerceService chuyển sang gọi endpoint hoàn tiền Đơn mẹ:
     ```http
     POST /api/v1/wallets/orders/{orderId}/refund
     ```
     thay vì gọi lặp từng đơn con, đảm bảo khấu trừ chính xác vào quỹ `OrderEscrow` gốc, triệt tiêu nguy cơ lỗi lệch số dư hoặc `409 ORDER_ESCROW_INSUFFICIENT`.
2. **Xử lý Mã lỗi Tài chính Chuyên biệt (Advanced ProblemDetails Matrix)**:
   - Nhận diện và xử lý chi tiết các mã lỗi mới từ Wallet:
     - `409 ORDER_ESCROW_INSUFFICIENT`: Quỹ ký quỹ không đủ giải ngân/hoàn tiền $\rightarrow$ Ghi log đối soát khẩn cấp (`Flagged for financial audit`).
     - `409 REFUND_EXCEEDS_PAYMENT`: Số tiền yêu cầu hoàn vượt quá số tiền khách đã thanh toán.
     - `409 SETTLEMENT_CONFLICT` / `409 PAYMENT_ORDER_CONFLICT`: Giao dịch bị xung đột thông tin đối soát.
     - `503 FEATURE_DISABLED`: Cảnh báo hạ tầng khi cờ `ReleaseFeatures:LegacyCommerceCreditsEnabled` bên WalletService bị tắt.
3. **Đóng băng Phương thức Stripe Trực tiếp (Stripe Direct Checkout Freeze)**:
   - Tạm thời đóng băng, không mở cổng thanh toán Stripe Direct trên bề mặt API CommerceService do WalletService chưa có Outbound Webhook báo trạng thái đơn về Commerce, chưa cài đặt API hủy phiên (`Cancel`), và chưa hỗ trợ hoàn tiền Stripe (`Stripe Refund`).
4. **Quy tắc Bất biến: Không hoàn tiền sau khi giao hàng thành công (Final Sale Invariant)**:
   - Khi đơn hàng đạt trạng thái `Delivered` hoặc `Completed`, dòng tiền ký quỹ chuyển dứt điểm cho Người bán (ngay khi Người mua bấm nhận hàng hoặc sau 3 ngày).
   - Hệ thống **tuyệt đối không hỗ trợ tính năng hoàn tiền cho Người mua sau khi đã giao hàng thành công**. Mọi khiếu nại phát sinh sau giao hàng phải qua kênh tranh chấp hành chính ngoại vi, không tự ý can thiệp rút tiền từ ví Người bán.

---

## 2. User Scenarios & Acceptance Criteria

### User Story 1 - Master Order Cancellation Full Escrow Drawdown (Priority: P1)
**As a** buyer cancelling my entire order before shipment,  
**I want** the system to refund the entire payment directly from the master order escrow pool,  
**So that** my money is refunded in full to my wallet immediately without child-order escrow mismatches.

- **Scenario 1.1 (Successful Full Refund)**: Given a master order paid via Wallet with `order.TotalAmount = 500,000 VND`, when the buyer cancels the order, CommerceService calls `POST /api/v1/wallets/orders/{orderId}/refund` with `{ userId, amount: 500000, reason }`. Wallet returns HTTP 200, Commerce records `OrderRefund.Status = Completed`.
- **Scenario 1.2 (Feature Flag Disabled in Wallet)**: Given the Wallet environment has `LegacyCommerceCreditsEnabled = false`, when refund is invoked, Wallet returns HTTP 503 `FEATURE_DISABLED`. Commerce catches this code, logs an explicit DevOps warning, and records `OrderRefund.Status = Requested` for subsequent worker retry.

### User Story 2 - Sub-Order Escrow Payout & Conflict Governance (Priority: P2)
**As a** seller whose package has been delivered and confirmed,  
**I want** my net earnings (`SellerReceiveAmount`) to be transferred to my personal wallet,  
**So that** I receive accurate compensation while platform commission remains securely with the marketplace.

- **Scenario 2.1 (Accurate Payout)**: Given a delivered SubOrder with `SellerReceiveAmount = 180,000 VND`, when receipt is confirmed, Commerce calls `POST /api/v1/wallets/sub-orders/{subOrderId}/settle` with `{ sellerId, amount: 180000 }`. Wallet returns HTTP 200 `type: "EscrowRelease"`, SubOrder transitions to `Completed`.
- **Scenario 2.2 (Settlement Conflict Replay)**: Given network retry sends duplicate settle request, Wallet returns HTTP 409 `SETTLEMENT_CONFLICT`. `WalletClient` detects the conflict, marks result as false, logs reconciliation warning without crashing the service.

---

## 3. Technical Specifications & Contract Matrix

### 3.1 Endpoint Mapping

| Nghiệp vụ | Endpoint WalletService | Header định danh | Body |
| :--- | :--- | :--- | :--- |
| **Thanh toán đơn hàng** | `POST /api/v1/wallets/orders/{orderId}/payment` | `x-user-id: <buyerGuid>` | `{ "amount": <totalAmount> }` |
| **Tra cứu số dư ví** | `GET /api/v1/wallets/me` | `x-user-id: <buyerGuid>` | Không có |
| **Hoàn tiền Đơn hàng mẹ** | `POST /api/v1/wallets/orders/{orderId}/refund` | Không bắt buộc | `{ "userId": "<buyerGuid>", "amount": <amount>, "reason": "<text>" }` |
| **Hoàn tiền Đơn hàng con** | `POST /api/v1/wallets/sub-orders/{subOrderId}/refund` | Không bắt buộc | `{ "userId": "<buyerGuid>", "amount": <amount>, "reason": "<text>" }` |
| **Quyết toán cho Shop** | `POST /api/v1/wallets/sub-orders/{subOrderId}/settle` | Không bắt buộc | `{ "sellerId": "<sellerGuid>", "amount": <amount> }` |

### 3.2 Error Handling & ProblemDetails Parser

Cập nhật `WalletClient.ParseProblemDetails` nhận diện:
1. `INSUFFICIENT_BALANCE` / `INSUFFICIENT_WALLET_BALANCE` (HTTP 400).
2. `ORDER_ESCROW_INSUFFICIENT` (HTTP 409): Quỹ ký quỹ không đủ.
3. `REFUND_EXCEEDS_PAYMENT` (HTTP 409): Số tiền hoàn vượt quá khoản đã trả.
4. `FEATURE_DISABLED` (HTTP 503): Cờ tính năng `LegacyCommerceCreditsEnabled` chưa bật.
