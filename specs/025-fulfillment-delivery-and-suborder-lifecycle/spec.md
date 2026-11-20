# Feature Specification: Fulfillment Delivery Synchronization and Sub-Order Lifecycle Governance

**Feature Branch**: `025-fulfillment-delivery-and-suborder-lifecycle`  
**Created**: 2026-11-17  
**Status**: Draft  
**Input**: User description: "Đồng bộ tích hợp Fulfillment v2: Nhận Webhook giao hàng GHN (POD & Bưu tá), siết chặt điều kiện xác nhận nhận hàng, hỗ trợ người mua hủy đơn theo từng SubOrder, và tự động hoàn tất đơn hàng sau thời hạn giao."

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Carrier Delivery & Proof-of-Delivery Sync (Priority: P1)

As the e-commerce platform and seller,  
I want logistics delivery updates (including Proof of Delivery photos and courier contact information) from Fulfillment Service to accurately update sub-order shipping status,  
So that buyers and sellers have real-time visibility into package arrival while escrow funds remain safely secured by the platform until inspection.

**Why this priority**: Correct carrier delivery synchronization is the foundational gateway between external physical logistics (GHN) and internal order lifecycle states. Without it, orders cannot advance to delivery and escrow funds cannot be legitimately settled.

**Independent Test**: Can be fully tested by dispatching a delivery status webhook for a sub-order with carrier details and POD URL, verifying the sub-order transitions to `Delivered`, history is logged, and escrow funds remain strictly in platform escrow.

**Acceptance Scenarios**:
1. **Given** a sub-order in `Shipped` status with an assigned tracking number,  
   **When** the Fulfillment Service delivers a webhook event indicating status `Delivered` with carrier code, tracking code, delivery timestamp, courier name, courier phone, and proof-of-delivery (POD) photo URL,  
   **Then** the sub-order status updates to `Delivered`, the delivery timestamp is recorded, a comprehensive timeline history event is stored with courier and POD details, and escrow funds remain locked in the platform account.
2. **Given** a master order containing multiple sub-orders,  
   **When** the final remaining sub-order transitions to `Delivered`,  
   **Then** the parent master order status automatically synchronizes to `Delivered`.
3. **Given** an unauthenticated webhook request or invalid service key,  
   **When** the delivery status endpoint is invoked,  
   **Then** the request is rejected immediately with an HTTP 403 Forbidden problem response and order state is unchanged.

---

### User Story 2 - Buyer Delivery Confirmation & Escrow Settlement (Priority: P2)

As a buyer who has received their delivered package,  
I want to explicitly confirm receipt of my items,  
So that the seller receives their deserved payout from platform escrow and I unlock the right to review and rate my purchased products.

**Why this priority**: Escrow settlement is the primary financial milestone of e-commerce. Protecting buyer rights by strictly ensuring goods have physically arrived before allowing receipt confirmation prevents premature payouts and delivery fraud.

**Independent Test**: Can be tested by attempting confirmation on an order before delivery (must be rejected) versus confirming an order in `Delivered` status (must succeed, settle funds, reward reputation, and sync review permissions).

**Acceptance Scenarios**:
1. **Given** a sub-order in `Delivered` status,  
   **When** the buyer invokes the receipt confirmation endpoint for that sub-order,  
   **Then** the sub-order status transitions to `Completed`, seller earnings are released from escrow to the seller's wallet, the seller receives a reputation bonus (+1 point), purchase records are synchronized to catalog review services, and the parent order is updated if all siblings are completed or cancelled.
2. **Given** a sub-order in `Shipped`, `Confirmed`, or `Paid` status (not yet delivered),  
   **When** the buyer attempts to invoke the receipt confirmation endpoint,  
   **Then** the system rejects the request with an invalid status error, informing the user that items can only be confirmed after delivery.
3. **Given** a master order where at least one sub-order is not in `Delivered` status,  
   **When** the buyer attempts to invoke the master order completion endpoint,  
   **Then** the system rejects the request, ensuring premature completion of unfulfilled items is prevented.

---

### User Story 3 - Independent Sub-Order Cancellation by Buyer (Priority: P3)

As a buyer who purchased items from multiple sellers in a single checkout,  
I want to cancel an unfulfilled sub-order from an unresponsive or delayed seller without affecting my other shipments,  
So that I receive an immediate refund for the cancelled portion while my other items continue their normal delivery.

**Why this priority**: In a multi-vendor marketplace, tying the cancellation of the entire order to all sellers penalizes prompt sellers when one seller delays. Sub-order cancellation provides a modern, fair shopping experience matching top-tier e-commerce platforms.

**Independent Test**: Can be tested by creating an order with two sub-orders (Shop A and Shop B), shipping Shop A, and then having the buyer cancel Shop B's sub-order. Shop B must be cancelled, inventory released, wallet refunded, while Shop A remains active in transit.

**Acceptance Scenarios**:
1. **Given** an order with multiple sub-orders where Sub-Order A is in `Shipped` status and Sub-Order B is in `Paid` or `Confirmed` status,  
   **When** the buyer submits a cancellation request specifically targeting Sub-Order B,  
   **Then** only Sub-Order B is marked as `Cancelled`, the inventory reserved for Sub-Order B is released via the fulfillment service, any created carrier shipment is cancelled with the logistics provider, the buyer's wallet is credited with a 100% refund for Sub-Order B, and Sub-Order A continues unaffected.
2. **Given** a sub-order that has already progressed to `Shipped`, `Delivered`, or `Completed`,  
   **When** the buyer attempts to cancel that sub-order,  
   **Then** the cancellation is rejected with an explanatory message that dispatched packages cannot be cancelled directly.
3. **Given** a master order where all constituent sub-orders become cancelled,  
   **When** the final sub-order is cancelled,  
   **Then** the master parent order automatically transitions to `Cancelled`.

---

### User Story 4 - Automated Completion of Delivered Orders (Priority: P4)

As an online marketplace operator and seller,  
I want packages that have been delivered for a configurable grace period without dispute to automatically complete,  
So that sellers receive their sales revenue in a predictable timeframe even if buyers forget to manually click the confirmation button.

**Why this priority**: The vast majority of buyers neglect to return to the application to confirm receipt after receiving items. Automated completion eliminates permanent escrow lockup and provides financial predictability for merchants.

**Independent Test**: Can be tested by configuring a short auto-completion threshold, seeding a sub-order in `Delivered` status with an older timestamp, triggering the background worker, and verifying the sub-order transitions to `Completed` with escrow funds successfully disbursed.

**Acceptance Scenarios**:
1. **Given** a sub-order in `Delivered` status whose delivery timestamp exceeds the configured auto-completion window (e.g., 3 days),  
   **When** the periodic background lifecycle worker runs,  
   **Then** the sub-order automatically transitions to `Completed`, seller escrow settlement is initiated, seller reputation is incremented, and an automated completion record is added to status history.
2. **Given** a delivered sub-order that is currently subject to an active dispute or return request,  
   **When** the background worker evaluates eligible sub-orders,  
   **Then** the sub-order is bypassed and remains in dispute until resolved.

---

### Edge Cases

- **Duplicate / Rapid Webhook Retries**: What happens if the logistics carrier delivers duplicate delivery webhooks within seconds? The system MUST detect duplicate delivery events within a narrow deduplication window and respond idempotently without duplicate status transitions or double notifications.
- **Carrier Cancellation Failure**: What happens if the buyer cancels a confirmed sub-order but the logistics provider's cancellation API is temporarily unreachable? The system MUST safely mark the sub-order cancelled, process the financial refund, release reserved stock, and log a warning for subsequent manual carrier synchronization.
- **Cash on Delivery (COD) Invariant**: How does the system handle escrow settlement when an order was placed using COD instead of Wallet? When a COD order reaches `Completed`, the system MUST NOT disburse funds from platform escrow (as funds were collected as cash by the courier), but MUST still award reputation points and catalog review permissions.
- **Unauthorized Cancellation Attempts**: How does the system handle an attempt by Buyer X to cancel a sub-order belonging to Buyer Y? The system MUST verify resource ownership against the authenticated caller and reject the request with HTTP 403 Forbidden.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST accept carrier delivery webhook payloads containing carrier code, tracking code, delivery status, timestamp, note, location, courier name, courier phone, and proof-of-delivery (POD) URL.
- **FR-002**: The system MUST enforce authentication on logistics webhook endpoints using the designated internal service secret header.
- **FR-003**: The system MUST update sub-order status to `Delivered` and record courier metadata and POD URLs upon receiving a verified delivery notification.
- **FR-004**: The system MUST prevent receipt confirmation on sub-orders unless the current status is strictly `Delivered`.
- **FR-005**: The system MUST prevent master order completion unless all non-cancelled sub-orders are in `Delivered` status.
- **FR-006**: Upon receipt confirmation of a digital wallet order, the system MUST disburse seller earnings from platform escrow to the seller's wallet via the wallet client.
- **FR-007**: Upon receipt confirmation of a Cash on Delivery (COD) order, the system MUST skip wallet escrow disbursement and mark the transaction completed for logistics reconciliation.
- **FR-008**: The system MUST reward the seller with positive reputation (+1) and synchronize purchase records to the catalog service upon sub-order completion.
- **FR-009**: The system MUST provide an endpoint allowing buyers to cancel an individual sub-order belonging to their master order prior to shipment.
- **FR-010**: When a sub-order is cancelled, the system MUST release inventory reservations for only that sub-order's items via the fulfillment service.
- **FR-011**: When a confirmed sub-order with a carrier tracking code is cancelled, the system MUST invoke the fulfillment service carrier cancellation endpoint to void the dispatch order.
- **FR-012**: When a paid sub-order is cancelled, the system MUST issue an automated 100% wallet refund to the buyer for the sub-order's total amount.
- **FR-013**: The system MUST automatically synchronize parent master order status to `Delivered` when all active sub-orders are delivered, and to `Completed` or `Cancelled` when all sub-orders reach terminal states.
- **FR-014**: The system MUST provide an automated background worker that transitions delivered sub-orders past their inspection window into `Completed` status with escrow settlement.
- **FR-015**: The auto-completion inspection window duration MUST be configurable via application settings to facilitate production stability and rapid demonstration.

---

### Key Entities

- **SubOrder**: Represents a distinct shop-level partition within a master order. Tracks status (`PendingPayment`, `Paid`, `Confirmed`, `Shipped`, `Delivered`, `Completed`, `Cancelled`), financial amounts (items, discount, shipping, seller payout), carrier details (carrier code, tracking code, label URL), and lifecycle timestamps (`DeliveredAt`, `CompletedAt`, `CancelledAt`).
- **Order**: Represents the aggregate customer checkout transaction. Tracks aggregate status (`PendingPayment`, `Paid`, `Delivered`, `Completed`, `Cancelled`) and total amount.
- **OrderStatusHistory**: Audit trail capturing every lifecycle transition, actor role (`Buyer`, `Seller`, `System`), reason notes, location, and courier/POD metadata.
- **OrderCancellation**: Formal cancellation record capturing actor, reason code, refund eligibility, and inventory release confirmation.
- **OrderRefund**: Financial reimbursement tracking refund type, amount, status (`Requested`, `Completed`), and transaction references.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of verified carrier delivery webhooks update sub-order state to `Delivered` without prematurely releasing escrow funds.
- **SC-002**: 100% of receipt confirmation attempts for un-delivered items are rejected, eliminating premature payout vulnerabilities.
- **SC-003**: Buyers can cancel individual sub-orders without cancelling prompt sibling shipments, maintaining a 100% refund accuracy rate for the cancelled shop's portion.
- **SC-004**: 100% of delivered sub-orders left unconfirmed after the inspection window automatically complete and disburse seller escrow payouts without manual intervention.
- **SC-005**: All inter-service calls between Commerce and Fulfillment operate synchronously via resilient typed clients with zero message broker or distributed saga dependencies.

---

## Assumptions

- **A-001**: Logistics tracking and courier webhook callbacks originate from the internal `FulfillmentService` gateway with pre-authenticated internal headers.
- **A-002**: Standard inspection grace period default is set to 3 days for production environments, with override capabilities in configuration for staging and evaluation environments.
- **A-003**: Proof of Delivery (POD) image assets are hosted and secured by the logistics provider/Fulfillment Service CDN, with URL links passed to CommerceService for reference.
- **A-004**: Cash on Delivery (COD) cash reconciliation between logistics couriers and merchants operates via courier settlement statements and does not debit platform escrow wallets.
