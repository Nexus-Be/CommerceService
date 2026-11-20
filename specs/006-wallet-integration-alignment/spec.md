# Feature Specification: Wallet Integration Alignment with Handoff Contract

**Feature Branch**: `006-wallet-integration-alignment`  
**Created**: 2026-11-02  
**Status**: Ready for Implementation  
**Input**: Điều chỉnh tích hợp CommerceService ↔ WalletService theo đúng tài liệu bàn giao kỹ thuật (Handoff Contract).

---

## 1. Executive Summary & Problem Statement

Trước đây, `CommerceService` tự giả định các hành vi và giao thức của `WalletService`:
1. Gọi route `POST api/v1/wallets/checkout/direct` với payload `{ orderId, userId, amount }` và header `x-user-id`.
2. Giả định có header `Idempotency-Key`.
3. Giả định `WalletTransactionId` trả về có thể ép kiểu `Guid` (UUID).
4. Giả định có API hoàn tiền tự động `POST api/v1/wallets/refund`, API số dư `/balance`, và luồng tạo link thanh toán VNPay cho đơn hàng.

Theo **Tài liệu bàn giao tích hợp Commerce ↔ WalletService**:
- WalletService **CHỈ CÓ DUY NHẤT** endpoint thanh toán nội bộ:
  `POST /api/v1/internal/wallet/orders/{orderId}/payment`
  Payload body chỉ chứa `{ "amount": <decimal> }`.
  Xác thực qua `Authorization: Bearer <buyer JWT>` (chuyển tiếp token).
  Idempotency quản lý tự nhiên bằng `{orderId}` trong route path.
  Transaction ID trả về là chuỗi ký tự (`string`).
- WalletService **KHÔNG CÓ** các API: Refund tự động, Escrow/Payout seller, VNPay Order URL, hay route `/balance`.

---

## 2. User Scenarios & Testing *(mandatory)*

### User Story 1 - Thanh Toán Đơn Hàng Qua Hợp Đồng Chuẩn Wallet (Priority: P1)

Là một người mua (Buyer) đã xác thực, khi tôi bấm thanh toán đơn hàng qua Ví Nexus, hệ thống Commerce Service phải gọi sang Wallet Service theo đúng route nội bộ, chuyển tiếp JWT của tôi, và chỉ gửi số tiền hợp lệ của đơn hàng.

* **Independent Test**: Gửi request checkout đơn hàng bằng ví (`PaymentMethod: Wallet`). Mock WalletService nhận `POST /api/v1/internal/wallet/orders/{orderId}/payment`, header `Authorization: Bearer <token>`, body `{ "amount": totalAmount }`. Commerce nhận về transaction DTO và chuyển đơn sang `Paid`.
* **Acceptance Scenarios**:
  1. **Given** Đơn hàng hợp lệ và Buyer đã đăng nhập, **When** thanh toán ví, **Then** Commerce gọi `POST /api/v1/internal/wallet/orders/{orderId}/payment` với `Authorization: Bearer <buyer-token>` và body `{ "amount": totalAmount }`.
  2. **Given** Wallet trả về `200 OK` kèm `{ "id": "tx-123456", "status": "Success", "balanceAfter": 520000 }`, **When** Commerce nhận kết quả, **Then** chuyển trạng thái đơn sang `Paid`, lưu mã giao dịch và gọi commit trừ kho tại Fulfillment.

---

### User Story 2 - Xử Lý Transaction ID Chuỗi và Chống Crash Ép Kiểu (Priority: P1)

Là hệ thống Commerce Service, khi nhận Transaction ID dạng chuỗi từ Wallet, tôi phải xử lý an toàn: nếu là UUID hợp lệ thì lưu vào cột `WalletTransactionId`, nếu là chuỗi opaque thì vẫn ghi nhận mà không gây lỗi `FormatException` làm crash tiến trình checkout.

* **Independent Test**: Giả lập Wallet trả về ID chuỗi dạng `"txn-20261102-001"` hoặc UUID `"3fa85f64-5717-4562-b3fc-2c963f66afa6"`. Hệ thống xử lý mượt mà và chuyển trạng thái đơn thành công.
* **Acceptance Scenarios**:
  1. **Given** Wallet trả về `id` là UUID hợp lệ, **When** xử lý kết quả, **Then** lưu vào `order.WalletTransactionId` dạng `Guid`.
  2. **Given** Wallet trả về `id` là chuỗi không phải UUID (ví dụ: `"tx_99214"`), **When** xử lý kết quả, **Then** ghi log cảnh báo và không ném ngoại lệ làm gián đoạn việc ghi nhận đơn hàng `Paid`.

---

### User Story 3 - Xử Lý Ma Trận Mã Lỗi Dứt Khoát Từ Wallet (Priority: P1)

Là người mua, khi ví của tôi không đủ tiền hoặc tôi chưa có ví, hệ thống phải thông báo chính xác mã lỗi nghiệp vụ và giải phóng số lượng hàng đang giữ kho (Reservation Release).

* **Independent Test**: Giả lập Wallet trả về `404 WALLET_NOT_FOUND` hoặc `409 INSUFFICIENT_BALANCE`.
* **Acceptance Scenarios**:
  1. **Given** Buyer chưa mở ví (Wallet trả về 404 `WALLET_NOT_FOUND`), **When** checkout ví, **Then** Commerce giữ đơn ở `PendingPayment` (hoặc hủy theo chính sách), thông báo người mua chưa có ví và giải phóng kho.
  2. **Given** Ví không đủ số dư (Wallet trả về 409 `INSUFFICIENT_BALANCE`), **When** checkout ví, **Then** Commerce giữ đơn ở `PendingPayment`, ném thông báo số dư không đủ và giải phóng hàng giữ tạm.
  3. **Given** Request gọi lại với cùng `orderId` nhưng khác buyer hoặc amount (Wallet trả về 409 `ORDER_ALREADY_PAID`), **When** xử lý, **Then** chặn tạo đơn, không retry bừa bãi.

---

### User Story 4 - Chịu Lỗi Mạng & Kết Quả Chưa Xác Định (Timeout / 5xx) (Priority: P2)

Là hệ thống thương mại điện tử, khi cuộc gọi sang Wallet bị Timeout, mất kết nối hoặc nhận HTTP 5xx, kết quả thanh toán là "chưa xác định" (có thể Wallet đã trừ tiền thành công trước khi rớt mạng). Commerce PHẢI giữ nguyên đơn ở `PendingPayment` và GIỮ NGUYÊN việc khóa kho để đối soát, tuyệt đối KHÔNG vội vàng nhả kho hay báo lỗi thất bại.

* **Independent Test**: Giả lập `WalletClient` ném `TimeoutException` hoặc HTTP 500.
* **Acceptance Scenarios**:
  1. **Given** Cuộc gọi sang Wallet bị timeout sau khi gửi request, **When** bắt ngoại lệ, **Then** đơn hàng giữ trạng thái `PendingPayment`, không nhả kho, không trừ tiền lại bằng order ID mới, ghi log phục vụ đối soát.

---

### User Story 5 - Loại Bỏ Các Điểm Chạm Giả Định Chưa Được Hỗ Trợ (Priority: P2)

Là kiến trúc sư hệ thống, Commerce Service phải loại bỏ các cuộc gọi sang các endpoint giả định không tồn tại của Wallet:
- Bãi bỏ việc gọi `POST api/v1/wallets/refund`. Khi Shop từ chối đơn hàng đã thanh toán, Commerce chỉ tạo bản ghi `OrderRefund` với trạng thái `Requested` / `PendingManualProcess` để nhân viên CSKH đối soát thủ công, không gọi API hoàn tiền tự động giả định.
- Bãi bỏ việc gọi `POST api/v1/wallets/checkout/direct` sinh payment URL cho VNPay.

* **Independent Test**: Kiểm tra luồng `SellerRejectSubOrderAsync` khi đơn đã thanh toán: hệ thống tạo bản ghi `OrderRefund` thành công mà không gọi `RefundSubOrderPaymentAsync`.

---

## 3. Success Criteria

1. `WalletClient.PayOrderAsync` tuân thủ 100% route `POST /api/v1/internal/wallet/orders/{orderId}/payment` với `Authorization: Bearer <token>` và body `{ "amount": amount }`.
2. Trả về đối tượng `WalletPaymentResult` có đầy đủ thông tin: `IsSuccess`, `TransactionId`, `ErrorCode`, `ErrorMessage`, `IsTransientFailure`.
3. Kiểm thử tự động bao phủ 100% các kịch bản thành công, lỗi dứt khoát (`404`, `409`), và lỗi chưa xác định (`Timeout`, `5xx`).
4. Toàn bộ test suite pass 100% và `dotnet build Commerce.sln -c Release` thành công 0 lỗi.
