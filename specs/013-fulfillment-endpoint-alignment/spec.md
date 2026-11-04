# Feature Specification: Fulfillment Service Internal REST Endpoint Alignment

**Feature Branch**: `013-fulfillment-endpoint-alignment`  
**Created**: 2026-11-04  
**Status**: Draft  
**Input**: User description: "Chuẩn hóa và đồng bộ 100% các route RESTful nội bộ trong Commerce.Infrastructure/Services/Fulfillment/FulfillmentClient.cs theo Báo cáo nghiệm thu E2E của FulfillmentService (Task F01-F08): (1) Đổi URI nhả kho từng phần của SubOrder bị từ chối từ '/api/v1/internal/inventories/reservations/{orderId}/release?reason=SubOrderRejected' sang endpoint chuyên trách 'POST /api/v1/internal/inventories/reservations/{orderId}/release-items' với body { items: [{ skuId, quantity }], reason }; (2) Đổi URI tính cước vận chuyển GHN từ 'POST /api/v1/fulfillment/shipping/quote' sang route nội bộ 'POST /api/v1/internal/shipping/quote'; (3) Đổi URI tạo vận đơn giao hàng từ 'POST /api/v1/shipments' sang route nội bộ 'POST /api/v1/internal/shipments'; (4) Đồng bộ toàn bộ Mock URI trong tests/Commerce.UnitTests/Infrastructure/FulfillmentClientTests.cs và OpenAPI contract; đảm bảo 100% Fail-Closed RFC 9110, dotnet build 0 error 0 warning và dotnet test đạt 100% pass rate."

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Targeted Sub-Order Inventory Release via Dedicated Endpoint (Priority: P1)

As the Commerce order management service, when a seller rejects a specific sub-order from a multi-shop master order, the system must invoke the dedicated partial inventory release endpoint (`/release-items`) with the exact SKU list and rejection reason, ensuring only that seller's items are released while preserving inventory reservations for all remaining sub-orders.

**Why this priority**: Calling the generic `/release` route risks cancelling the entire master order hold or failing on endpoint contract mismatches, which causes stock reservation loss for valid shops.

**Independent Test**: Can be independently verified by issuing a seller rejection on SubOrder A of a multi-shop order and asserting an outbound HTTP request targets `POST /api/v1/internal/inventories/reservations/{orderId}/release-items` with payload `{ items: [...], reason: "SubOrderRejected" }`, verifying SubOrder B remains active and held.

**Acceptance Scenarios**:

1. **Given** a confirmed multi-shop order where Shop A rejects its sub-order, **When** the seller rejection is processed, **Then** the system sends a request to `POST /api/v1/internal/inventories/reservations/{orderId}/release-items` containing only Shop A's items and reason `"SubOrderRejected"`.
2. **Given** an invalid or empty list of items to release, **When** the release is requested, **Then** the client aborts outbound invocation and logs a descriptive warning without disrupting order state.

---

### User Story 2 - Accurate Carrier Shipping Quotation via Internal Service Route (Priority: P1)

As a customer reviewing the checkout preview, the system must retrieve dynamic shipping fees by communicating with the fulfillment service through its standardized internal routing path (`/api/v1/internal/shipping/quote`), ensuring live quotes are returned without 404 route errors.

**Why this priority**: Calling legacy or public route aliases causes HTTP 404 errors, which under fail-closed policies aborts checkout and prevents customers from placing orders.

**Independent Test**: Can be independently verified by requesting a shipping quote during checkout preview and confirming the outbound HTTP POST targets `/api/v1/internal/shipping/quote`, returning the calculated fee without mock fallbacks.

**Acceptance Scenarios**:

1. **Given** valid shop items and a destination delivery address, **When** a shipping quote is requested, **Then** the request is sent to `POST /api/v1/internal/shipping/quote` and the fee amount is incorporated into the checkout summary.
2. **Given** the fulfillment service returns an HTTP 500 or experiences network disruption, **When** a quote is requested, **Then** the system throws an RFC 9110 HTTP 503 error (`FULFILLMENT_SERVICE_UNAVAILABLE`) rather than substituting a hardcoded mock fee.

---

### User Story 3 - Carrier Shipment Dispatch via Protected Internal Route (Priority: P1)

As a seller accepting an order for dispatch, the system must request shipping label creation and tracking code generation through the fulfillment service's internal shipments endpoint (`/api/v1/internal/shipments`), obtaining genuine carrier credentials and dispatch tracking without artificial mock fallbacks.

**Why this priority**: Dispatching to public or un-prefixed shipment routes causes HTTP 404 failures, blocking the seller's workflow and preventing physical package handover.

**Independent Test**: Can be independently verified by triggering seller order acceptance and verifying the outbound HTTP POST targets `/api/v1/internal/shipments` with `{ subOrderId }`, returning valid tracking code and printable label URLs.

**Acceptance Scenarios**:

1. **Given** an eligible sub-order accepted by the seller, **When** shipment dispatch is initiated, **Then** an HTTP POST is sent to `POST /api/v1/internal/shipments` with the sub-order identifier.
2. **Given** the carrier service is unreachable or returns a server error, **When** shipment creation fails, **Then** the system raises HTTP 503 (`FULFILLMENT_SERVICE_UNAVAILABLE`) and rolls back the order transition, preventing fake tracking code creation.

---

### User Story 4 - End-to-End Contract Consistency and Test Coverage (Priority: P2)

As an engineer and compliance auditor, I need all unit test fixtures, mock handlers, and OpenAPI specifications to reflect the finalized internal route conventions, guaranteeing zero test drift across build pipelines.

**Why this priority**: Stale mock routes mask integration defects and cause automated build validation pipelines to fail or provide false confidence.

**Independent Test**: Can be verified by running `dotnet test` and inspecting test reports to confirm all client tests assert against the standardized internal routes.

**Acceptance Scenarios**:

1. **Given** the updated client routes, **When** the unit test suite runs, **Then** all mock assertions verify `/api/v1/internal/inventories/reservations/{orderId}/release-items`, `/api/v1/internal/shipping/quote`, and `/api/v1/internal/shipments`.
2. **Given** the local OpenAPI specification in `contracts/`, **When** inspected, **Then** path declarations match the internal service routes exactly.

---

### Edge Cases

- **Partial Release Payload Malformation**: If a sub-order has zero items, the client handles the guard clause locally before dispatching HTTP calls.
- **Fulfillment Network Timeout**: When any outbound call times out, the client logs detailed context with seller and order IDs, throwing `CommerceDomainException` with HTTP 503 without swallowing errors.
- **Carrier Quotation 400 Bad Request**: If the destination ward code or district ID is unrecognized by the carrier, the client surfaces HTTP 400 `SHIPPING_QUOTE_FAILED` with clean English explanations.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The client MUST target `POST api/v1/internal/inventories/reservations/{orderId}/release-items` when releasing inventory for a rejected sub-order.
- **FR-002**: The partial inventory release payload MUST serialize an `items` array with elements containing `skuId` and `quantity`, along with an optional `reason` string set to `"SubOrderRejected"`.
- **FR-003**: The client MUST target `POST api/v1/internal/shipping/quote` when calculating dynamic carrier shipping fees.
- **FR-004**: The client MUST target `POST api/v1/internal/shipments` with `{ subOrderId }` when creating carrier shipments upon seller order acceptance.
- **FR-005**: All client methods MUST operate under Fail-Closed policies, prohibiting artificial mock values (such as hardcoded 30,000 VND shipping fees or fake `GHN-...` tracking numbers) upon transport or server failures.
- **FR-006**: Unit tests in `tests/Commerce.UnitTests/Infrastructure/FulfillmentClientTests.cs` MUST verify exact URI matching for `/release-items`, `/internal/shipping/quote`, and `/internal/shipments`.
- **FR-007**: The OpenAPI contract in `specs/013-fulfillment-endpoint-alignment/contracts/` MUST accurately document the three standardized internal endpoints and their payload schemas.

---

### Key Entities

- **ReleaseReservationItemsRequest**: Payload containing list of items (`skuId`, `quantity`) and rejection reason to selectively restore inventory balances.
- **ShopShippingQuoteRequest**: Context containing shop identifier, carrier district/ward destination codes, and item weights for dynamic carrier fee evaluation.
- **ShipmentCreationRequest**: Minimal dispatch payload containing `subOrderId` to issue carrier tracking and label print assets.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of seller sub-order rejection flows release only targeted items without dropping sibling shop reservations.
- **SC-002**: 100% of checkout shipping quotation requests resolve through internal service routes with 0 occurrences of HTTP 404 Route Not Found errors.
- **SC-003**: 100% of shipment creation requests route through protected internal endpoints without dummy tracking mocks.
- **SC-004**: The complete solution builds with 0 errors and 0 warnings, passing 100% of automated unit tests across the repository.

---

## Assumptions

- FulfillmentService listens on port `8006` in local and containerized environments.
- FulfillmentService handles authentication for internal endpoints via the configured `X-Internal-Service-Key` header.
- The carrier adapter in FulfillmentService handles fallback calculations internally when carrier providers experience temporary network timeouts.
