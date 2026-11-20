# Feature Specification: Frontend Alignment, Notification Outbox & Cross-Service Integration Resilience

**Feature Branch**: `029-fe-alignment-and-resilience-integration`  
**Created**: 2026-11-17  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  
**Input**: Đồng bộ hóa toàn diện các cải tiến và tính năng tích hợp thực tế từ bản Frontend/Cross-service alignment vào CommerceService: Notification Outbox, Auction Callback & Financial Retry Workers, Review Eligibility API, Shipment Creation Payload Factory, Delivery Status Transition Guards, và Voucher Stacking Engine.

---

## 1. Executive Summary & Value Proposition

Trong quá trình hoàn thiện giao diện người dùng (Frontend) và tích hợp thực tế giữa CommerceService với các microservices đối tác (`UserService:8001`, `AuctionService:8005`, `FulfillmentService:8006`, `NotificationService:8007`, `WalletService:8004`), nhóm phát triển đã phát hiện và hoàn thiện nhiều cơ chế vận hành sống còn mà nhánh chính (`dev`) chưa tích hợp:

1. **Thông báo đơn hàng qua Transactional Outbox Pattern**:
   - Khi đơn hàng hoàn tất thanh toán, hệ thống phải phát thông báo cho cả Người mua và các Người bán liên quan.
   - Sử dụng **Transactional Outbox** (`NotificationOutboxItem` và `NotificationOutboxWorker`) để đảm bảo không bị thất thoát sự kiện kể cả khi tiến trình bị sập ngay sau khi commit thanh toán vào database.
   - Tự động phân giải `ShopId` từ `sellerUserId` qua `IUserClient.GetSellerShopAsync`.
2. **Khả năng tự phục hồi và tái thử giao tiếp Đấu giá (Auction Resilience & Retries)**:
   - Chuẩn hóa payload sự kiện thanh toán đấu giá qua `AuctionPaymentCallbackFactory` để đảm bảo tính bất biến (idempotency) khi gửi lại cho AuctionService.
   - Thêm `AuctionCallbackRetryWorker` tự động quét gửi lại các callback bị lỗi mạng.
   - Thêm `AuctionFinancialRetryWorker` tự động retry hoàn trả tiền cọc hoặc giải ngân bị nghẽn qua `WalletService`.
   - Cung cấp API nội bộ `GET /api/v1/internal/auction-orders/{orderId}/notification-snapshot` cho AuctionService truy vấn chi tiết đơn để bắn thông báo.
3. **API Xác thực quyền Viết đánh giá cho UserService (`GET /api/v1/internal/review-eligibility`)**:
   - Cung cấp endpoint nội bộ kiểm tra hợp lệ trước khi cho phép người mua viết nhận xét/đánh giá sản phẩm/người bán, áp dụng cho cả SubOrder thông thường và AuctionOrder.
4. **Chuẩn hóa Payload Vận chuyển GHN & Chống thụt lùi trạng thái**:
   - Cung cấp `ShipmentRequestFactory` tạo đủ 100% dữ liệu kiện hàng gửi sang FulfillmentService (tên, số điện thoại, địa chỉ xã/huyện/tỉnh, khối lượng gram, danh sách SKU).
   - Bổ sung `DeliveryStatusTransitions` ngăn chặn webhook GHN đến muộn/bị replay làm đảo ngược trạng thái đơn hàng (ví dụ: đã `Delivered` không được quay lại `InTransit`).
5. **Chuẩn hóa Bảo mật Nội bộ & Miễn trừ Kiểm tra Role (`[AllowAnonymous]`)**:
   - Bổ sung `InternalServiceKeyGuard` sử dụng phép so sánh an toàn thời gian cố định (`CryptographicOperations.FixedTimeEquals`) kiểm tra `X-Internal-Service-Key`.
   - Miễn trừ role check trên `RequireRoleAttribute` cho các endpoint service-to-service được đánh dấu `[AllowAnonymous]`.
6. **Tối ưu hóa Mô hình Khuyến mãi đa tầng (Shopee 2-Tier Voucher)**:
   - Mở rộng `TierSavingsDto` chứa `ProductDiscount`.
   - Phân tách độc lập giữa voucher cấp Shop (1 voucher/shop) và voucher cấp Product (1 voucher/SKU) cho phép áp dụng đồng thời trong cùng giỏ hàng.
7. **Quyền Quản trị viên/Nhân viên cho Đơn đấu giá**:
   - Cho phép tài khoản `Admin` và `Staff` xem chi tiết đơn đấu giá phục vụ hỗ trợ khách hàng và vận hành.

---

## 2. User Scenarios & Acceptance Criteria

### User Story 1 - Thông báo đơn hàng thanh toán thành công (Priority: P1)
Khi Người mua thanh toán thành công đơn hàng (Ví hoặc Cổng thanh toán), hệ thống phải bảo đảm gửi thông báo tới Người mua và từng Người bán có sản phẩm trong đơn, nhắc nhở Shop xác nhận và chuẩn bị hàng trong vòng 24 giờ.

- **Independent Test**: Thanh toán một đơn hàng có 2 shop khác nhau. Kiểm tra bảng `NotificationOutbox` có 2 bản ghi trạng thái `AwaitingData`. Khi worker chạy, bản ghi chuyển sang `Sent` và gọi đúng endpoint của NotificationService với `sourceEventId` duy nhất.
- **Acceptance Scenarios**:
  1. **Given** một đơn hàng ở trạng thái `PendingPayment`, **When** thanh toán thành công, **Then** `SubOrder.Status = Paid`, `SubOrder.SellerRespondDeadlineAt = PaidAt + 24h`, và bản ghi `NotificationOutboxItem` được lưu cùng transaction.
  2. **Given** worker `NotificationOutboxWorker`, **When** xử lý bản ghi `AwaitingData`, **Then** gọi `IUserClient.GetSellerShopAsync(sellerUserId)` để lấy `ShopId`, đóng băng `PayloadJson` và chuyển trạng thái sang `Pending` $\rightarrow$ `Sent`.
  3. **Given** NotificationService gặp sự cố tạm thời (5xx/Timeout), **When** worker gửi thất bại, **Then** tăng `AttemptCount`, áp dụng exponential backoff với `NextRetryAt`, giữ nguyên `PayloadJson` và không làm crash hệ thống.

---

### User Story 2 - Tự động đồng bộ và thử lại Callback Đấu giá (Priority: P1)
Khi đơn đấu giá hoàn tất thanh toán hoặc bị hủy do quá hạn 24h, CommerceService phải thông báo kết quả cho AuctionService. Nếu gặp sự cố mạng, hệ thống phải tự động thử lại mà không làm mất tính toàn vẹn dữ liệu.

- **Independent Test**: Mô phỏng AuctionService trả về 503 khi đơn đấu giá thanh toán. Kiểm tra dòng `OrderStatusHistory` có `AuctionCallbackStatus = 'Paid'` và `AuctionCallbackSentAt == null`. Sau đó khởi động worker và bật lại AuctionService, kiểm tra callback được gửi thành công và `AuctionCallbackSentAt` được cập nhật.
- **Acceptance Scenarios**:
  1. **Given** một đơn đấu giá được thanh toán, **When** gọi `NotifyPaymentStatusAsync`, **Then** payload được sinh từ `AuctionPaymentCallbackFactory` chứa đúng `OrderNo`, `TotalAmount`, `SellerRespondDeadlineAt`, và `OrderSource` dạng wire string (`"WINNER"` hoặc `"SECOND_CHANCE"`).
  2. **Given** cuộc gọi ban đầu thất bại, **When** `AuctionCallbackRetryWorker` chạy định kỳ 30s, **Then** quét tối đa 50 bản ghi `OrderStatusHistory` chưa gửi để gửi lại với cùng `EventId` và nội dung.
  3. **Given** AuctionService cần lấy thông tin đơn để hiển thị, **When** gửi request `GET /api/v1/internal/auction-orders/{orderId}/notification-snapshot` kèm `X-Internal-Service-Key`, **Then** CommerceService trả về `200 OK` với `AuctionOrderNotificationSnapshot`.

---

### User Story 3 - Kiểm tra điều kiện đánh giá từ UserService (Priority: P2)
Khi người dùng chuẩn bị gửi đánh giá sao và nhận xét cho một đơn hàng hoặc đấu giá, UserService gọi sang CommerceService để xác thực tính hợp lệ.

- **Independent Test**: Gửi request `GET /api/v1/internal/review-eligibility?referenceType=Order&referenceId={subOrderId}` với header `X-Internal-Service-Key`.
- **Acceptance Scenarios**:
  1. **Given** một SubOrder tồn tại, **When** UserService gọi API kiểm tra quyền đánh giá, **Then** trả về `200 OK` chứa `BuyerId`, `SellerId`, `Status`, và `CompletedAt`.
  2. **Given** một AuctionOrder tồn tại, **When** UserService gọi API với `referenceType=Auction`, **Then** trả về thông tin tương ứng của đơn đấu giá.
  3. **Given** `referenceId` không tồn tại, **When** gọi API, **Then** trả về HTTP 404 với error code `REVIEW_ORDER_NOT_FOUND`.
  4. **Given** `X-Internal-Service-Key` bị thiếu hoặc sai, **When** gọi API, **Then** trả về HTTP 401 `INTERNAL_KEY_INVALID`.

---

### User Story 4 - Đăng ký vận đơn GHN đầy đủ dữ liệu & Bảo vệ trạng thái giao hàng (Priority: P2)
Khi Shop xác nhận đơn hàng, CommerceService gửi yêu cầu tạo vận đơn sang FulfillmentService với đầy đủ chi tiết kiện hàng. Khi nhận webhook từ bưu tá, hệ thống chặn việc lùi trạng thái đơn hàng.

- **Independent Test**: Kiểm tra `ShipmentRequestFactory.ForSubOrder` và `ForAuctionOrder` tạo đủ thông tin địa chỉ 3 cấp và khối lượng hàng. Gửi webhook cập nhật trạng thái `InTransit` khi đơn đã `Delivered` và kiểm tra webhook bị bỏ qua mà không báo lỗi.
- **Acceptance Scenarios**:
  1. **Given** một SubOrder hoặc AuctionOrder, **When** tạo vận đơn, **Then** `ShipmentCreationRequest` chứa đầy đủ `RecipientName`, `RecipientPhone`, `ShippingAddress`, `ToProvinceName`, `ToDistrictId`, `ToWardCode`, `WeightInGrams`, và danh sách items.
  2. **Given** SubOrder đang ở trạng thái `Delivered` hoặc `Completed`, **When** nhận webhook giao hàng với trạng thái `InTransit` hoặc `Picking`, **Then** `DeliveryStatusTransitions.IsAllowed` trả về `false`, hệ thống ghi log và giữ nguyên trạng thái hiện tại.

---

### User Story 5 - Khuyến mãi đa tầng và Quyền quản trị (Priority: P3)
Người mua có thể áp dụng 1 voucher của Shop kèm các voucher sản phẩm khác nhau. Admin/Staff có thể tra cứu đơn đấu giá để hỗ trợ khách hàng.

- **Acceptance Scenarios**:
  1. **Given** giỏ hàng có sản phẩm từ Shop A, **When** áp dụng 1 Shop Voucher của Shop A và 1 Product Voucher cho SKU 1 của Shop A, **Then** hệ thống chấp nhận và tính tổng tiết kiệm trong `TierSavingsDto.ProductDiscount` và `ShopDiscount`.
  2. **Given** người dùng có role `Admin` hoặc `Staff`, **When** gọi `GET /api/v1/auction-orders/{id}`, **Then** hệ thống trả về chi tiết đơn hàng mà không báo lỗi 403 Forbidden.

---

## 3. Requirements

### Functional Requirements

- **FR-001**: Hệ thống BẮT BUỘC lưu trữ sự kiện thông báo thanh toán đơn hàng vào bảng `NotificationOutbox` trong cùng transaction với cập nhật thanh toán đơn hàng.
- **FR-002**: Hệ thống BẮT BUỘC cung cấp background worker `NotificationOutboxWorker` xử lý gửi thông báo ra NotificationService với cơ chế khóa nguyên tử và retry lũy thừa.
- **FR-003**: Hệ thống BẮT BUỘC tích hợp phương thức `IUserClient.GetSellerShopAsync(Guid sellerUserId)` để phân giải `ShopId` từ `sellerUserId`.
- **FR-004**: Hệ thống BẮT BUỘC lưu trữ trạng thái callback đấu giá trong `OrderStatusHistory` và retry tự động qua `AuctionCallbackRetryWorker`.
- **FR-005**: Hệ thống BẮT BUỘC cung cấp `AuctionFinancialRetryWorker` retry các khoản hoàn cọc và giải ngân đấu giá bị treo qua WalletService.
- **FR-006**: Hệ thống BẮT BUỘC cung cấp endpoint `GET /api/v1/internal/auction-orders/{orderId}/notification-snapshot` phục vụ AuctionService.
- **FR-007**: Hệ thống BẮT BUỘC cung cấp endpoint `GET /api/v1/internal/review-eligibility` phục vụ UserService kiểm tra quyền đánh giá.
- **FR-008**: Hệ thống BẮT BUỘC sử dụng `ShipmentRequestFactory` đóng gói đầy đủ payload kiện hàng khi gọi `IFulfillmentClient.CreateShipmentAsync`.
- **FR-009**: Hệ thống BẮT BUỘC sử dụng `DeliveryStatusTransitions` để bảo vệ đơn hàng khỏi việc bị lùi trạng thái do webhook đến chậm hoặc bị lặp lại.
- **FR-010**: Hệ thống BẮT BUỘC miễn trừ kiểm tra JWT Role trên `RequireRoleAttribute` cho các endpoint được đánh dấu `[AllowAnonymous]` và dùng `InternalServiceKeyGuard` kiểm tra `X-Internal-Service-Key`.
- **FR-011**: Hệ thống BẮT BUỘC cập nhật `TierSavingsDto` bao gồm trường `ProductDiscount` và hỗ trợ tách biệt voucher cấp Shop và voucher cấp Product.
- **FR-012**: Hệ thống BẮT BUỘC cho phép `Admin` và `Staff` xem chi tiết đơn đấu giá bất kỳ.

---

## 4. Edge Cases & Resilience

- **Notification Service tạm thời ngừng hoạt động**: Outbox worker ghi nhận `LastError`, tăng `AttemptCount`, lùi `NextRetryAt`. Sau 24h (`GiveUpAt`), chuyển sang `NeedsAttention` để vận hành kiểm tra thủ công.
- **Lỗi mạng khi gọi User Service lấy `ShopId`**: Bản ghi outbox giữ nguyên ở trạng thái `AwaitingData`, worker sẽ thử lại ở chu kỳ tiếp theo mà không khóa các bản ghi khác.
- **Webhook giao hàng đến trễ hoặc ngược thứ tự**: `DeliveryStatusTransitions` từ chối các bước chuyển không hợp lệ (ví dụ: `Delivered` $\rightarrow$ `InTransit`), ghi nhận cảnh báo và phản hồi HTTP 200 cho bên vận chuyển để tránh retry không cần thiết.
- **Thiếu cấu hình `INTERNAL_SERVICE_SECRET`**: `InternalServiceKeyGuard` từ chối mọi request với HTTP 503 `INTERNAL_CONFIGURATION_ERROR`, không dùng fallback hardcode trong code.

---

## 5. Implementation Phasing

| Giai đoạn | Nội dung công việc | Kiểm chứng |
| :--- | :--- | :--- |
| **Pha 1: Core Domain & Data Model** | Thêm entity `NotificationOutboxItem`, enum `NotificationOutboxStatus`, cập nhật các entity `SubOrder`, `AuctionOrder`, `OrderStatusHistory`, và tạo Migration. | `dotnet build` & kiểm tra snapshot model EF Core. |
| **Pha 2: Application Contracts & Helpers** | Thêm `ShipmentRequestFactory`, `DeliveryStatusTransitions`, `ReviewEligibility`, `AuctionPaymentCallbackFactory`, cập nhật `TierSavingsDto` & `VoucherCalculationEngine`. | Unit tests: `DeliveryStatusTransitionsTests`, `ShipmentCreationContractTests`. |
| **Pha 3: Infrastructure Clients** | Cập nhật `UserClient.GetSellerShopAsync`, `FulfillmentClient.CreateShipmentAsync`, `WalletClient.GetAuctionDepositHoldAsync`. | Unit tests: `UserPenaltyContractTests`, `ShippingQuoteContractTests`. |
| **Pha 4: API, Security & Background Workers** | Thêm `InternalServiceKeyGuard`, cập nhật `RequireRoleAttribute`, thêm các API internal và đăng ký 3 Hosted Services trong `Program.cs`. | Unit tests: `RequireRoleAllowAnonymousTests`, `ReviewEligibilityTests`. |
| **Pha 5: Outbox & Notification Flow** | Tích hợp enqueue tại `OrderService` và hoàn thiện `NotificationOutboxWorker`. | Unit tests: `NotificationOutboxWorkerTests`, `OrderPaidNotificationTests`. |
| **Pha 6: Verification & End-to-End** | Chạy toàn bộ test suite kiểm tra cả tính năng mới và các specs 026-028. | `dotnet test` đạt 100% pass, 0 warning/error. |
