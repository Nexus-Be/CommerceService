# Quyết định Kiến trúc & Đối chiếu Liên Dịch Vụ (Gaps & Decisions)

> **Nguồn:** Trích xuất từ `Commerce_Function_API_Backlog.xlsx` (Sheet `Gaps_Decisions` & `SRS_Sync`).

| ID | Type | Issue | Proposal / options | Owner | Status |
| --- | --- | --- | --- | --- | --- |
| G01 | Decision | Decision resolved with Auction 2026-10-27: Commerce enforces the 24h deadline by job (AO-04) and emits OrderCancelled(PAYMENT_TIMEOUT). Auction computes paymentDeadlineAt = determinedAt + 24h and owns AUCTION_PAYMENT_DEADLINE_HOURS. Matches SRS OQ-01 recommendation. | Closed - no action; SRS section 4.5/5.7/7 must be updated (see SRS_Sync). | Backlog Lead + Auction lead | Resolved |
| G02 | Contract | Auction event that creates the order is AuctionSettled (emitted before payment); AuctionWon is ignored. Earlier plan AuctionOrderRequested is dropped. | Closed. Confirm OQ-22 (SETTLED state name) and OQ-23 (same eventId on retry) with Auction. | Commerce | Resolved |
| G03 | Contract | Order id goes back to Auction through OrderCreated with orderType=AUCTION. AuctionOrderCreated is not used. | Closed. | Commerce + Auction | Resolved |
| G04 | Decision | OQ-03: who deducts reputation on PAYMENT_TIMEOUT. Spec proposes User listens to OrderCancelled(PAYMENT_TIMEOUT) filtered by AUCTION and owns PAYMENT_TIMEOUT_PENALTY_SCORE; AuctionPaymentTimedOut is not used for penalties. Needs User to confirm. | Confirm with User; one owner only to avoid double penalty. | Auction + User | Open |
| G05 | Contract | Payment was split into Wallet. Functions.xlsx assumes POST /internal/v1/payments, GET payments?referenceId, PaymentSucceeded/Failed/Expired, RefundCompleted/Failed. | Adopt WAL-05A/B, WalletDebitedSuccess/Failed, WalletRefunded. Still needed from Wallet: payment-status lookup, refund-failed event, VNPAY success event. | Wallet | Open |
| G06 | Contract | Commerce expects all-or-nothing reserve for a whole group; Fulfillment FUL-06 reserves from an event per order. | Either Commerce emits one OrderCreated per order and treats reservation as async saga step, or Fulfillment adds a group-level API. Recommend async per order + saga compensation. | Fulfillment | Open |
| G07 | Contract | FUL-17 emits only ShipmentDelivered / DeliveryFailed. PickedUp, InTransit, Returned not emitted. | Fulfillment to emit them, or Commerce moves to SHIPPING from OrderSellerAccepted + first tracking event. | Fulfillment | Open |
| G08 | Design | Two entry points to complete an order: Commerce ORD-07 and Fulfillment FUL-23. | Keep ORD-07 as the only mutation; FUL-23 calls it (or emits event only). | Commerce + Fulfillment | Open |
| G09 | Contract | Functions.xlsx references Catalog POST /internal/v1/skus:batch-get; Catalog sheet defines CAT-39 and CAT-24. | Use CAT-39 for purchasability/price, CAT-24 for display data. | Catalog | Proposed fix |
| G10 | Naming | Error names follow SRS and Events spec section 5 (CHECKOUT_ALREADY_OPEN, INVALID_TRANSITION, DEADLINE_EXPIRED, IDEMPOTENCY_KEY_CONFLICT 409 ...). Mapping to Functions.xlsx names is in Error_Codes. | Closed in this workbook; update Functions.xlsx / API spec. | Commerce | Resolved |
| G11 | Contract | User endpoints referenced but not defined: status/trust check at checkout, batch name lookup. | Ask User service to expose them or fall back to cached names. | User | Open |
| G12 | Scope | Voucher management APIs are absent in Functions.xlsx; system SRS has no voucher section. | Add DSC-05..DSC-08 (PROPOSED); confirm with BA that vouchers are in MVP. | BA | Open |
| G13 | Contract | Event names: Notification and User listen to OrderSellerRejected / OrderSellerAccepted; Commerce publishes OrderCancelled(SELLER_REJECTED) and OrderConfirmed. InvoiceGenerated is not subscribed by Notification. | Tell Notification, User and Fulfillment the final names. | All services | Open |
| G14 | Design | CONFIRMED status exists in DB v2 but the system SRS state machine may not. | Keep CONFIRMED; update SRS. | BA | Proposed fix |
| G15 | Contract | orderType (REGULAR/AUCTION), auctionId, winningBidId are in the shared payload (spec 2.3). | Closed. | Commerce | Resolved |
| G16 | DB | auction_orders has no payment_attempt_count, so the 3-attempt limit cannot be enforced for auction orders. | Add column in DB v2.1 or accept unlimited retries until deadline. | Commerce DB | Open |
| G17 | Scope | Refund is full-amount only in MVP; partial refund of a group transaction is allowed by DB. | Keep MVP full only. | BA | Open |
| G18 | Decision | Do vouchers return when a buyer cancels after paying (OQ-09)? | Default: no return after PAID unless seller-caused. | BA | Open |
| G19 | DB | 'No open dispute' check for auto-complete has no data model. | Ignore until a dispute module exists. | BA | Open |
| G20 | Config | system_settings seed has 7 keys; PLATFORM_MARKUP_PERCENT, AUCTION_PLATFORM_FEE_PERCENT, ORDER_AUTO_COMPLETE_DAYS, etc. are not seeded. | Add seed rows before Wave 1. | Commerce DB | Open |
| G21 | Config | SYSTEM_TIMEZONE default is UTC (SRS COM-ORD-001, section 7). Asia/Ho_Chi_Minh was only a suggestion. | Use UTC unless BA decides otherwise. | Commerce | Proposed fix |
| G22 | Dependency | Not applicable: Commerce never calls Auction.CheckPaymentDeadline (spec 2.6). | None. | Commerce | Closed |
| G23 | Scope | Tax/VAT and guest cart: DB has no tax column and carts.buyer_id is NOT NULL (OQ-04, OQ-08). | No tax in MVP; guest cart on client, CART-10 optional. | BA | Open |
| G24 | Tech | Broker: Commerce sheet says Kafka or RabbitMQ; other services use RabbitMQ. | Use RabbitMQ with MassTransit (.NET) outbox. | Architect | Open |
| G25 | Behavior | ShipmentFailed handling: stay SHIPPING and alert Support (OQ-11). | Confirm with BA. | BA | Open |
| G26 | Ops | Job intervals/alerting channels are not defined (cart hourly vs 1-min jobs; refund reconciliation). | Define in ops spec; use Hangfire/Quartz with distributed lock in .NET. | Commerce | Open |
| G27 | Source | Sheet 'Error Codes' referenced by Functions.xlsx does not exist. | Error codes derived from the Commerce rows (see API_Catalog). | Commerce | Assumption |
| G28 | Source | Sheets 'Status & Formula' and 'Design Decisions' referenced but missing. | Derived from SRS_Commerce_Service_v1 + DB_Commerce_v2. | Commerce | Assumption |
| G29 | RBAC | Voucher privileges (DISCOUNT.*) are not in SRS 2.7. | Add DISCOUNT.CREATE/UPDATE/VIEW to RBAC matrix. | BA | Open |
| G32 | SRS sync | SRS_Commerce_Service_v1 is behind the agreed Events spec on the auction flow and several events. | Apply the items in sheet SRS_Sync. | BA | Open |
| G33 | Open (Auction) | OQ-22: SETTLED in Auction Flow4 is only an internal state name? OQ-23: does Auction keep the same eventId when retrying AuctionSettled? | Wait for Auction confirmation; unique auction_id already prevents a second order. | Auction | Open |
| G34 | Contract | Event names introduced here but not in SRS 5.2 (CheckoutCompleted/Expired/Cancelled, OrderShipped, OrderDelivered, InvoiceGenerated). | Add to SRS 5.2 after review (SRS 4.2 requires contract review) or drop. | BA + Commerce | Open |
| G35 | Conflict | Events spec API 13 lets a buyer cancel a single PENDING_PAYMENT order; SRS COM-CHK-010 cancels the whole group only. | SRS wins: use CHK-05 for unpaid orders. Update spec API 13. | Commerce | Open |
| G36 | Scope | 4 requirement IDs created in this workbook are not in the SRS: COM-ORD-013 (rating eligibility), COM-AUC-009 (AuctionCancelled handling), COM-PLT-010 (inbox retry), COM-RFD-006 (refund reconciliation alert). | Add them to SRS section 4 or remove the functions. | BA | Open |
| G30 | Contract | Adopting CloudEvents requires Auction, Wallet, Fulfillment, Notification, User and Catalog to use the same envelope, attributes and naming. | Propose one platform-wide event convention doc (this CloudEvents_Spec). Commerce dual-publishes legacy name for 1 release. | Architect / all leads | Open |
| G31 | Tech | Namespace (com.nexus), dataschema host and where JSON Schemas live are assumed. | Create contracts/ repo folder with JSON Schemas + CI compatibility check. | Architect | Open |
