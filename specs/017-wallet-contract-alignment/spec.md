# Feature Specification: WalletService Integration Contract Alignment & Direct VNPay Orchestration

**Feature Branch**: `017-wallet-contract-alignment`  
**Created**: 2026-11-10  
**Status**: Ready for Planning  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  
**Input**: User description: "Triển khai đồng bộ toàn diện giữa CommerceService và WalletService theo Hợp đồng tích hợp 2026-11-10: (1) Chuẩn hóa typed REST client IWalletClient/WalletClient truyền định danh x-user-id thay vì Bearer token cho endpoint thanh toán orders/{orderId}/payment và tra cứu số dư wallets/me; (2) Chuẩn hóa phân giải Problem Details RFC 9110 bắt chính xác errorCode INSUFFICIENT_BALANCE (HTTP 400), PAYMENT_ORDER_CONFLICT (HTTP 409), loại bỏ phụ thuộc header Idempotency-Key và dựa trên Business Key {orderId}/{subOrderId}; (3) Tích hợp luồng thanh toán trực tiếp VNPay (POST /api/v1/wallets/checkout/direct) lấy paymentUrl thực tế và thiết lập worker định kỳ thăm dò trạng thái (GET /api/v1/wallets/checkout/direct/{orderId}) để kích hoạt commit tồn kho khi thanh toán thành công; (4) Kết nối luồng hoàn tiền tự động và giải ngân Escrow khi đơn hàng hoàn thành/hủy bỏ; (5) Đảm bảo 100% tiếng Anh Sentence-case cho error messages, SCREAMING_SNAKE_CASE cho error code, biên dịch Release 0 error 0 warning và pass 100% test suite."

---

## 1. User Scenarios & Testing *(mandatory)*

### User Story 1 - Reliable Wallet Payment via Trusted Identity Propagation (Priority: P1)

As a Buyer checking out with my personal digital wallet balance, I want my payment transaction to be authenticated securely and processed instantly, so that my order transitions to `Paid` status immediately and my reserved items are guaranteed.

**Why this priority**:
Wallet payments are the primary internal payment method. If caller identity is not transmitted in accordance with the payment service's security boundary, transactions are rejected as unauthorized, completely blocking the checkout funnel.

**Independent Test**:
Can be independently tested by submitting an order checkout using `PaymentMethod.Wallet` as an authenticated buyer with sufficient wallet balance. Verify that the payment service receives the buyer's unique identifier, records a successful purchase debit, returns a transaction reference, and the order marks as `Paid` with stock committed.

**Acceptance Scenarios**:
1. **Given** an authenticated buyer with sufficient wallet balance, **When** placing an order using wallet payment, **Then** the system requests a purchase debit on the buyer's wallet using the order identifier as the idempotency key, records the transaction ID, and marks the order and all sub-orders as `Paid`.
2. **Given** a buyer with insufficient wallet balance, **When** attempting to pay, **Then** the payment service returns an insufficient balance response, and CommerceService keeps the order in `PendingPayment` without throwing unhandled server errors, allowing the buyer to top up or switch payment methods.
3. **Given** a temporary network timeout or server disruption during wallet debit, **When** the error is flagged as transient, **Then** the order remains in `PendingPayment` with reserved stock held, allowing reconciliation workers to resolve the final outcome safely.
4. **Given** an order that has already been paid, **When** a retry is received with identical parameters, **Then** the system idempotently recognizes the previous transaction and confirms the payment without creating duplicate debits.

---

### User Story 2 - Automated Seller Escrow Settlement & Order Cancellation Refunds (Priority: P1)

As a Seller completing an order shipment or a Buyer whose order was cancelled, I want financial payouts and refunds to be processed automatically and idempotently by the system, so that marketplace funds are disbursed accurately without manual administrative delays.

**Why this priority**:
Timely seller payouts and prompt customer refunds are critical for marketplace integrity and regulatory compliance. Manual processing causes operational bottlenecks and human error.

**Independent Test**:
1. Call receipt confirmation on an in-flight sub-order as the buyer. Verify that the sub-order transitions to `Completed` and triggers an automated settlement credit to the seller's wallet for the exact earnings amount.
2. Cancel a paid sub-order (or trigger seller rejection). Verify that the sub-order transitions to `Cancelled` and an automated refund credit is issued to the buyer's wallet.

**Acceptance Scenarios**:
1. **Given** a sub-order in `Delivered` status, **When** the buyer confirms receipt of goods, **Then** the sub-order marks as `Completed` with a completion timestamp, and an automated payout credit is released to the seller's wallet based on the sub-order ID.
2. **Given** a payout request that is retried due to a transient timeout, **When** the payout re-executes with the same seller and amount, **Then** the payment service returns the existing settlement record without double credit.
3. **Given** a seller rejects or cancels a paid sub-order, **When** cancellation processing runs, **Then** the system executes an automated refund to the buyer's wallet, records an approved refund record, and releases only the inventory associated with that specific sub-order.
4. **Given** order creation experiences a database failure after wallet funds have been debited, **When** the compensating transaction triggers, **Then** the system issues a full refund for the order and releases all reserved inventory.

---

### User Story 3 - Direct Gateway Checkout & Background Payment Reconciliation (Priority: P1)

As a Buyer who prefers paying directly via payment gateway (such as VNPay QR code or online banking), I want to receive a genuine, merchant-authorized payment link at checkout, and have my payment automatically detected in the background once completed, so that my order is finalized without depending on browser redirects.

**Why this priority**:
Customers who do not maintain a pre-funded wallet require direct payment gateway options. Without background polling and legitimate payment session initiation, direct gateway orders cannot be completed or verified.

**Independent Test**:
Submit an order with direct gateway payment. Verify that the system calls the payment service's direct checkout initiation endpoint, obtains a signed gateway payment URL, and returns it to the buyer. Then simulate gateway settlement and verify that the background polling worker detects the success status, updates the order to `Paid`, and commits reserved stock.

**Acceptance Scenarios**:
1. **Given** a buyer checking out with the direct gateway payment method, **When** the order is created, **Then** the system initiates a direct checkout session with the payment service and returns a valid payment gateway URL in the checkout response.
2. **Given** an order pending gateway payment, **When** the background reconciliation worker polls the payment service and detects payment success, **Then** the order status transitions to `Paid`, all sub-orders update to `Paid`, and inventory reservations are permanently committed.
3. **Given** a pending gateway order whose payment window (15 minutes) expires without successful payment, **When** the timeout worker processes the order, **Then** the order transitions to `Cancelled` and all reserved inventory is released back to stock.
4. **Given** a client attempting to forge payment confirmation via browser redirect parameters, **When** evaluating payment validity, **Then** the system ignores client redirect parameters and only accepts payment confirmation verified directly from the payment service.

---

### User Story 4 - Real-Time Wallet Balance In Checkout Quotation (Priority: P2)

As a Buyer reviewing my shopping cart quotation before placing an order, I want to see my actual current wallet balance, so that I immediately know whether I have enough balance to pay with my wallet or if I need to choose another payment method.

**Why this priority**:
Displaying an accurate wallet balance at the quotation stage improves user experience by setting clear expectations and preventing checkout abandonment.

**Independent Test**:
Call the checkout quotation endpoint as an authenticated buyer. Verify that the system retrieves the buyer's live balance from the wallet service and populates it accurately in the quotation response.

**Acceptance Scenarios**:
1. **Given** an authenticated buyer requesting a checkout quotation, **When** the quotation is calculated, **Then** the system queries the buyer's balance using their authenticated identity and displays the balance in the quote response.
2. **Given** the wallet service is temporarily unavailable during quotation calculation, **When** balance retrieval fails, **Then** the system logs a diagnostic warning and defaults the displayed balance to zero without blocking the customer from receiving their price quotation.

---

## 2. Edge Cases & Boundary Conditions

- **EC-001 (Duplicate Payment Attempt)**: An identical payment request is submitted twice due to connection lag.  
  *Mitigation*: The order identifier serves as the business key. Replays with identical parameters return the existing transaction record without duplicate charges.
- **EC-002 (Conflicting Payment Details)**: A payment retry is submitted with conflicting parameters (different amount or buyer).  
  *Mitigation*: The payment service returns a conflict error. CommerceService captures the conflict code and flags the order for audit reconciliation.
- **EC-003 (Excessive Refund Request)**: A refund request specifies an amount greater than the original order debit.  
  *Mitigation*: The payment service rejects the request with an excess refund error. CommerceService records a failed refund event with audit notes.
- **EC-004 (Untrusted Browser Callbacks)**: The user's browser returns to CommerceService with spoofed payment success query parameters.  
  *Mitigation*: CommerceService treats browser return URLs as purely informational and strictly relies on server-to-server status verification.

---

## 3. Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST authenticate wallet debit calls by propagating the buyer's unique identity header (`x-user-id`) to the payment service.
- **FR-002**: The system MUST parse standardized problem details error responses and distinctly identify insufficient balance conditions (`INSUFFICIENT_BALANCE` at HTTP 400) to keep the order in pending status without application failure.
- **FR-003**: The system MUST eliminate legacy idempotency header dependencies and anchor idempotency on order and sub-order business identifiers in the request route path.
- **FR-004**: The system MUST initiate direct payment gateway checkout sessions via the payment service when direct gateway payment is selected, obtaining a valid external payment URL.
- **FR-005**: The system MUST include the external payment gateway URL in the checkout response for direct gateway orders.
- **FR-006**: The system MUST periodically poll payment status for pending gateway orders to verify settlement asynchronously.
- **FR-007**: Upon confirming payment success from the payment service, the system MUST transition the order to `Paid` status and permanently commit the reserved inventory.
- **FR-008**: When a gateway order payment expires or fails permanently, the system MUST transition the order to `Cancelled` and release reserved inventory.
- **FR-009**: When a customer confirms delivery of goods, the system MUST automatically trigger revenue settlement for the corresponding seller using the sub-order identifier.
- **FR-010**: When an order or sub-order is cancelled after payment, the system MUST automatically execute a refund credit to the buyer's wallet.
- **FR-011**: The system MUST query the buyer's real-time wallet balance during checkout quotation using the buyer's authenticated identity.
- **FR-012**: All error messages across the payment integration path MUST strictly adhere to 100% English Sentence-case descriptions and `SCREAMING_SNAKE_CASE` error codes.

### Key Entities

- **Order**: Represents the master checkout transaction. Key attributes: `Id`, `BuyerId`, `Status`, `TotalAmount`, `PaymentMethod`, `PaymentDeadlineAt`, `WalletTransactionId`.
- **SubOrder**: Represents the seller-specific fulfillment unit. Key attributes: `Id`, `OrderId`, `SellerId`, `SellerReceiveAmount`, `Status`, `PaidAt`, `CompletedAt`.
- **OrderRefund**: Represents an automated or manual refund transaction. Key attributes: `Id`, `SubOrderId`, `OrderId`, `Amount`, `Status`, `ReasonCode`, `RequestedBy`.

---

## 4. Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of wallet payment and balance verification operations authenticate seamlessly without token-expiry or credential mismatches.
- **SC-002**: 100% of direct gateway checkout requests generate valid, merchant-authorized payment links without manual fallback interventions.
- **SC-003**: Direct gateway payments are automatically reconciled and confirmed in under 60 seconds from customer completion, committing reserved inventory without manual operations.
- **SC-004**: 100% of eligible order cancellations and receipt confirmations trigger corresponding refunds and seller disbursements automatically.
- **SC-005**: 100% of automated test scenarios for wallet payments, refunds, and gateway polling pass successfully.

---

## 5. Assumptions

- **A-001**: The payment service is operational and accessible at port 8008 on the internal container network.
- **A-002**: The API Gateway validates incoming client credentials and forwards the verified caller identifier via the `x-user-id` header.
- **A-003**: Payment gateway IPN notifications are received directly by the payment service, which maintains the authoritative payment status.
- **A-004**: Payment gateway sessions remain valid for a maximum of 15 minutes before expiration.
