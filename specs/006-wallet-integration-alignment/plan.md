# Implementation Plan: Wallet Integration Alignment with Handoff Contract

**Branch**: `006-wallet-integration-alignment` | **Date**: 2026-11-02 | **Spec**: [specs/006-wallet-integration-alignment/spec.md](spec.md)

---

## 1. Summary

Điều chỉnh toàn bộ tầng kết nối ngoại vi (`Commerce.Infrastructure/Services/Wallet/WalletClient.cs`) và tầng nghiệp vụ (`Commerce.Application/Services/Order/OrderService.cs`) theo đúng thỏa thuận tại **Tài liệu bàn giao tích hợp Commerce ↔ WalletService**:
1. Đổi route thanh toán sang `POST /api/v1/internal/wallet/orders/{orderId}/payment`.
2. Truyền `{ "amount": totalAmount }` trong body và forward `Authorization: Bearer <buyer JWT>`.
3. Nhận về đối tượng `WalletPaymentResult` có Transaction ID chuỗi và mã lỗi RFC 9110 chi tiết.
4. Xử lý an toàn Transaction ID dạng chuỗi (chống crash nếu không phải UUID).
5. Phân định rõ ràng giữa lỗi dứt khoát (`404 WALLET_NOT_FOUND`, `409 INSUFFICIENT_BALANCE`) và lỗi chưa xác định (`Timeout / 5xx`).
6. Loại bỏ các lệnh gọi giả định sang các endpoint không tồn tại của Wallet (`/refund`, `/balance`).

---

## 2. Technical Context

- **Ngôn ngữ & Nền tảng:** C# 12, .NET 8.0, ASP.NET Core Web API
- **Kiến trúc:** Clean Architecture 4 tầng, No-Repository over EF Core
- **Database:** PostgreSQL 16 (`commerce_db:5435`)
- **External Client:** `HttpClient` kết nối `WalletService` qua private network (Port `8008`)
- **Authentication:** Chuyển tiếp Bearer JWT từ `HttpContext` (`IHttpContextAccessor`)
- **Testing:** xUnit, Moq, FluentAssertions, EF Core InMemory

---

## 3. Constitution & Architecture Guardrails

- [x] **No-Repository Rule:** Tương tác database trực tiếp qua `ICommerceDbContext` / `AppDbContext`.
- [x] **Inward-Only Rule:** Không tham chiếu đường dẫn ra ngoài repository CommerceService.
- [x] **Enum & Error Rules:** 100% English sentence-case error messages, SCREAMING_SNAKE_CASE error codes, PascalCase Enums.
- [x] **Verification Gate:** `dotnet build Commerce.sln -c Release` thành công 100% (0 error).

---

## 4. Phase Breakdown & Implementation Design

### Phase 1: Models & Interface Refactoring
1. Tạo file `Commerce.Application/Interfaces/WalletPaymentResult.cs`:
   ```csharp
   public sealed record WalletPaymentResult(
       bool IsSuccess,
       string? TransactionId = null,
       string? ErrorCode = null,
       string? ErrorMessage = null,
       bool IsTransientFailure = false,
       decimal? BalanceAfter = null);
   ```
2. Cập nhật `Commerce.Application/Interfaces/IWalletClient.cs`:
   - Thêm phương thức chuẩn:
     ```csharp
     Task<WalletPaymentResult> PayOrderAsync(
         Guid orderId,
         decimal amount,
         string? bearerToken = null,
         CancellationToken cancellationToken = default);
     ```
   - Duy trì `CheckoutDirectPaymentAsync` như wrapper hoặc migration bridge để không phá vỡ caller cũ.
   - Đánh dấu deprecated hoặc loại bỏ các method giả định `RefundSubOrderPaymentAsync`, `RefundOrderPaymentAsync`.

### Phase 2: Infrastructure Implementation (`WalletClient.cs`)
1. Inject `IHttpContextAccessor` vào `WalletClient` để tự động lấy token Bearer từ request hiện tại nếu caller không truyền trực tiếp.
2. Thiết lập request HTTP:
   - Method: `POST`
   - URL: `api/v1/internal/wallet/orders/{orderId}/payment`
   - Header: `Authorization: Bearer <token>`
   - Body: `JsonContent.Create(new { amount })`
3. Parse Response:
   - Status `200 OK`: Deserialize transaction DTO (`id`, `status`, `balanceAfter`), trả về `WalletPaymentResult` thành công.
   - Status `400` / `404` / `409`: Parse ProblemDetails, trích xuất `errorCode` (`WALLET_NOT_FOUND`, `INSUFFICIENT_BALANCE`, `ORDER_ALREADY_PAID`), trả về `IsSuccess = false`, `IsTransientFailure = false`.
   - Timeout / `HttpRequestException` / `5xx`: Bắt lỗi, trả về `IsSuccess = false`, `IsTransientFailure = true`.

### Phase 3: Application Logic Adjustment (`OrderService.cs`)
1. Trong `CreateOrderAsync`:
   - Thay thế việc gọi `CheckoutDirectPaymentAsync` bằng `PayOrderAsync`.
   - Khi `result.IsSuccess == true`:
     - Phân tích `result.TransactionId`: Nếu `Guid.TryParse` thành công thì gán `order.WalletTransactionId = parsedGuid`, gán cho các `SubOrder`. Nếu không phải UUID, ghi log info an toàn.
     - Đổi `order.Status = OrderStatus.Paid`.
     - Gọi `_fulfillmentClient.CommitStockAsync(orderId, ct)`.
   - Khi `result.IsTransientFailure == true` (Timeout / 5xx):
     - Không hủy đơn! Giữ `order.Status = OrderStatus.PendingPayment`.
     - Giữ nguyên reservation tại Fulfillment để đối soát sau.
   - Khi `result.IsSuccess == false` và không phải transient (ví hết tiền / chưa có ví):
     - Giữ `PendingPayment` và ghi log cảnh báo / thông báo người mua.
2. Trong `SellerRejectSubOrderAsync`:
   - Bãi bỏ việc gọi `_walletClient.RefundSubOrderPaymentAsync`.
   - Lưu bản ghi `OrderRefund` với trạng thái `Requested` kèm `ReasonNote` rõ ràng để bộ phận tài chính / CSKH xử lý hoàn tiền thủ công.

### Phase 4: Automated Tests & Verification
1. Cập nhật `OrderCheckoutTests.cs`:
   - Test thanh toán ví thành công qua contract mới.
   - Test khi ví trả `409 INSUFFICIENT_BALANCE`.
   - Test khi ví trả `404 WALLET_NOT_FOUND`.
   - Test khi ví bị Timeout / 5xx (giữ PendingPayment).
2. Cập nhật `OrderLifecycleTests.cs`:
   - Test Shop từ chối đơn hàng đã thanh toán (tạo OrderRefund mà không gọi API refund giả định).
3. Chạy `dotnet test` và `dotnet build Commerce.sln -c Release`.
