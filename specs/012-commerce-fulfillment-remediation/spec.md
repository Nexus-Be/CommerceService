# Feature Specification: Fulfillment Integration Remediation and Architectural Alignment

**Feature Directory**: `specs/012-commerce-fulfillment-remediation`  
**Created**: 2026-11-04  
**Status**: Draft  
**Input**: Khắc phục và đồng bộ toàn diện hợp đồng tích hợp giữa CommerceService (Port 8003) và FulfillmentService (Port 8006), xóa bỏ hoàn toàn cơ chế Fail-Open và mock cước/tracking giả, đồng bộ referenceId giữ/nhả kho, xử lý đầy đủ vòng đời trạng thái vận chuyển và cập nhật Hiến pháp kiến trúc v1.4.0.

---

## Clarifications

### Session 2026-11-04
- Q: How should CommerceService and FulfillmentService coordinate the identifier and payload when releasing inventory for a rejected SubOrder (FR-2.1)? → A: Option A (Giữ phiếu giữ kho theo Master Order Id lúc checkout; khi Shop từ chối SubOrder, gọi `POST /api/v1/internal/inventories/reservations/{orderId}/release?reason=SubOrderRejected` kèm body JSON chứa danh sách mặt hàng cần nhả `{ "items": [ { "skuId", "quantity" } ] }` để Fulfillment giải phóng đúng phần kho của SubOrder mà không hủy phiếu giữ kho của các shop khác trong cùng Master Order).
- Q: How should PaymentTimeoutWorker handle database state and retries if the outbound batch release HTTP call to FulfillmentService fails or times out (FR-2.2)? → A: Option A (Tiến hành cập nhật trạng thái hủy đơn xuống Database bình thường và ghi nhận cờ `InventoryReleased = false` trong bản ghi `OrderCancellation`; cho phép Worker hoặc tiến trình retry quét lại các đơn đã hủy nhưng chưa nhả kho để gửi lại lệnh nhả kho ở chu kỳ kế tiếp mà không chặn tiến trình cập nhật đơn).
- Q: How should SubOrder status and financial refund records be modeled in CommerceService when Fulfillment reports DeliveryFailed or Returned (FR-3.1)? → A: Option A (Chuyển `SubOrder.Status` sang `SubOrderStatus.Cancelled`, cập nhật `CancelledAt`, tạo bản ghi `OrderCancellation` với lý do `DeliveryFailed` hoặc `PackageReturned`, đồng thời tạo bản ghi `OrderRefund` với trạng thái `Requested` nếu đơn con đã thanh toán để phục vụ đối soát hoàn tiền mà không cần sửa đổi schema enum `SubOrderStatus` trong database).

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Chuẩn hóa URI, Cổng Kết Nối & Áp Dụng Fail-Closed cho FulfillmentClient (Priority: P1) 🎯 MVP

Là hệ thống `CommerceService`, tôi cần kết nối đúng cổng chuẩn của `FulfillmentService` (`8006`), gọi chính xác các đường dẫn RESTful theo hợp đồng dịch vụ và áp dụng nguyên tắc Fail-Closed (ném lỗi HTTP 503) khi dịch vụ kho/vận chuyển gặp sự cố thay vì tự động trả về giá trị cước phí hoặc mã vận đơn giả lập.

**Why this priority**: Cốt lõi của độ tin cậy và an toàn vận hành sàn. Việc sinh cước ship cứng 30.000 VNĐ hoặc mã vận đơn GHN giả khi Fulfillment rớt mạng gây sai lệch tài chính và phá vỡ tính toàn vẹn của chuỗi cung ứng.

**Independent Test**:
1. Gọi `CalculateShopShippingFeeAsync` khi Fulfillment trả về lỗi 5xx hoặc mất mạng: xác nhận ném `CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", ..., 503)`, không trả về `30000m`.
2. Gọi `CreateShipmentAsync` khi Fulfillment gặp lỗi: xác nhận ném `CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", ..., 503)`, không sinh chuỗi `GHN-...` hay URL nhãn giả.
3. Kiểm tra BaseUrl trong `DependencyInjection.cs`: xác nhận trỏ về cổng `8006`.

**Acceptance Scenarios**:
1. **Given** người mua yêu cầu báo giá checkout, **When** CommerceService tính cước vận chuyển và FulfillmentService phản hồi thành công từ `/api/v1/fulfillment/shipping/quote`, **Then** hệ thống ghi nhận đúng mức cước tính toán từ Fulfillment.
2. **Given** dịch vụ FulfillmentService gặp sự cố hoặc timeout, **When** CommerceService tính cước, **Then** hệ thống từ chối tính toán và ném `CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "Fulfillment service is temporarily unreachable for shipping fee calculation.", 503)`.
3. **Given** Seller chấp nhận đơn hàng con (`SellerAcceptSubOrderAsync`), **When** CommerceService yêu cầu tạo vận đơn tại `/api/v1/shipments`, **Then** hệ thống nhận mã vận đơn và URL nhãn in thật từ Fulfillment; nếu thất bại, ném 503 để rollback giao dịch xác nhận đơn của Seller.
4. **Given** yêu cầu nhả kho hàng loạt, **When** gọi `BatchReleaseStockAsync`, **Then** hệ thống gửi POST tới `/api/v1/internal/inventories/reservations/batch-release` và không nuốt lỗi qua cờ `_failOpen`.

---

### User Story 2 - Đồng Bộ Định Danh Giữ & Nhả Kho Theo Đơn Hàng (Priority: P1)

Là hệ thống quản lý đơn hàng (`OrderService`) và tiến trình nền (`PaymentTimeoutWorker`), tôi cần đảm bảo định danh (`referenceId`) khi tạo phiếu giữ kho và khi gọi giải phóng kho (cho cả đơn hoặc từng đơn hàng con `SubOrder`) khớp hoàn toàn với nhau để loại bỏ tình trạng giam tồn kho ảo.

**Why this priority**: Tránh thất thoát hàng tồn kho vật lý và lỗi "AlreadyReleased" giả khi Shop từ chối đơn hoặc khi đơn hàng bị hủy do hết hạn thanh toán.

**Independent Test**:
1. Tạo đơn hàng với phiếu giữ kho theo `orderId`. Gọi `SellerRejectSubOrderAsync` cho một shop trong đơn: xác nhận gọi Fulfillment với đúng định danh gốc `orderId` và danh sách món hàng của SubOrder đó.
2. Cho đơn hàng rơi vào trạng thái quá hạn thanh toán (`PaymentTimeoutWorker`): xác nhận Worker chủ động gửi lệnh HTTP POST nhả kho sang Fulfillment với danh sách các `orderId` quá hạn.

**Acceptance Scenarios**:
1. **Given** một đơn hàng gồm nhiều SubOrder có phiếu giữ kho được tạo theo mã Master Order `orderId`, **When** một Seller từ chối đơn con (`SellerRejectSubOrderAsync`), **Then** CommerceService gọi Fulfillment giải phóng từng phần kho theo đúng `orderId` và danh sách mặt hàng cần nhả.
2. **Given** một danh sách các đơn hàng chưa thanh toán và đã quá hạn thanh toán (hết hạn timeout), **When** `PaymentTimeoutWorker` quét và xử lý theo lô, **Then** Worker chủ động gọi HTTP sang Fulfillment `/api/v1/internal/inventories/reservations/batch-release` để nhả toàn bộ lượng hàng tồn trước khi cập nhật trạng thái đơn thành `Cancelled`/`Expired`.

---

### User Story 3 - Tiếp Nhận Đầy Đủ Toàn Bộ Vòng Đời Vận Chuyển Từ Webhook (Priority: P2)

Là hệ thống `CommerceService`, khi nhận webhook thông báo biến động giao hàng từ `FulfillmentService` (`POST /api/v1/sub-orders/{subOrderId}/delivery-status`), tôi cần cập nhật chính xác và đầy đủ toàn bộ 9 trạng thái vận chuyển theo chuẩn enum `ShipmentStatus`.

**Why this priority**: Cập nhật trạng thái giao vận theo thời gian thực cho người mua và người bán, kích hoạt đối soát và xử lý hoàn tiền khi giao hàng thất bại hoặc hoàn hàng.

**Independent Test**: Gửi webhook với các trạng thái `InTransit`, `HandedOver`, `DeliveryFailed`, `Returned`, `Cancelled`: xác nhận trạng thái của `SubOrder` chuyển đổi hợp lệ và kích hoạt các nghiệp vụ hoàn tiền/nhả kho tương ứng.

**Acceptance Scenarios**:
1. **Given** webhook gửi trạng thái chuẩn bị hàng (`ReadyToPick`, `Picking`, `Packed`), **When** nhận request, **Then** hệ thống duy trì trạng thái xác nhận của `SubOrder`, cập nhật timestamp và phản hồi HTTP 200 Idempotent.
2. **Given** webhook gửi trạng thái giao vận (`HandedOver`, `InTransit`), **When** nhận request, **Then** hệ thống chuyển trạng thái `SubOrder` sang `Shipped` / `InTransit` và lưu mốc thời gian giao hàng.
3. **Given** webhook gửi trạng thái giao thành công (`Delivered`), **When** nhận request, **Then** hệ thống chuyển trạng thái `SubOrder` sang `Delivered` và chuẩn bị cho chu kỳ thanh quyết toán ví người bán.
4. **Given** webhook gửi trạng thái thất bại hoặc hoàn hàng (`DeliveryFailed`, `Returned`), **When** nhận request, **Then** hệ thống cập nhật trạng thái đơn hàng con và kích hoạt quy trình hoàn tiền cho người mua (nếu đã thanh toán) cùng giải phóng hàng tồn.
5. **Given** webhook gửi trạng thái không hợp lệ nằm ngoài danh mục, **When** nhận request, **Then** hệ thống từ chối với HTTP 400 `INVALID_DELIVERY_STATUS`.

---

### User Story 4 - Vệ Sinh Kiến Trúc & Cập Nhật Hiến Pháp Spec-Kit v1.4.0 (Priority: P3)

Là đội ngũ phát triển và bảo trì hệ thống, tôi muốn toàn bộ tài liệu kiến trúc (`constitution.md`, `docs/`) phản ánh chính xác 100% mô hình điều phối đồng bộ RESTful đang chạy trên thực tế, bãi bỏ hoàn toàn các điều khoản lỗi thời về MassTransit / RabbitMQ Saga.

**Why this priority**: Duy trì tính minh bạch kiến trúc, đảm bảo các AI Agent và lập trình viên tuân thủ đúng chuẩn giao tiếp hiện hành, ngăn chặn việc tái lập các Anti-pattern hàng đợi và Outbox.

**Independent Test**: Kiểm tra `.specify/memory/constitution.md` có phiên bản `1.4.0` và không còn điều khoản bắt buộc MassTransit Outbox.

**Acceptance Scenarios**:
1. **Given** tài liệu Hiến pháp `constitution.md`, **When** rà soát nguyên tắc kiến trúc, **Then** nguyên tắc II được cập nhật thành "Synchronous Typed HTTP REST Orchestration & Transactional Compensation", bãi bỏ MassTransit Transactional Outbox và CloudEvents Saga.
2. **Given** tài liệu `docs/COMMERCE_EVENTS_AND_ASYNC_SPEC.md`, **When** xem xét trạng thái, **Then** tài liệu được đánh dấu Deprecated rõ ràng kèm đường dẫn thay thế.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Hệ thống BẮT BUỘC cấu hình BaseUrl fallback của `FulfillmentClient` trỏ tới cổng `8006` trong `DependencyInjection.cs`.
- **FR-002**: Hệ thống BẮT BUỘC sử dụng route `/api/v1/fulfillment/shipping/quote` cho việc tính cước vận chuyển và ném ngoại lệ RFC 9110 HTTP 503 `FULFILLMENT_SERVICE_UNAVAILABLE` khi không kết nối được hoặc nhận mã lỗi từ FulfillmentService; TUYỆT ĐỐI CẤM trả về giá trị cước mặc định 30.000 VNĐ.
- **FR-003**: Hệ thống BẮT BUỘC sử dụng route `/api/v1/shipments` cho việc tạo vận đơn và ném ngoại lệ RFC 9110 HTTP 503 `FULFILLMENT_SERVICE_UNAVAILABLE` khi tạo vận đơn thất bại; TUYỆT ĐỐI CẤM tự sinh mã vận đơn `GHN-...` hoặc liên kết nhãn in PDF giả.
- **FR-004**: Hệ thống BẮT BUỘC chuẩn hóa route nhả kho hàng loạt tại `/api/v1/internal/inventories/reservations/batch-release`, loại bỏ cờ `_failOpen` nuốt lỗi để đảm bảo tính minh bạch lỗi vận hành.
- **FR-005**: Hệ thống BẮT BUỘC đồng bộ định danh phiếu giữ kho (`referenceId`) khi tạo (Master `orderId`) và khi nhả kho từng phần (`SellerRejectSubOrderAsync`), gọi `POST /api/v1/internal/inventories/reservations/{orderId}/release?reason=SubOrderRejected` với body JSON chứa danh sách `itemsToRelease` (`skuId`, `quantity`).
- **FR-006**: Tiến trình `PaymentTimeoutWorker` BẮT BUỘC chủ động gọi HTTP POST sang FulfillmentService (`/api/v1/internal/inventories/reservations/batch-release`) để giải phóng kho cho danh sách các đơn hàng quá hạn thanh toán; nếu cuộc gọi HTTP thất bại, Worker vẫn commit hủy đơn xuống Database nhưng gán cờ `InventoryReleased = false` để tái thử thách giải phóng ở chu kỳ quét sau.
- **FR-007**: Hệ thống BẮT BUỘC mở rộng bộ ánh xạ trạng thái trong `OrderService.UpdateSubOrderDeliveryStatusAsync` để tiếp nhận và xử lý đầy đủ 9 trạng thái giao hàng từ FulfillmentService (`ReadyToPick`, `Picking`, `Packed`, `HandedOver`, `InTransit`, `Delivered`, `DeliveryFailed`, `Returned`, `Cancelled`):
  - `ReadyToPick`, `Picking`, `Packed`: Duy trì trạng thái `Confirmed`, cập nhật timestamp, phản hồi HTTP 200 OK Idempotent.
  - `HandedOver`, `InTransit` (và `Shipped`): Cập nhật `SubOrderStatus.Shipped` và `ShippedAt`.
  - `Delivered`: Cập nhật `SubOrderStatus.Delivered` và `DeliveredAt`.
  - `DeliveryFailed`, `Returned`: Cập nhật `SubOrderStatus.Cancelled`, lưu `CancelledAt`, tạo bản ghi `OrderCancellation` kèm lý do tương ứng, và tạo bản ghi `OrderRefund` (trạng thái `Requested`) nếu đơn con đã thanh toán.
  - Từ chối các giá trị không hợp lệ bằng HTTP 400 `INVALID_DELIVERY_STATUS`.
- **FR-008**: Hệ thống BẮT BUỘC nâng cấp Hiến pháp `.specify/memory/constitution.md` lên phiên bản `1.4.0`, chính thức phê chuẩn mô hình Điều phối RESTful Đồng bộ qua Typed HTTP Client và bãi bỏ các yêu cầu liên quan đến MassTransit, RabbitMQ và Saga Outbox.

---

## Edge Cases

- **Lỗi mạng hoặc 5xx khi PaymentTimeoutWorker gọi nhả kho**: Worker ghi nhận cảnh báo, đánh dấu `InventoryReleased = false` trên `OrderCancellation` và hoàn tất cập nhật hủy đơn trong DB, tránh để đơn bị treo vĩnh viễn ở trạng thái `PendingPayment`.
- **Fulfillment trả về 400 Bad Request khi tính cước**: Xảy ra khi địa chỉ giao hàng bị thiếu quận/huyện hoặc mã tỉnh; CommerceService ném `SHIPPING_QUOTE_FAILED` (400) kèm mô tả lỗi chi tiết từ Fulfillment.
- **Tạo vận đơn thất bại do Seller chưa nhập kho hoặc thông tin gói hàng không hợp lệ**: CommerceService rollback giao dịch xác nhận đơn của Seller, không cập nhật trạng thái SubOrder sang `Confirmed` nếu chưa có tracking thật.
- **Nhả kho khi đơn hàng đã được nhả trước đó (Idempotency)**: Fulfillment phản hồi 200 OK hoặc thông báo đã nhả; CommerceService tiếp nhận an toàn và không gây crash tiến trình Worker.
- **Webhook gửi trùng lặp trạng thái vận chuyển**: Hệ thống kiểm tra trạng thái hiện tại của `SubOrder`; nếu trạng thái không thay đổi, phản hồi HTTP 200 OK ngay lập tức (Idempotent).

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 0% trường hợp phát sinh cước vận chuyển giả định 30.000 VNĐ hoặc mã tracking giả `GHN-...` trong cơ sở dữ liệu và giao diện người dùng khi Fulfillment gặp sự cố.
- **SC-002**: 100% các cuộc gọi giải phóng kho khi từ chối đơn con hoặc đơn hết hạn thanh toán sử dụng đúng định danh giữ kho, đạt tỷ lệ nhả kho thành công 100% bên Fulfillment.
- **SC-003**: 100% các trạng thái vận chuyển thực tế từ Fulfillment (`InTransit`, `HandedOver`, `DeliveryFailed`, `Returned`) được tiếp nhận và ánh xạ chính xác vào trạng thái của `SubOrder`.
- **SC-004**: 100% bộ kiểm thử tự động (Unit Tests & E2E Tests) của CommerceService vượt qua (bao gồm 96 tests hiện có và các unit tests mới) với 0 cảnh báo và 0 lỗi.
- **SC-005**: `dotnet build Commerce.sln -c Release` biên dịch thành công tuyệt đối (0 warnings, 0 errors).
