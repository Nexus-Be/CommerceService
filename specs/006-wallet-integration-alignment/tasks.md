# Tasks: Wallet Integration Alignment with Handoff Contract

**Input**: Design documents from `/specs/006-wallet-integration-alignment/` (`spec.md`, `plan.md`)  
**Branch**: `006-wallet-integration-alignment`  
**Status**: Completed  

---

## Task List

### Phase 1: Models & Interface (Shared Contracts)

- [x] **TASK-01**: Tạo file `Commerce.Application/Interfaces/WalletPaymentResult.cs` định nghĩa kết quả thanh toán có cấu trúc (`IsSuccess`, `TransactionId`, `ErrorCode`, `ErrorMessage`, `IsTransientFailure`, `BalanceAfter`) và static helpers (`Success`, `Failure`, `Transient`).
- [x] **TASK-02**: Cập nhật `Commerce.Application/Interfaces/IWalletClient.cs` bổ sung `PayOrderAsync(Guid orderId, decimal amount, string? bearerToken = null, CancellationToken cancellationToken = default)`.

### Phase 2: Client Implementation (`WalletClient.cs`)

- [x] **TASK-03**: Cập nhật `Commerce.Infrastructure/Services/Wallet/WalletClient.cs`:
  - Hiện thực `PayOrderAsync`: gọi `POST /api/v1/internal/wallet/orders/{orderId}/payment`.
  - Body chỉ chứa `{ "amount": amount }`.
  - Bổ sung header `Authorization: Bearer <token>` thông qua `IHttpContextAccessor`.
  - Parse response: `200 OK` deserialize transaction DTO và lấy trường `id`.
  - Bắt các lỗi `400`, `401`, `404 WALLET_NOT_FOUND`, `409 INSUFFICIENT_BALANCE`, `409 ORDER_ALREADY_PAID`.
  - Bắt lỗi Timeout / 5xx, đánh dấu `IsTransientFailure = true`.
- [x] **TASK-04**: Kiểm tra và đăng ký `IHttpContextAccessor` trong `Commerce.Infrastructure/DependencyInjection.cs`.

### Phase 3: Application Logic Alignment (`OrderService.cs`)

- [x] **TASK-05**: Điều chỉnh `CreateOrderAsync` trong `Commerce.Application/Services/Order/OrderService.cs`:
  - Chuyển sang gọi `_walletClient.PayOrderAsync`.
  - Khi thành công: Lưu `TransactionId` (kiểm tra `Guid.TryParse` an toàn), đổi `order.Status = OrderStatus.Paid`, commit kho tại Fulfillment.
  - Khi lỗi Transient (Timeout/5xx): Giữ nguyên `PendingPayment`, giữ nguyên reservation tại Fulfillment để đối soát, không báo lỗi dứt khoát cho khách.
  - Khi lỗi nghiệp vụ dứt khoát (`INSUFFICIENT_BALANCE`, `WALLET_NOT_FOUND`): Giữ `PendingPayment`, ghi log và trả phản hồi phù hợp.
- [x] **TASK-06**: Điều chỉnh `SellerRejectSubOrderAsync` trong `OrderService.cs`:
  - Loại bỏ lệnh gọi giả định `_walletClient.RefundSubOrderPaymentAsync`.
  - Lưu bản ghi `OrderRefund` trạng thái `Requested` với ghi chú đối soát thủ công.

### Phase 4: Automated Tests & Verification

- [x] **TASK-07**: Cập nhật mock `IWalletClient` trong `tests/Commerce.UnitTests/Application/OrderCheckoutTests.cs` để kiểm thử phương thức `PayOrderAsync` (Happy path, Insufficient balance, Timeout, Idempotency).
- [x] **TASK-08**: Cập nhật mock trong `tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs` và `OrderServiceTests.cs`.
- [x] **TASK-09**: Chạy `dotnet test` kiểm chứng 100% test cases pass (49/49 tests passed).
- [x] **TASK-10**: Chạy `dotnet build Commerce.sln -c Release` đạt 0 lỗi 0 cảnh báo.
