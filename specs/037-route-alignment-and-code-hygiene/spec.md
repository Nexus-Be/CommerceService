# Feature Specification: Route Alignment, Gateway Parity & Compiler Warning Hygiene

**Feature Branch**: `037-route-alignment-and-code-hygiene`  
**Created**: 2026-11-26  
**Status**: Ready for Implementation  
**Target Service**: `CommerceService` (Port `8003`, Database `commerce_db:5435`)  
**Scope**: Inward-Only (CommerceService internal architecture, contracts, persistence, routing, and Gateway interface alignment)  
**Input**: Triển khai kế hoạch chỉnh sửa toàn diện từ báo cáo phân tích sự cố: (1) Chuẩn hóa route tra cứu vận đơn `OrdersController.cs` sang `/api/v1/orders/{orderId}/shipments` để giải quyết xung đột với Fulfillment trên Gateway; (2) Giữ alias tương thích ngược; (3) Khắc phục triệt để 6 cảnh báo biên dịch CS8604, CS8601, CS0108; (4) Cập nhật tài liệu API nội bộ; (5) Cung cấp đặc tả cấu hình Gateway định tuyến 7 nhóm route Commerce.

---

## 1. Background & Codebase Audit Findings

Đối soát trực tiếp mã nguồn `CommerceService` trên nhánh `dev` tại thời điểm hiện tại:

1. **Vấn đề 1.3: Xung đột định tuyến Route Shipments với Fulfillment trên API Gateway (P1)**:
   - File: `Commerce.Api/Controllers/Public/OrdersController.cs` dòng 246 đang định nghĩa:
     ```csharp
     [HttpGet("/api/v1/shipments/order/{orderId:guid}")]
     public async Task<IActionResult> GetShipmentTracking([FromRoute] Guid orderId, CancellationToken ct)
     ```
   - **Xung đột thực tế**: API Gateway cấu hình chuyển tiếp toàn bộ prefix `/api/v1/shipments/**` sang `FulfillmentService` (port 8006). Khi Client gọi `GET /api/v1/shipments/order/{orderId}` qua Gateway 8080, request bị đẩy sang Fulfillment thay vì Commerce, dẫn đến HTTP 404 Not Found từ Fulfillment.
   - **Giải pháp chuẩn hóa**:
     - Đổi route chính thành `[HttpGet("{orderId:guid}/shipments")]` (URL canonical: `GET /api/v1/orders/{orderId}/shipments`), tuân thủ chuẩn RESTful phân cấp (Order là Aggregate Root, Shipments là sub-resource) và nằm trọn trong route prefix `/api/v1/orders/**` đã được Gateway chuyển sang CommerceService.
     - Đồng thời khai báo thêm route alias `[HttpGet("/api/v1/shipments/order/{orderId:guid}")]` với attribute `[Obsolete]` để duy trì tương thích ngược 100% cho các caller nội bộ trực tiếp trong cụm container.
     - Cập nhật đồng bộ các tài liệu API: `docs/api/Orders/GET_SHIPMENTS_ORDER_TRACKING.md`, `docs/api/README.md`, `docs/api_catalog.md`, `docs/COMMERCE_API_CONTRACT.md`, `docs/COMMERCE_ARCHITECTURE_AND_FLOW_SPEC.md`.

2. **Vấn đề 1.4: Cảnh báo trình biên dịch Nullability & Member Hiding (P2)**:
   - Biên dịch thực tế phát hiện **6 Warning** trong `Commerce.Application` và `Commerce.Api`:
     - `AuctionOrderService.cs(1374,31)`: CS8604 - `rawAddress.District` có kiểu `string?` truyền vào parameter `DistrictName` kiểu `string` (non-null) của constructor `ShippingAddressDto`.
     - `OrderService.cs(3900,24)`: CS8601 - `resolvedAddress.District` (`string?`) gán vào thuộc tính non-null `ShippingAddressSnapshot.District`.
     - `OrderService.cs(3904,27)`: CS8601 - `resolvedAddress.GhnWardCode` (`string?`) gán vào thuộc tính non-null `ShippingAddressSnapshot.GhnWardCode`.
     - `OrderService.cs(4023,28)`: CS8601 - `resolvedAddress.District` (`string?`) gán vào thuộc tính non-null `ShippingAddressSnapshot.District`.
     - `OrderService.cs(4027,31)`: CS8601 - `resolvedAddress.GhnWardCode` (`string?`) gán vào thuộc tính non-null `ShippingAddressSnapshot.GhnWardCode`.
     - `AdminRefundFundCasesController.cs(57,38)`: CS0108 - Action method `Request([FromBody] ...)` che khuất thuộc tính có sẵn `ControllerBase.Request`.
   - **Giải pháp**:
     - Áp dụng null-coalescing fallback an toàn `?? string.Empty` cho các trường chuỗi địa chỉ để đảm bảo non-null tại runtime và compile-time.
     - Thêm từ khóa `new` tại method `Request(...)` của `AdminRefundFundCasesController.cs` để làm rõ chủ đích và dập tắt warning CS0108.
     - Đạt mục tiêu chất lượng: `dotnet build Commerce.sln -c Release` đạt **0 Errors, 0 Warnings**.

3. **Vấn đề 1.2: Các Route con của Commerce bị thiếu trên API Gateway (P1)**:
   - Gateway hiện chỉ ánh xạ `/api/v1/carts/**` và `/api/v1/orders/**`.
   - 7 cụm route sau của CommerceService bị Gateway trả 404 khi gọi qua cổng 8080:
     - `/api/v1/checkouts/**` (`POST /api/v1/checkouts/quote`, `POST /api/v1/checkouts/quote/{id}/recalculate`)
     - `/api/v1/sub-orders/**` (`GET /api/v1/sub-orders`, `GET /api/v1/sub-orders/{id}`, `POST .../cancel`, `POST .../confirm-receipt`, `POST .../delivery-status`, `POST .../shipments/retry`, `GET .../shipments/timeline`, `GET .../financial-breakdown`, `PATCH .../shipping-address`)
     - `/api/v1/auction-orders/**` (toàn bộ lifecycle đơn đấu giá)
     - `/api/v1/discounts/**` (hệ thống voucher/khuyến mãi)
     - `/api/v1/refunds/**` (xử lý yêu cầu hoàn tiền)
     - `/api/v1/admin/platform-fee/**` (quản trị phí sàn)
     - `/api/v1/admin/buyer-refusal-penalties/**`, `/api/v1/admin/refund-fund/cases/**`, `/api/v1/admin/orders`, `/api/v1/admin/sub-orders`
   - **Giải pháp**: Xây dựng tài liệu đặc tả cấu hình định tuyến chuẩn JSON cho YARP / Reverse Proxy (`specs/037-route-alignment-and-code-hygiene/gateway-routes-spec.json`) để đội ngũ phụ trách Gateway dễ dàng áp dụng mà không làm vi phạm nguyên tắc Inward-Only.

4. **Vấn đề 1.1: Hủy vận đơn sang Fulfillment sai Endpoint & ID (P0)**:
   - **Xác nhận**: Đã được xử lý triệt để tại `FulfillmentClient.cs:605` (`api/v1/internal/shipments/order/{subOrderId}/cancel`). Unit test `FulfillmentClientCancelTests.cs` xác nhận đã phủ toàn diện và pass 100%.

---

## 2. User Scenarios & Testing *(mandatory)*

### User Story 1 - Canonical Order Shipments Tracking Route & Backward Compatibility (Priority: P1) 🎯 MVP

As a buyer or staff member checking order shipment status, I need to call `GET /api/v1/orders/{orderId}/shipments` so that the API Gateway reliably routes my request to CommerceService without colliding with FulfillmentService's shipment endpoints.

- **Why this priority**:
  Currently, any client attempting to track an order via Gateway 8080 using the old route receives HTTP 404 from FulfillmentService. Aligning to `GET /api/v1/orders/{orderId}/shipments` immediately restores shipment tracking for all users.
- **Independent Test**:
  1. Call `GET /api/v1/orders/{orderId}/shipments` with a valid buyer JWT token owning `{orderId}`. Assert HTTP 200 OK with tracking timeline data.
  2. Call `GET /api/v1/shipments/order/{orderId}` (alias). Assert HTTP 200 OK with identical data.
- **Acceptance Scenarios**:
  1. **Given** an existing Order with SubOrders having GHN tracking codes, **When** the buyer invokes `GET /api/v1/orders/{orderId}/shipments`, **Then** Commerce returns HTTP 200 OK with the consolidated list of shipments and current delivery status.
  2. **Given** a caller using the legacy path `GET /api/v1/shipments/order/{orderId}`, **When** invoked directly on CommerceService, **Then** the request still succeeds with HTTP 200 OK via route aliasing.
  3. **Given** a buyer attempting to query an order belonging to another user, **When** invoked, **Then** Commerce returns HTTP 403 Forbidden (`FORBIDDEN`, `"You do not have permission to view this order's tracking information."`).

---

### User Story 2 - Compiler Warnings Elimination & Code Hygiene (Priority: P1)

As a software engineer maintaining CommerceService, I need all nullability and member-hiding compiler warnings resolved so that the solution compiles with 0 errors and 0 warnings under `-c Release`, preventing subtle runtime `NullReferenceException` bugs.

- **Why this priority**:
  Clean compiler output with zero warnings is an immutable quality gate in CommerceService Constitution and ensures null-safety for shipping addresses.
- **Independent Test**:
  Run `dotnet build Commerce.sln -c Release`. Verify the output states `0 Warning(s)`, `0 Error(s)`.
- **Acceptance Scenarios**:
  1. **Given** an auction order shipping address resolution where `rawAddress.District` is null, **When** mapped to `ShippingAddressDto`, **Then** it safely defaults to `string.Empty` without triggering CS8604 or throwing at runtime.
  2. **Given** sub-order address modification where `resolvedAddress.District` or `GhnWardCode` is null, **When** assigned to `ShippingAddressSnapshot`, **Then** properties default to `string.Empty` without triggering CS8601.
  3. **Given** `AdminRefundFundCasesController`, **When** compiled, **Then** action method `Request` is explicitly marked with `new`, silencing CS0108.

---

### User Story 3 - API Gateway Route Parity & Exportable Routing Contract (Priority: P2)

As a DevOps engineer managing the system API Gateway, I need a complete and verified JSON routing specification for all CommerceService public and admin endpoints, so that all sub-order, checkout, auction-order, discount, refund, and admin routes are cleanly exposed without 404 errors.

- **Why this priority**:
  Ensures seamless client-to-service communication across the API Gateway without violating CommerceService's Inward-Only boundary.
- **Independent Test**:
  Review `specs/037-route-alignment-and-code-hygiene/gateway-routes-spec.json` against all controllers in `Commerce.Api/Controllers/`. Verify 100% of controller routes have matching Gateway route definitions.
- **Acceptance Scenarios**:
  1. **Given** the generated Gateway routing specification, **When** inspected, **Then** it includes route clusters for `checkouts`, `sub-orders`, `auction-orders`, `discounts`, `refunds`, and `admin` targeting `http://commerce-service:8003`.
  2. **Given** any existing route on `OrdersController` or `CartsController`, **When** verified against the specification, **Then** there are no conflicting prefixes with sibling services (`Fulfillment`, `Catalog`, `Wallet`).

---

### Edge Cases

- **EC-001**: What happens if a client sends a request to the old route `/api/v1/shipments/order/{orderId}` directly to CommerceService?
  - *Resolution*: Handled transparently by the secondary route attribute on `GetShipmentTracking`, returning identical payload.
- **EC-002**: What happens if `ShippingAddressInput.District` is omitted or null in `ResolveShippingAddressAsync`?
  - *Resolution*: Fallback to `string.Empty` prevents null reference assignment while domain validation ensures GHN district ID and ward code are valid for carrier fee calculation.
- **EC-003**: What happens if an unauthenticated user calls `GET /api/v1/orders/{orderId}/shipments`?
  - *Resolution*: `this.TryGetUserId` returns HTTP 401 Unauthorized with standard RFC 9110 problem details.

---

## 3. Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST expose `GET /api/v1/orders/{orderId}/shipments` on `OrdersController` to retrieve shipment and carrier tracking for a master order.
- **FR-002**: System MUST continue to support `GET /api/v1/shipments/order/{orderId}` as a secondary route on `OrdersController` for backward compatibility during rollout.
- **FR-003**: System MUST enforce buyer ownership and admin/staff role verification on `GET /api/v1/orders/{orderId}/shipments`, rejecting unauthorized callers with HTTP 403 `FORBIDDEN`.
- **FR-004**: All internal API documentation (`docs/api/Orders/GET_SHIPMENTS_ORDER_TRACKING.md`, `docs/api/README.md`, `docs/api_catalog.md`, `docs/COMMERCE_API_CONTRACT.md`, `docs/COMMERCE_ARCHITECTURE_AND_FLOW_SPEC.md`) MUST be updated to document `/api/v1/orders/{orderId}/shipments` as the canonical route.
- **FR-005**: System MUST eliminate compiler warning `CS8604` in `AuctionOrderService.cs` by applying null-coalescing `?? string.Empty` for `rawAddress.District`.
- **FR-006**: System MUST eliminate compiler warnings `CS8601` in `OrderService.cs` (lines 3900, 3904, 4023, 4027) by applying null-coalescing `?? string.Empty` for `resolvedAddress.District` and `resolvedAddress.GhnWardCode`.
- **FR-007**: System MUST eliminate compiler warning `CS0108` in `AdminRefundFundCasesController.cs` by adding the `new` modifier to action method `Request`.
- **FR-008**: The solution `Commerce.sln` MUST compile cleanly with zero (0) warnings and zero (0) errors under `Release` configuration.
- **FR-009**: System MUST provide a self-contained YARP / Reverse Proxy routing artifact (`specs/037-route-alignment-and-code-hygiene/gateway-routes-spec.json`) mapping all 7 Commerce route prefixes to port 8003.
- **FR-010**: All existing unit tests in `Commerce.UnitTests` (696+ tests) MUST pass without regression.

### Key Entities

- **`Order`**: Aggregate root for buyer purchases, containing one or more SubOrders.
- **`SubOrder`**: Seller-specific sub-order containing line items, status, and GHN shipping tracking.
- **`ShippingAddressSnapshot`**: Value object persisting buyer delivery address, ensuring non-null string properties for province, district, ward, and full address text.
- **`ShippingAddressDto`**: Contract DTO used across Application and inter-service boundaries with non-null address components.

---

## 4. Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `GET /api/v1/orders/{orderId}/shipments` is fully accessible and returns 200 OK for authorized orders without any routing collisions on Gateway.
- **SC-002**: Solution build `dotnet build Commerce.sln -c Release` achieves **0 Warnings** and **0 Errors**.
- **SC-003**: 100% of unit tests in `Commerce.UnitTests` pass successfully (696+ passed, 0 failed).
- **SC-004**: 100% documentation consistency across all 5 API catalog and specification files.
- **SC-005**: Zero breaking changes for existing internal callers due to preservation of the alias route.

---

## 5. Assumptions

- **Assumption 1**: API Gateway routes requests based on URL prefix matching; `/api/v1/orders/**` is forwarded to CommerceService (port 8003).
- **Assumption 2**: FulfillmentService retains ownership of `/api/v1/shipments/**` on Gateway for shipment creation and webhook management.
- **Assumption 3**: The Gateway configuration JSON is self-contained and ready for consumption by DevOps / Gateway engineers without requiring out-of-repo modifications by CommerceService agents.
