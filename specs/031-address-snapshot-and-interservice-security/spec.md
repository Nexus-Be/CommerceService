# Feature Specification: Address Snapshot Parity, Checkout Address Integration & S2S Internal Security

**Feature Branch**: `031-address-snapshot-and-interservice-security`  
**Created**: 2026-11-19  
**Status**: Draft  
**Scope**: Inward-Only (CommerceService Port 8003, Database `commerce_db:5435`)  
**Input**: Triển khai gói tính năng SPEC-031 gồm tích hợp sổ địa chỉ từ UserService, tự động fallback địa chỉ mặc định, API tính lại cước ship GHN khi đổi địa chỉ trên Checkout, API nội bộ xác minh đơn hàng khiếu nại (Dispute/Report) cho UserService, và đồng bộ hóa Worker kiên cố.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Tích hợp Client lấy địa chỉ Buyer (`AddressId` & Default Address) từ UserService (Priority: P1) 🎯 MVP

Khi người mua tiến hành thanh toán hoặc gửi địa chỉ nhận hàng đấu giá, CommerceService cần lấy thông tin địa chỉ chi tiết (kèm mã định danh quận/huyện `GhnDistrictId` và phường/xã `GhnWardCode` của GHN) trực tiếp từ `UserService` thông qua API nội bộ.

- **Why this priority**: Là nền tảng để loại bỏ hoàn toàn việc bắt khách hàng phải tự gõ lại địa chỉ hoặc nhập tay mã GHN, đảm bảo tính nhất quán dữ liệu địa chỉ trên toàn hệ sinh thái.
- **Independent Test**: Gọi `_userClient.GetBuyerAddressAsync(buyerId, addressId)` $\rightarrow$ Trả về `BuyerAddressDto` với đầy đủ thông tin địa chỉ cấu trúc và mã GHN. Khi `UserService` trả HTTP 404 $\rightarrow$ client trả về `null` an toàn mà không ném exception làm gián đoạn luồng.
- **Acceptance Scenarios**:
  1. **Given** `buyerId` và `addressId` hợp lệ tồn tại trong UserService, **When** gọi `GetBuyerAddressAsync`, **Then** trả về `BuyerAddressDto` chứa `RecipientName`, `Phone`, `Street`, `Ward`, `District`, `Province`, `GhnDistrictId`, `GhnWardCode`, `FullAddressText`, `IsDefault`.
  2. **Given** `addressId` không tồn tại hoặc không thuộc `buyerId`, **When** gọi `GetBuyerAddressAsync`, **Then** trả về `null`.
  3. **Given** `buyerId` có địa chỉ mặc định, **When** gọi `GetBuyerDefaultAddressAsync`, **Then** trả về địa chỉ có `IsDefault == true`.

---

### User Story 2 - Hỗ trợ `AddressId` & Auto-fallback Default Address tại Báo giá Checkout và Đơn Đấu giá (Priority: P1)

Khi khách hàng gọi API báo giá đặt hàng (`POST /api/v1/checkouts/quote` hoặc `/api/v1/orders/quote`) hoặc gửi địa chỉ nhận hàng đấu giá (`PUT /api/v1/auction-orders/{id}/shipping`), hệ thống cho phép chỉ cần gửi `AddressId` (hoặc để trống cả hai để tự động lấy địa chỉ mặc định của người mua từ `UserService`).

- **Why this priority**: Cải thiện tối đa trải nghiệm người dùng, giảm thiểu tối đa sai sót cước vận chuyển do nhập tay địa chỉ tự do.
- **Independent Test**: Người mua có địa chỉ mặc định gọi `POST /api/v1/orders/quote` với `shippingAddress: null` và `addressId: null` $\rightarrow$ Hệ thống tự động phân giải địa chỉ mặc định, tính cước ship GHN chính xác và trả về báo giá.
- **Acceptance Scenarios**:
  1. **Given** request truyền `addressId`, **When** hệ thống phân giải địa chỉ, **Then** nạp địa chỉ qua `_userClient.GetBuyerAddressAsync`; nếu không tìm thấy, ném lỗi HTTP 404 `ADDRESS_NOT_FOUND`.
  2. **Given** request không truyền `addressId` và `shippingAddress == null`, **When** hệ thống phân giải địa chỉ, **Then** tự động nạp qua `_userClient.GetBuyerDefaultAddressAsync`; nếu người mua chưa có địa chỉ mặc định, ném lỗi HTTP 400 `ADDRESS_REQUIRED`.
  3. **Given** request truyền trực tiếp `shippingAddress`, **When** validate, **Then** bắt buộc kiểm tra `GhnDistrictId > 0` và `!string.IsNullOrWhiteSpace(GhnWardCode)`; nếu không thỏa mãn, ném lỗi HTTP 400 `VAL_INVALID_INPUT`.
  4. **Given** người mua thắng đấu giá gọi `PUT /api/v1/auction-orders/{id}/shipping`, **When** truyền `addressId` hoặc để trống địa chỉ, **Then** quy trình phân giải áp dụng tương tự và chốt `TotalAmount` chuẩn xác.

---

### User Story 3 - API Re-quote phí ship GHN khi đổi địa chỉ trên Checkout (Priority: P1)

Khi khách hàng đang ở màn hình Checkout và thay đổi địa chỉ giao hàng (hoặc chọn địa chỉ khác trong sổ địa chỉ), hệ thống cung cấp API cho phép tính lại phí vận chuyển GHN cho toàn bộ các Shop trong đơn hàng mà không làm mất giỏ hàng hay voucher đã chọn trước đó.

- **Why this priority**: Cho phép Frontend cập nhật lại tổng thanh toán và phí ship động ngay khi khách hàng đổi địa chỉ nhận hàng.
- **Independent Test**: Có một `cachedQuote` đã tạo. Gọi `POST /api/v1/checkouts/quote/{quoteId}/recalculate` với địa chỉ mới $\rightarrow$ Hệ thống tính lại cước ship GHN của từng shop con, cập nhật cache báo giá và trả về `CheckoutQuoteResponse` mới.
- **Acceptance Scenarios**:
  1. **Given** `quoteId` hợp lệ trong cache, **When** gửi địa chỉ mới (`AddressId` hoặc `ShippingAddressInput`), **Then** tính lại cước GHN từng shop, cập nhật `CachedQuote` và trả về HTTP 200 `CheckoutQuoteResponse`.
  2. **Given** `quoteId` không tồn tại hoặc đã hết hạn trong cache, **When** gọi API recalculate, **Then** ném lỗi HTTP 404 `QUOTE_NOT_FOUND`.
  3. **Given** địa chỉ mới khiến GHN không hỗ trợ tuyến giao hàng, **When** tính cước, **Then** ném lỗi HTTP 422 `GHN_ROUTE_UNSUPPORTED` hoặc HTTP 400 `VAL_INVALID_INPUT`.

---

### User Story 4 - Internal API xác minh đơn hàng phục vụ Khiếu nại/Tố cáo cho UserService (Priority: P1)

Khi người dùng gửi khiếu nại hoặc tố cáo đối tác trên `UserService` (`DisputeService` / `ReportService`), `UserService` cần xác minh xem hai bên có thực sự phát sinh giao dịch đơn hàng ở trạng thái đã giao thành công (`Delivered`) hoặc hoàn tất (`Completed`) hay không thông qua API nội bộ bảo mật.

- **Why this priority**: Giải quyết điểm nghẽn S2S hiện tại khiến `UserService` bị chặn HTTP 401/403 do gọi nhầm vào Public API.
- **Independent Test**: Gọi `GET /api/v1/internal/orders/dispute-eligibility?orderOrSubOrderId={id}&reporterId={rId}&reportedUserId={uId}` kèm header `X-Internal-Service-Key` $\rightarrow$ Trả về HTTP 200 kèm `DisputeEligibilityDto(IsEligible: true, ...)` nếu đơn đã giao và đúng 2 đối tượng giao dịch.
- **Acceptance Scenarios**:
  1. **Given** yêu cầu không có header `X-Internal-Service-Key` hoặc key không đúng, **When** gọi API, **Then** trả về HTTP 401 `INTERNAL_KEY_INVALID`.
  2. **Given** đơn hàng (SubOrder, Order, hoặc AuctionOrder) tồn tại, có trạng thái `Delivered` hoặc `Completed`, và cặp `(reporterId, reportedUserId)` trùng khớp với `(BuyerId, SellerId)` hoặc `(SellerId, BuyerId)`, **When** gọi API, **Then** trả về `200 OK` với `isEligible: true`.
  3. **Given** đơn hàng ở trạng thái chưa giao (ví dụ `PendingPayment`, `Paid`, `Shipping`) hoặc người dùng không liên quan đến đơn, **When** gọi API, **Then** trả về `200 OK` với `isEligible: false`.
  4. **Given** `orderOrSubOrderId` không tồn tại trên toàn bộ hệ thống, **When** gọi API, **Then** trả về HTTP 404 `ORDER_NOT_FOUND`.

---

### User Story 5 - Đồng bộ `AuctionCallbackRetryWorker` & Dọn dẹp Worker `PaymentTimeoutWorker` (Priority: P2)

Đảm bảo các Background Worker xử lý kiên cố (resilience) hoạt động chuẩn xác, không bị lệch payload callback giữa lần gửi đầu và lần retry, và không spam vi phạm phạt seller khi hết hạn phản hồi đơn hàng.

- **Why this priority**: Đảm bảo tính toàn vẹn dữ liệu giao tiếp liên dịch vụ với `AuctionService` và `UserService`.
- **Independent Test**: Kích hoạt `AuctionCallbackRetryWorker` quét lịch sử đơn đấu giá cần retry $\rightarrow$ Payload sinh ra từ `AuctionPaymentCallbackFactory.Build(order, history)` chứa đủ 9 trường thông tin khớp 100% với lần gửi đầu tiên.
- **Acceptance Scenarios**:
  1. **Given** một bản ghi callback đơn đấu giá bị thất bại ở lần gửi đầu, **When** `AuctionCallbackRetryWorker` gửi lại, **Then** sử dụng `AuctionPaymentCallbackFactory.Build` để gửi đúng `OrderNo`, `TotalAmount`, `SellerRespondDeadlineAt`, `OrderSource`.
  2. **Given** đơn hàng bị quá hạn phản hồi của người bán (`SellerExpired`), **When** `PaymentTimeoutWorker` xử lý, **Then** chỉ gọi `_userClient.CreatePenaltyAsync` duy nhất 1 lần với lý do `UserPenaltyViolationTypes.SellerResponseTimeout`.

---

## Edge Cases

- **UserService bị ngắt kết nối mạng tạm thời (Timeout/503)**:
  - Khi phân giải địa chỉ mặc định hoặc theo ID, `UserClient` áp dụng Polly retry (3 lần, exponential backoff). Nếu vẫn lỗi, ném lỗi rõ ràng HTTP 503 `USER_SERVICE_UNAVAILABLE`.
- **Địa chỉ trả về từ UserService thiếu mã vùng GHN**:
  - Nếu `GhnDistrictId <= 0` hoặc `GhnWardCode` rỗng từ phía UserService, CommerceService từ chối tạo báo giá và trả lỗi HTTP 400 `GHN_ADDRESS_INCOMPLETE` yêu cầu khách cập nhật lại địa chỉ chuẩn GHN trên trang cá nhân.
- **Đổi địa chỉ khi đơn đã tạo vận đơn GHN**:
  - Chặn tuyệt đối nếu `!string.IsNullOrWhiteSpace(subOrder.TrackingCode)` với mã lỗi HTTP 400 `CANNOT_CHANGE_ADDRESS_AFTER_SHIPPING`.

---

## Requirements & Non-Functional Constraints

1. **Port & Persistence**: Cấu hình bất biến Port `8003`, Database `commerce_db:5435`.
2. **Clean Architecture & No-Repository**:
   - Sử dụng trực tiếp `ICommerceDbContext` / `AppDbContext`. Cấm tạo Repository pattern.
3. **Internal API Security**:
   - Mọi internal endpoint bắt buộc gắn `[AllowAnonymous]` và kiểm tra qua `InternalServiceKeyGuard.Check(Request, configuration)`.
4. **Error Protocol**:
   - Error code chuẩn `SCREAMING_SNAKE_CASE` (ví dụ: `ADDRESS_NOT_FOUND`, `ADDRESS_REQUIRED`, `QUOTE_NOT_FOUND`, `GHN_ADDRESS_INCOMPLETE`, `ORDER_NOT_FOUND`).
   - Message 100% tiếng Anh viết theo chuẩn Sentence case.
5. **Test Verification**:
   - Toàn bộ test suite `dotnet test` phải đạt trạng thái Passed 100% (0 failed).
