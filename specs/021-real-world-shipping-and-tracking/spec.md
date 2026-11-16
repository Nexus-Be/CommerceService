# Feature Specification: Real-World Carrier Logistics and Shipment Tracking

**Feature Branch**: `021-real-world-shipping-and-tracking`  
**Created**: 2026-11-12  
**Status**: Draft  
**Input**: User description: "Triển khai toàn diện hệ thống giao vận và theo dõi hành trình đơn hàng chuẩn thực tế sàn thương mại điện tử kết nối Fulfillment Service (GHN): (1) Lưu trữ persistent thông tin vận đơn thật (CarrierCode, TrackingCode, LabelPrintUrl, EstimatedDeliveryAt) vào SubOrder và AuctionOrder; xóa bỏ hoàn toàn việc sinh mã vận đơn giả lập; (2) Ghi nhận toàn bộ sự kiện webhook từ Fulfillment vào bảng OrderStatusHistories để cung cấp Timeline hành trình trực quan cho người mua và người bán qua endpoint GET /api/v1/sub-orders/{subOrderId}/shipments/timeline; (3) Tự động tổng hợp và đồng bộ trạng thái đơn cha Master Order sang Delivered khi 100% đơn con hoàn tất; (4) Xử lý phân tầng giao hàng thất bại (DeliveryAttemptFailed ghi nhận lý do hẹn lại, DeliveryFailed/Returned hoàn hàng và kích hoạt hoàn tiền ví tự động); (5) Cung cấp cơ chế retry an toàn POST /api/v1/sub-orders/{subOrderId}/shipments/retry khi GHN nghẽn mạng; (6) Tính cước vận chuyển động theo trọng lượng thực tế của từng sản phẩm; tuân thủ nghiêm ngặt Hiến pháp CommerceService v1.4.0, Fail-Closed, 100% English Sentence-case error messages, build 0 error 0 warning và pass 100% test suite."

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Persistent Carrier Shipment Creation & Dispatch (Priority: P1) 🎯 MVP

As a seller accepting a paid sub-order, when I confirm order acceptance, the system must dispatch a shipment creation request to the fulfillment service (`POST /api/v1/internal/shipments`), persist the authentic carrier tracking code, carrier code, and printable label URL directly on the `SubOrder` record, and return this data in the response, eliminating all artificial mock tracking codes.

**Why this priority**: Core logistics integration requirement. Without persisting real carrier tracking metadata, sellers cannot print shipping labels to package items, and buyers receive fake tracking strings that fail carrier lookup.

**Independent Test**: Can be verified by issuing a seller order acceptance (`POST /api/v1/orders/{orderId}/accept`) with mock fulfillment returning real carrier credentials (`GHN-SGN-1029384`), asserting the database entity stores `TrackingCode = "GHN-SGN-1029384"`, `CarrierCode = "GHN"`, and verifying `GetShipmentTrackingAsync` returns this genuine code with zero occurrences of `GHN-{subOrderNo}`.

**Acceptance Scenarios**:

1. **Given** a paid sub-order in `Paid` status, **When** the seller calls accept order, **Then** Commerce invokes `IFulfillmentClient.CreateShipmentAsync`, persists `TrackingCode`, `CarrierCode`, `LabelPrintUrl`, updates status to `Confirmed`, and records `OrderStatusHistory`.
2. **Given** a carrier network timeout during order acceptance, **When** shipment creation fails, **Then** Commerce still transitions status to `Confirmed`, sets `ShipmentPendingCreation = true`, and returns a warning so seller acceptance is not blocked.
3. **Given** a confirmed sub-order with missing tracking, **When** seller calls `POST /api/v1/sub-orders/{subOrderId}/shipments/retry`, **Then** Commerce retries `CreateShipmentAsync`, saves returned credentials, and sets `ShipmentPendingCreation = false`.

---

### User Story 2 - Real-Time Carrier Delivery Webhook & Visual Timeline (Priority: P1) 🎯 MVP

As a customer or seller, I need incoming delivery status callbacks from the fulfillment service (`POST /api/v1/sub-orders/{subOrderId}/delivery-status`) to ingest carrier events, update order status accordingly, record an `OrderStatusHistory` entry for every transition with timestamps and locations, and expose a visual timeline query (`GET /api/v1/sub-orders/{subOrderId}/shipments/timeline`).

**Why this priority**: Essential customer experience pillar. Customers expect real-time milestone updates (Packed $\rightarrow$ HandedOver $\rightarrow$ InTransit $\rightarrow$ Delivered) and full journey transparency.

**Independent Test**: Can be verified by delivering status webhooks for `ReadyToPick`, `HandedOver`, `InTransit`, and `Delivered`, asserting `OrderStatusHistories` records every event chronologically, and querying the timeline endpoint to confirm all events and location metadata match.

**Acceptance Scenarios**:

1. **Given** a sub-order in `Confirmed` status, **When** Fulfillment sends `POST /delivery-status` with `status: "HandedOver"`, **Then** Commerce updates status to `Shipped`, sets `ShippedAt = UtcNow`, and appends an `OrderStatusHistory` record.
2. **Given** a sub-order in `Shipped` status, **When** Fulfillment sends `POST /delivery-status` with `status: "InTransit"` and `location: "Tan Binh Hub"`, **Then** Commerce records an `OrderStatusHistory` entry noting transit location while preserving `Shipped` status.
3. **Given** an authenticated buyer or seller, **When** calling `GET /api/v1/sub-orders/{subOrderId}/shipments/timeline`, **Then** Commerce returns the full chronological timeline array with timestamps, status titles, and descriptive carrier notes.

---

### User Story 3 - Master Order State Aggregation & Delivery Completion (Priority: P2)

As an e-commerce buyer with a multi-shop order, when all individual sub-orders from different sellers reach `Delivered` or terminal status, the parent `Order` status must automatically transition to `Delivered`, enabling post-delivery lifecycle management and review capabilities.

**Why this priority**: Prevents master orders from remaining indefinitely in `Paid` status after all physical goods have been handed over to the buyer.

**Independent Test**: Can be verified by creating an order with 2 sub-orders (Shop A and Shop B), delivering SubOrder A (Order remains `Paid`/active), then delivering SubOrder B, and asserting parent `Order.Status` transitions to `Delivered`.

**Acceptance Scenarios**:

1. **Given** a master order with multiple sub-orders where only one sub-order is delivered, **When** inspected, **Then** master order status remains in its active payment/processing state.
2. **Given** a master order where the final pending sub-order transitions to `Delivered`, **When** the delivery webhook completes, **Then** master order status automatically updates to `OrderStatus.Delivered`.
3. **Given** a master order where all sub-orders are cancelled, **When** evaluated, **Then** master order status updates to `OrderStatus.Cancelled`.

---

### User Story 4 - Delivery Exception Handling & Automated Return/Refund (Priority: P2)

As a commerce platform operator, when a carrier reports temporary delivery failure (`DeliveryAttemptFailed`), the system must record the attempt without cancelling the order; when the carrier reports terminal delivery failure (`DeliveryFailed`) and package return (`Returned`), the system must transition sub-order status to `Cancelled`, issue an automated full refund to the buyer's wallet for prepaid orders, and invoke inventory release back to the seller's warehouse stock.

**Why this priority**: Protects buyer finances and inventory integrity during logistics failures, preventing stuck escrow and abandoned stock.

**Independent Test**: Can be verified by posting `DeliveryAttemptFailed` (sub-order remains `Shipped`, timeline logged), followed by `Returned` on a paid sub-order, asserting `SubOrder.Status` becomes `Cancelled`, an `OrderRefund` record is generated, `IWalletClient.RefundSubOrderPaymentAsync` is called, and `IFulfillmentClient.ReleaseSubOrderStockAsync` is executed with reason `PackageReturned`.

**Acceptance Scenarios**:

1. **Given** a sub-order out for delivery, **When** Fulfillment sends `status: "DeliveryAttemptFailed"`, **Then** Commerce retains `SubOrderStatus.Shipped` and logs the customer reschedule note in `OrderStatusHistories`.
2. **Given** a prepaid sub-order, **When** Fulfillment sends `status: "Returned"`, **Then** Commerce sets status to `Cancelled`, creates `OrderCancellation`, issues an automated wallet refund, and calls `ReleaseSubOrderStockAsync`.
3. **Given** a COD sub-order, **When** Fulfillment sends `status: "Returned"`, **Then** Commerce sets status to `Cancelled` and creates `OrderCancellation` without issuing a cash refund.

---

### User Story 5 - Dynamic Weight-Based Carrier Quotation (Priority: P3)

As a customer checking out, carrier shipping fees must be dynamically evaluated based on total package weight (`quantity * unitWeightGrams`) and valid GHN destination district/ward codes, rejecting invalid addresses with clear RFC 9110 guidance.

**Why this priority**: Heavy or bulky items incur higher shipping costs; dynamic weight quotation prevents shipping fee shortfalls that sellers would otherwise have to absorb.

**Independent Test**: Can be verified by calculating shipping quotes with different item quantities and weights, asserting outbound requests to `POST /api/v1/internal/shipping/quote` serialize populated `weightGrams` per line item.

**Acceptance Scenarios**:

1. **Given** valid shop items with defined weights and destination codes, **When** previewing checkout, **Then** Commerce dispatches `ShopShippingQuoteRequest` with cumulative line-item weights to Fulfillment Service.
2. **Given** destination address with missing or zero `GhnDistrictId`, **When** calculating shipping fee, **Then** Commerce returns HTTP 400 `INVALID_SHIPPING_ADDRESS` with English sentence-case guidance.

---

## Edge Cases

- **Duplicate Delivery Webhooks**: If Fulfillment sends duplicate webhooks for the same status and timestamp, Commerce applies safe idempotent handling, returning HTTP 200 without appending duplicate history rows.
- **Carrier Service Outage during Acceptance**: If GHN carrier API is down when seller accepts the order, Commerce persists `ShipmentPendingCreation = true` to allow seller workflow continuity, surfacing an alert for later retry.
- **Partial Order Return**: In a multi-item sub-order where only one sub-order is returned, sibling sub-orders are completely unaffected and continue normal settlement.
- **Auction Order Tracking**: Auction orders (`AuctionOrder`) share identical carrier tracking fields (`CarrierCode`, `TrackingCode`, `LabelPrintUrl`) and timeline mechanics.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `SubOrder` and `AuctionOrder` entities MUST include persistent columns: `CarrierCode` (varchar 50), `TrackingCode` (varchar 100, indexed), `LabelPrintUrl` (varchar 500), `EstimatedDeliveryAt` (DateTimeOffset?), and `ShipmentPendingCreation` (boolean).
- **FR-002**: `OrderService.SellerAcceptSubOrderAsync` MUST store `ShipmentCreationResult.TrackingCode`, `CarrierCode`, and `LabelPrintUrl` returned by `IFulfillmentClient.CreateShipmentAsync`.
- **FR-003**: `OrderService.GetShipmentTrackingAsync` MUST return authentic persisted carrier tracking codes, completely eliminating artificial string formatting (`GHN-{subOrderNo}`).
- **FR-004**: System MUST expose `GET /api/v1/sub-orders/{subOrderId}/shipments/timeline` returning comprehensive shipment metadata, destination snapshot, and chronological events from `OrderStatusHistories`.
- **FR-005**: System MUST expose `POST /api/v1/sub-orders/{subOrderId}/shipments/retry` allowing sellers to retry carrier shipment generation for orders with `ShipmentPendingCreation == true`.
- **FR-006**: Inbound webhook `POST /api/v1/sub-orders/{subOrderId}/delivery-status` MUST enforce `X-Internal-Service-Key` header authentication, returning HTTP 403 `FORBIDDEN` on failure.
- **FR-007**: Every delivery status webhook transition MUST persist an `OrderStatusHistory` record containing status, timestamp, actor role `System`, and descriptive reason/location.
- **FR-008**: When all sub-orders of a master order reach `Delivered` or `Completed`, parent `Order.Status` MUST automatically update to `OrderStatus.Delivered`.
- **FR-009**: When a sub-order receives `Returned` status:
  - If paid: system MUST trigger automated wallet refund via `IWalletClient.RefundSubOrderPaymentAsync`.
  - System MUST invoke `IFulfillmentClient.ReleaseSubOrderStockAsync` with reason `"PackageReturned"`.
- **FR-010**: All error messages MUST adhere to 100% English Sentence-case formatting and SCREAMING_SNAKE_CASE error codes (`SUB_ORDER_NOT_FOUND`, `INVALID_SHIPPING_ADDRESS`, `SHIPMENT_ALREADY_EXISTS`, `FORBIDDEN`).

---

### Key Entities

- **SubOrder**: Shop-level order containing seller reference, financial snapshots, and new logistics tracking columns (`CarrierCode`, `TrackingCode`, `LabelPrintUrl`, `EstimatedDeliveryAt`, `ShipmentPendingCreation`).
- **OrderStatusHistory**: Audit trail entry recording order lifecycle transitions, carrier status updates, and timestamps.
- **ShipmentTimelineResponse**: Visual DTO aggregating carrier information, destination address, and chronological timeline events.
- **UpdateDeliveryStatusRequest**: Extended webhook payload accepting `status`, `deliveredAt`, `carrierCode`, `trackingCode`, `note`, and `location`.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of accepted sub-orders store genuine carrier tracking credentials with 0 occurrences of fake `GHN-...` mock strings.
- **SC-002**: 100% of delivery webhook callbacks generate corresponding `OrderStatusHistory` records, verifiable via `/timeline` query.
- **SC-003**: 100% of multi-shop orders automatically transition master `Order.Status` to `Delivered` upon final sub-order delivery.
- **SC-004**: 100% of returned prepaid sub-orders trigger automated wallet refund and inventory release without manual intervention.
- **SC-005**: Complete test suite passes with 100% success rate across all application, domain, and infrastructure tests (`dotnet test Commerce.sln -c Release`).

---

## Assumptions

- FulfillmentService operates at `http://localhost:8006` (or `http://fulfillment-service:8006` in Docker) and validates `X-Internal-Service-Key: eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e`.
- Carrier GHN webhook events are routed through FulfillmentService before reaching CommerceService.
- Catalog items provide or default to weight in grams (`weightGrams >= 100`).
