# ĐẶC TẢ SỰ KIỆN (EVENTS), CẤU HÌNH (CONFIGS) VÀ GIAO TIẾP BẤT ĐỒNG BỘ – COMMERCE SERVICE

| Mục | Giá trị |
|---|---|
| Dự án / Service | Project Nexus – Commerce Service |
| Phiên bản | **2.1** (chuẩn hóa sau khi đối chiếu với team Auction) |
| Căn cứ | `SRS_Commerce_Service_v1.md` (nguồn chính), `Event_AuctionService.docx` (đối chiếu 2026-10-27), `Functions.xlsx`, schema `DB_Commerce_v2` |
| Ngôn ngữ | Tiếng Việt, giữ nguyên thuật ngữ và định danh tiếng Anh |

---

## 0. Cách đọc tài liệu

### 0.1 Thẻ độ tin cậy (giống SRS mục 0.2)

| Thẻ | Ý nghĩa | Agent/dev được phép |
|---|---|---|
| `[SRS]` | Có trong SRS Commerce v1 / SRS hệ thống | Coi là yêu cầu |
| `[DB]` | Có trong `DB_Commerce_v2` | Ràng buộc cứng, không vi phạm |
| `[DERIVED]` | Suy ra hợp lý từ `[SRS]` + `[DB]` | Dùng, ghi chú giả định |
| `[AGREED]` | Đã thống nhất với team liên quan (ghi rõ bên nào, ngày nào) | Coi là yêu cầu, chờ cập nhật SRS |
| `[PROPOSED]` | Đề xuất, chưa chốt | Dùng làm mặc định, đặt sau feature flag/interface |
| `[OPEN]` | Chưa có quyết định | **Không tự quyết**, để TODO kèm mã `OQ-xx` |

### 0.2 Thay đổi so với v2.0
1. Auction: Commerce nghe **`AuctionSettled`** để tạo `auction_orders`; `AuctionWon` bỏ qua `[AGREED Auction 2026-10-27]`.
2. `paymentDeadlineAt` và `skuId` của `AuctionSettled` là **bắt buộc**; thiếu thì từ chối. Bỏ giá trị dự phòng `settledAt + 24h`.
3. Hạn thanh toán xử lý bằng **job quét** (không dùng timer trong bộ nhớ); API `pay` chặn chính xác theo `payment_deadline_at`.
4. Sau `AuctionSettled` phiên không bị hủy `[AGREED]`; nếu `AuctionCancelled` vẫn đến cho phiên đã có đơn: chỉ ghi log và báo Support.
5. `OrderPaymentFailed`: Auction là consumer **tùy chọn**. Commerce không gọi `Auction.CheckPaymentDeadline`.
6. `AUCTION_PAYMENT_DEADLINE_HOURS` do **Auction sở hữu**; Commerce không giữ cấu hình này.
7. Thêm mục 2.6 (hợp đồng với Auction), mục 6 (nhật ký quyết định và câu hỏi mở), từ điển "settled".
8. Từ v2.0 giữ nguyên: bổ sung event còn thiếu, `orderType` = `REGULAR|AUCTION`, `reasonCode` đóng băng, Commerce không tự trừ/cộng điểm uy tín, `ShipmentFailed/Returned` xử lý thủ công.

### 0.3 Ranh giới (bounded context) `[SRS]`
Commerce sở hữu: giỏ hàng, checkout, đơn thường, đơn đấu giá, hủy đơn, refund (phía order), hóa đơn, mã giảm giá. **Không** sở hữu: ví/giao dịch/cổng thanh toán (Payment), tồn kho (Inventory), vận chuyển (Shipping), sản phẩm (Catalog), user/reputation (User), phiên đấu giá (Auction), thông báo (Notification).

```
  [ Client ] ──HTTPS──► [ API Gateway ] ──► [ Commerce Service ] ──► DB_Commerce_v2 (PostgreSQL)
                                                   │  ▲
                                          Outbox ──┘  └── Inbox
                                                   ▼  │
                                         [ Message Broker ]
        ┌──────────┬───────────────┬───────────────┬──────────────┬───────────────┐
        ▼          ▼               ▼               ▼              ▼               ▼
    Catalog   Fulfillment-    Fulfillment-      Payment         Auction     Notification / User
              Inventory       Shipping          (tách riêng)
```

### 0.4 Bất biến `[DB]`/`[SRS]`
1. `orders.total_amount = seller_receive_amount + platform_fee_amount + shipping_fee_amount`. `platform_fee_amount` có thể âm.
2. Đơn đấu giá: `shipping_fee_amount` và `total_amount` cùng NULL, hoặc `total_amount = winning_amount + shipping_fee_amount`.
3. Cross-service ref (`buyer_id`, `seller_id`, `product_id`, `sku_id`, `wallet_transaction_id`...) chỉ là UUID, **không FK**, không query DB service khác.
4. Event ghi vào `outbox_messages` **cùng transaction** với thay đổi dữ liệu; nhận event khử trùng bằng `inbox_messages (message_id, consumer)`; giao **at-least-once**.
5. Mọi cập nhật trạng thái dùng optimistic lock `row_version` và ghi `order_status_history`.
6. Lỗi Notification **không** được làm hỏng xử lý đơn `[SRS 5.2]`.

### 0.5 Từ điển thuật ngữ gây nhầm
| Thuật ngữ | Nghĩa trong Commerce/DB | Nghĩa trong Auction | Quy ước |
|---|---|---|---|
| "settled" / `settled_at` | Lúc **chốt người thắng**, mốc tính hạn thanh toán (comment DB `settled_at + 24h`) | Trạng thái `SETTLED` sau khi thanh toán khớp (Flow4) | Event `AuctionSettled` phát **ngay khi chốt người thắng**, **trước** thanh toán `[AGREED]`. Hạn thanh toán = `determinedAt + 24h` do Auction tính. Trạng thái `SETTLED` sau thanh toán là tên nội bộ của Auction `[OPEN]` OQ-22 (chờ xác nhận) |

---

## 1. Cấu hình

### 1.1 Gateway runtime (`gateway.runtime.json`) `[DERIVED]`

```json
{
  "Gateway": {
    "AuthMode": "Enforce",
    "Services": {
      "commerce": {
        "Mode": "Mock",
        "Address": "http://localhost:8003",
        "TimeoutSeconds": 5,
        "MockDelayMs": 0,
        "MockStatus": 200
      }
    }
  }
}
```

### 1.2 Tham số nghiệp vụ (`system_settings`)

| Khóa | Kiểu | Giá trị | Category | Trạng thái | Ghi chú |
|---|---|---|---|---|---|
| `CHECKOUT_TIMEOUT_MINUTES` | INTEGER | 15 | TIMING | Đã seed `[DB]` | Hạn thanh toán checkout. Mức 5 phút `[OPEN]` OQ-21, chưa được BA xác nhận |
| `SELLER_RESPOND_HOURS` | INTEGER | 24 | TIMING | Đã seed `[DB]` | |
| `ORDER_AUTO_COMPLETE_DAYS` | INTEGER | 7 | TIMING | Đã seed, **đề xuất, chờ BA** | |
| `CART_EXPIRATION_DAYS` | INTEGER | 7 | TIMING | Đã seed `[DB]` | |
| `PAYMENT_RETRY_LIMIT` | INTEGER | 3 | SYSTEM | Đã seed `[DB]` | |
| `MAX_CART_ITEMS` | INTEGER | 50 | SYSTEM | Đã seed `[DB]` | Số **dòng** |
| `MAX_QTY_PER_ITEM` | INTEGER | 99 | SYSTEM | Đã seed `[DB]` | |
| `MAX_ACTIVE_CART_PER_USER` | INTEGER | 1 | SYSTEM | Chưa seed; ép bằng unique index | |
| `FREE_SHIPPING_THRESHOLD` | DECIMAL | **`[OPEN]`** | MONEY | Chưa seed | SRS ghi `100`; đơn vị tiền chưa chốt (OQ-14) |
| `PLATFORM_MARKUP_PERCENT` | DECIMAL | **`[OPEN]`** | MONEY | Chưa seed, **chờ BA** | OQ-06 |
| `AUCTION_PLATFORM_FEE_PERCENT` | DECIMAL | **`[OPEN]`** | MONEY | Chưa seed, chờ BA | OQ-06 |
| `CHECKOUT_PRICE_REVALIDATION` | BOOLEAN | true | SYSTEM | Chưa seed `[SRS]` | |
| `ORDER_IDEMPOTENCY_ENABLED` | BOOLEAN | true | SYSTEM | Chưa seed `[SRS]` | |
| `FEATURE_AUCTION_ENABLED` | BOOLEAN | true | SYSTEM | `[SRS]` | Tắt thì ngừng nhận `AuctionSettled` |

**Không thuộc Commerce, không seed ở đây:**

| Khóa | Thuộc | Ghi chú |
|---|---|---|
| `AUCTION_PAYMENT_DEADLINE_HOURS` (24) | **Auction** `[AGREED]` | Auction tính hạn và gửi `paymentDeadlineAt`; Commerce dùng nguyên giá trị. SRS mục 7 đang liệt kê ở Commerce, cần sửa SRS cho khớp |
| `PAYMENT_TIMEOUT_PENALTY_SCORE` (10) | User/Reputation `[SRS]` | Ai trừ điểm: OQ-03 |
| `SELLER_CANCEL_PENALTY_SCORE`, `ORDER_COMPLETE_REPUTATION_BONUS` | Không có trong SRS | `[PROPOSED]`, thuộc User, Commerce không đọc |
| `PAYMENT_PROVIDER`, `PAYMENT_CALLBACK_TIMEOUT_MINUTES`, `PAYMENT_IDEMPOTENCY_WINDOW_MINUTES` | Payment | OQ-17 |
| `INVENTORY_RESERVATION_TIMEOUT_MINUTES` (30) | Inventory | Hạn giữ kho phải ≥ hạn thanh toán (OQ-07) |

---

## 2. Danh mục Event

### 2.1 Nguyên tắc `[SRS]`
- Phát qua Outbox, nhận qua Inbox, at-least-once, **consumer dedupe theo `eventId`**.
- Không đảm bảo thứ tự giữa các loại event: consumer so sánh `occurredAt` và trạng thái hiện tại.
- Thay đổi schema event phải qua review contract (SRS 4.2).
- Consumer gặp `reasonCode` **không biết** thì xử lý theo hướng an toàn: không phạt, ghi log.

### 2.2 Envelope chuẩn (map vào `outbox_messages`)

```json
{
  "eventId": "uuid-v4",
  "eventType": "OrderPaid",
  "aggregateType": "Order",
  "aggregateId": "uuid",
  "occurredAt": "2026-10-26T14:00:00.000Z",
  "headers": {
    "traceId": "trace-nexus-001",
    "correlationId": "checkout-group-uuid",
    "schemaVersion": "1.0"
  },
  "payload": { }
}
```

- `eventId` = `outbox_messages.id`. `aggregateType` ∈ `Cart` | `CheckoutGroup` | `Order` | `AuctionOrder` | `Refund`.
- `occurredAt`: ISO-8601 UTC.

### 2.3 Payload dùng chung cho `OrderCreated`, `OrderPaid`, `OrderPaymentFailed`, `OrderCancelled` `[AGREED Auction]`

| Trường | Điều kiện | Ghi chú |
|---|---|---|
| `orderType` | Luôn có | `REGULAR` hoặc `AUCTION`. **Bắt buộc**; Auction và User lọc theo field này |
| `orderId`, `orderNo` | Luôn có | `orders.id` hoặc `auction_orders.id` |
| `buyerId`, `sellerId`, `totalAmount` | Luôn có | Đơn đấu giá chưa báo ship: `totalAmount` có thể null |
| `paymentDeadlineAt` | Luôn có | |
| `checkoutGroupId` | Chỉ `REGULAR` | |
| `auctionId`, `winningBidId` | Chỉ `AUCTION` | Auction dùng để nối đơn với phiên |

Trường riêng theo event: `paidAt` (`OrderPaid`), `attemptCount` (`OrderPaymentFailed`), `cancelledAt` / `reasonCode` / `actorRole` / `wasPaid` (`OrderCancelled`).

---

### 2.4 Nhóm 1: Event Commerce phát ra (Outbox)

#### 2.4.1 Cart & Checkout `[SRS 2.8]`

| Event | Khi nào | Consumer | Payload tối thiểu |
|---|---|---|---|
| `CartCreated` | Tạo giỏ mới (COM-CART-001) | Notification, Event Log | `cartId`, `buyerId` |
| `CartItemAdded` | COM-CART-002 | Notification, Event Log | `cartId`, `cartItemId`, `productId`, `skuId`, `quantity` |
| `CartItemUpdated` | COM-CART-003 | Notification, Event Log | `cartId`, `cartItemId`, `quantity` |
| `CartItemRemoved` | COM-CART-004 / `ProductDeleted` | Notification, Event Log | `cartId`, `cartItemId` |
| `CartExpired` | Job CART-09 | Notification, Event Log | `cartId`, `buyerId`, `lastActivityAt` |
| `CheckoutStarted` | COM-CHK-001 | Event Log | `checkoutGroupId`, `cartId`, `buyerId`, `totalAmount` |

#### 2.4.2 `OrderCreated` `[SRS]`
- **Khi nào:** tạo `orders` (sau khi lưu `checkout_groups` + `orders`, `PENDING_PAYMENT`) hoặc `auction_orders` (từ `AuctionSettled`).
- **Consumer:** Notification, Event Log; **Auction** nếu `orderType = AUCTION` (đây là cách Auction nhận `orderId`, không có event mới) `[AGREED]`.
- **Payload (REGULAR):**
```json
{
  "orderType": "REGULAR",
  "checkoutGroupId": "uuid",
  "orderId": "uuid",
  "orderNo": "ORD-20261026-000001",
  "buyerId": "uuid",
  "sellerId": "uuid",
  "totalAmount": 255000.00,
  "paymentDeadlineAt": "2026-10-26T14:15:00Z",
  "items": [
    { "productId": "uuid", "skuId": "uuid", "quantity": 2, "unitPrice": 250000.00 }
  ]
}
```
- **Payload (AUCTION)** `[AGREED]`:
```json
{
  "orderType": "AUCTION",
  "orderId": "uuid",
  "orderNo": "AOR-20261026-000001",
  "auctionId": "uuid",
  "winningBidId": "uuid",
  "buyerId": "uuid",
  "sellerId": "uuid",
  "winningAmount": 1200000.00,
  "totalAmount": null,
  "paymentDeadlineAt": "2026-10-27T14:00:00Z"
}
```
Mã đơn theo định dạng DB: `ORD-YYYYMMDD-NNNNNN`, `AOR-YYYYMMDD-NNNNNN` (6 chữ số).

#### 2.4.3 `OrderPaid` `[SRS]`
- **Khi nào:** nhận kết quả thanh toán thành công, **đúng số tiền**, group còn `PAYMENT_PROCESSING`/`PENDING_PAYMENT`. Phát **cho từng order**.
- **Consumer:** Fulfillment (commit kho; tạo shipment theo OQ-20), Notification (buyer + seller), **Auction** (ghi thời điểm thanh toán, nếu `AUCTION`).
- **Payload:**
```json
{
  "orderType": "REGULAR",
  "checkoutGroupId": "uuid",
  "orderId": "uuid",
  "orderNo": "ORD-20261026-000001",
  "buyerId": "uuid",
  "sellerId": "uuid",
  "totalAmount": 255000.00,
  "paymentDeadlineAt": "2026-10-26T14:15:00Z",
  "paidAt": "2026-10-26T14:05:00Z",
  "walletTransactionId": "uuid",
  "sellerRespondDeadlineAt": "2026-10-27T14:05:00Z"
}
```
- Seller **chỉ thấy đơn đã thanh toán**, nên `OrderPaid` cũng là mốc đơn xuất hiện cho seller.
- Commerce **không** gọi `Auction.CheckPaymentDeadline` khi người thắng thanh toán, chỉ phát `OrderPaid` `[AGREED]`.

#### 2.4.4 `OrderPaymentFailed` `[SRS]`
- **Khi nào:** nhận kết quả thanh toán thất bại (COM-PAY-003). Đơn **không** được `PAID`.
- **Consumer:** Notification. **Auction: tùy chọn** (một lần thất bại, **chưa phải trạng thái cuối**; Auction đã xác nhận không cần nghe `[AGREED]`).
- **Payload:** payload chung + `attemptCount` (= `payment_attempt_count`) + `failureCode` `[OPEN]` (phụ thuộc contract Payment).
```json
{
  "orderType": "AUCTION",
  "orderId": "uuid",
  "orderNo": "AOR-20261026-000001",
  "auctionId": "uuid",
  "winningBidId": "uuid",
  "buyerId": "uuid",
  "sellerId": "uuid",
  "totalAmount": 1230000.00,
  "paymentDeadlineAt": "2026-10-27T14:00:00Z",
  "attemptCount": 1
}
```

#### 2.4.5 `OrderConfirmed` `[PROPOSED]` *(đổi tên từ `OrderSellerAccepted`)*
- **Khi nào:** seller xác nhận, `PAID → CONFIRMED` (COM-ORD-005), set `seller_accepted_at`.
- **Consumer:** Fulfillment-Shipping tạo shipment, Notification.
- **Lưu ý:** SRS 3.6.2.7 nói tạo shipment "sau khi đơn đã thanh toán"; tạo sau `CONFIRMED` là cách giải OQ-20, **chưa chốt** → đặt sau flag, để Shipping có thể chuyển sang nghe `OrderPaid`.
- **Payload:**
```json
{
  "orderType": "REGULAR",
  "orderId": "uuid",
  "orderNo": "ORD-20261026-000001",
  "buyerId": "uuid",
  "sellerId": "uuid",
  "sellerAcceptedAt": "2026-10-26T15:00:00Z",
  "shippingAddress": {
    "receiverName": "Nguyễn Văn A",
    "street": "123 Đường Lê Lợi",
    "ward": "Bến Nghé",
    "district": "Quận 1",
    "province": "Hồ Chí Minh",
    "ghnDistrictId": 1442,
    "ghnWardCode": "20101"
  },
  "contactPhone": "0912345678"
}
```

#### 2.4.6 `OrderCancelled` `[SRS]`
- **Khi nào:** COM-CHK-008 (checkout hết hạn), COM-AUC-006 (đấu giá quá hạn), COM-ORD-006/007 (seller từ chối / không phản hồi), COM-CNL (buyer/admin/support hủy). Trong cùng transaction ghi `order_cancellations`, history, saga.
- **Consumer:** Notification; Fulfillment (release kho); **Auction** (nếu `AUCTION`: `PAYMENT_TIMEOUT` thì phát `AuctionPaymentTimedOut`); User (reputation, nếu bên đó được chọn, OQ-03).
- **Voucher:** release/giữ lượt dùng là xử lý **nội bộ** Commerce, **không** qua event (OQ-09 cho đơn đã trả).
- **Payload:** payload chung + `cancelledAt`, `reasonCode`, `actorRole`, `wasPaid`, `reasonNote` (tùy chọn).
```json
{
  "orderType": "REGULAR",
  "checkoutGroupId": "uuid",
  "orderId": "uuid",
  "orderNo": "ORD-20261026-000001",
  "buyerId": "uuid",
  "sellerId": "uuid",
  "totalAmount": 255000.00,
  "paymentDeadlineAt": "2026-10-26T14:15:00Z",
  "wasPaid": true,
  "reasonCode": "SELLER_REJECTED",
  "actorRole": "SELLER",
  "cancelledAt": "2026-10-26T15:30:00Z"
}
```

**`reasonCode` đóng băng** `[AGREED Auction]` (chờ ký contract chung, OQ-18):

| `reasonCode` | `actorRole` điển hình | Ý nghĩa | Lỗi của buyer? |
|---|---|---|---|
| `PAYMENT_TIMEOUT` | `SYSTEM` | Quá hạn thanh toán (checkout hoặc đơn đấu giá); `wasPaid = false` | **Có** |
| `BUYER_CANCELLED` | `BUYER` | Buyer tự hủy | Tùy chính sách |
| `SELLER_REJECTED` | `SELLER` | Seller từ chối đơn đã trả | Không |
| `SELLER_NO_RESPONSE` | `SYSTEM` | Quá `SELLER_RESPOND_HOURS` | Không |
| `ADMIN_CANCELLED` | `ADMIN` / `SUPPORT_STAFF` | Admin/Support hủy | Không (xem xét thủ công) |

`actorRole` ∈ `BUYER`, `SELLER`, `ADMIN`, `SUPPORT_STAFF`, `SYSTEM`. Refund bổ sung `LATE_PAYMENT`, `AMOUNT_MISMATCH` (**không** xuất hiện trong `OrderCancelled`). Lý do chi tiết do người dùng nhập đi vào **`reasonNote`**, không đổi `reasonCode`.

#### 2.4.7 `OrderRefundRequested` `[SRS]`
- **Khi nào:** (a) hủy đơn `was_paid = true` (COM-CNL-004); (b) thanh toán trễ/lệch tiền (COM-PAY-004); (c) refund thủ công Admin/Support (COM-RFD-005, ORD-13).
- **Consumer:** **Payment**. Ghi `order_refunds` và outbox trong **cùng transaction**; `outbox_messages.id` lưu vào `order_refunds.request_event_id`.
- **Payload:** đúng **một** trong `orderId` / `auctionOrderId` / `checkoutGroupId`.
```json
{
  "refundId": "uuid",
  "refundType": "FULL",
  "amount": 255000.00,
  "orderId": "uuid",
  "auctionOrderId": null,
  "checkoutGroupId": null,
  "originalWalletTransactionId": "uuid",
  "reasonCode": "SELLER_REJECTED",
  "actorRole": "SYSTEM",
  "requestedBy": null,
  "requestedAt": "2026-10-26T15:30:01Z"
}
```
- Refund một order trong group = hoàn **một phần** giao dịch của group (`refundType = PARTIAL`).
- Service phải tự kiểm tra (DB không ép): tổng refund ≤ số tiền đã trả; không vừa refund cả group vừa refund từng order.

#### 2.4.8 `OrderRefundCompleted` `[SRS]`
- **Khi nào:** nhận kết quả refund thành công (COM-RFD-003). **Consumer:** Notification.
- **Payload:**
```json
{
  "refundId": "uuid",
  "orderId": "uuid",
  "refundTransactionId": "uuid",
  "amount": 255000.00,
  "completedAt": "2026-10-26T15:35:00Z"
}
```

#### 2.4.9 `OrderCompleted` `[SRS]`
- **Khi nào:** job ORD-08 chuyển `DELIVERED → COMPLETED`. Buyer xác nhận nhận hàng sớm: `[OPEN]` OQ-11.
- **Consumer:** Notification; User (mở quyền rating `[SRS 3.1.2.1]`).
- **Không gán cho `OrderCompleted`** (chưa có trong SRS): giải ngân cho seller (Payment) `[OPEN]`; cộng điểm uy tín `[PROPOSED]` thuộc User; phát hành hóa đơn (nội bộ, OQ-15).
- **Payload:**
```json
{
  "orderType": "REGULAR",
  "orderId": "uuid",
  "orderNo": "ORD-20261026-000001",
  "buyerId": "uuid",
  "sellerId": "uuid",
  "completedAt": "2026-11-02T16:00:00Z"
}
```

---

### 2.5 Nhóm 2: Event Commerce tiếp nhận (Inbox)

Mọi xử lý: kiểm tra `inbox_messages (message_id, consumer)` → xử lý → đánh dấu `PROCESSED` **cùng transaction**; trùng thì `SKIPPED`.

| Event nhận | Nguồn | Trạng thái contract | Xử lý của Commerce |
|---|---|---|---|
| Kết quả thanh toán **thành công** (tên tạm: `PaymentSucceeded`) | Payment | **`[OPEN]`** OQ-17 | Xem 2.5.1 |
| Kết quả thanh toán **thất bại** (tạm: `PaymentFailed`) | Payment | `[OPEN]` OQ-17 | Xem 2.5.2 |
| Kết quả refund thành công (tạm: `RefundCompleted`) | Payment | `[OPEN]` | `order_refunds` → `COMPLETED` (ghi `refund_transaction_id`, `completed_at`); order → `REFUNDED`; saga `COMPLETED`; phát `OrderRefundCompleted`. Idempotent theo `refund_transaction_id` unique |
| `RefundFailed` | Payment | Tên theo comment DB | `order_refunds` → `FAILED` (`failure_code`, `failed_at`); saga `FAILED`; Support đối soát thủ công; được tạo refund mới |
| `InventoryReserved` | Inventory `[SRS]` | Sync/event: OQ-07 | Bước CheckoutSaga: `INVENTORY_RESERVING → AWAITING_PAYMENT` |
| `InventoryReservationFailed` | Inventory | `[PROPOSED]`, không có trong SRS | Bù trừ: hủy order, release voucher, trả `INSUFFICIENT_STOCK` |
| `InventoryReleased` | Inventory `[SRS]` | | Đặt `order_cancellations.inventory_released = true` |
| `InventoryReservationExpired` | Inventory `[SRS]` | | Lưới an toàn: chỉ khi group **vẫn** `PENDING_PAYMENT` thì hủy; nếu đã `PAID` thì bỏ qua và cảnh báo |
| `ShipmentPickedUp` | Shipping `[SRS]` | | `CONFIRMED → SHIPPING`, set `shipped_at` |
| `ShipmentInTransit` | Shipping `[SRS]` | | Ghi nhận, không đổi trạng thái |
| `ShipmentDelivered` | Shipping `[SRS]` | | `SHIPPING → DELIVERED`, set `delivered_at` (job ORD-08 tự hoàn tất, **không dùng timer riêng**) |
| `ShipmentFailed` / `ShipmentReturned` | Shipping `[SRS]` | **`[OPEN]`** OQ-11 | Chỉ ghi log + báo Support, **không tự đổi trạng thái, không tự refund** |
| `ProductDeleted` | Catalog `[SRS]` | | Xóa `cart_items` theo `product_id`, tính lại tổng, phát `CartItemRemoved` |
| `ProductStatusChanged` | Catalog `[SRS]` | | Không sửa giỏ; chỉ chặn ở validate (COM-CART-006) `[PROPOSED]` |
| **`AuctionSettled`** | Auction | **`[AGREED Auction 2026-10-27]`** | Tạo `auction_orders` (xem 2.5.3) |
| `AuctionWon` | Auction | `[AGREED]` | **Bỏ qua** (Auction dùng cho Notification) |
| `AuctionCancelled` | Auction `[SRS]` | `[AGREED]` | Phiên chưa có đơn: bỏ qua. Phiên **đã có đơn**: chỉ ghi log + báo Support, không tự đổi trạng thái đơn (không mong đợi xảy ra) |

Mọi tên event của Payment đi qua **lớp adapter** để đổi tên khi contract được chốt.

#### 2.5.1 Xử lý kết quả thanh toán thành công `[SRS COM-PAY-002/004]`
Một transaction, theo thứ tự:
1. Tìm group (hoặc `auction_order`) theo `reference`.
2. **Nhánh bình thường:** group đang `PAYMENT_PROCESSING`/`PENDING_PAYMENT` **và** `amount == total_amount`:
   - group → `PAID` (ghi `wallet_transaction_id`); mọi order → `PAID` (`paid_at`, `seller_respond_deadline_at = paid_at + SELLER_RESPOND_HOURS`);
   - voucher `RESERVED → USED`, `used_count + 1`, `reserved_count − 1`;
   - saga → `COMPLETED`; commit kho; cart → `CHECKED_OUT` `[OPEN]` OQ-10;
   - ghi `order_status_history`; phát `OrderPaid` cho từng order.
3. **Nhánh trễ / lệch tiền:** group đã `EXPIRED`/`CANCELLED`, hoặc `amount != total_amount`:
   - **không** đánh dấu `PAID`;
   - tạo `order_refunds` `FULL` theo `checkout_group_id` (`reason_code` = `LATE_PAYMENT` hoặc `AMOUNT_MISMATCH`), `wallet_transaction_id` = giao dịch gốc;
   - phát `OrderRefundRequested`. Chống trùng nhờ unique `uq_order_refunds_group_active` và Inbox.
   - Khoảng ân hạn cho kết quả đến muộn sát hạn: `[OPEN]` (quyết định nghiệp vụ).
4. Kết quả trùng (đã `PAID`): không đổi gì thêm (idempotent).

#### 2.5.2 Xử lý kết quả thanh toán thất bại `[SRS COM-PAY-003]`
- Đơn **không** được `PAID`.
- **Không** tăng `payment_attempt_count` ở đây (đã tăng lúc khởi tạo thanh toán, COM-PAY-001). Tăng ở hai chỗ sẽ đếm đôi.
- Nếu `payment_attempt_count < PAYMENT_RETRY_LIMIT`: group `PAYMENT_PROCESSING → PENDING_PAYMENT` để thử lại (vẫn bị chặn bởi `payment_deadline_at`).
- Hết lượt `[PROPOSED]`: từ chối lần thử mới bằng lỗi nghiệp vụ; group giữ `PENDING_PAYMENT` đến khi hết hạn.
- Phát `OrderPaymentFailed` (kèm `attemptCount`).

#### 2.5.3 Xử lý `AuctionSettled` `[AGREED Auction]`
Validate trước khi tạo đơn: `auctionId`, `buyerId`, `sellerId`, `productId`, **`skuId`**, `winningBidId`, `finalPriceAmount`, `determinedAt`, **`paymentDeadlineAt`** đều **bắt buộc**. Thiếu field bắt buộc → từ chối, ghi `inbox_messages.status = FAILED` + `last_error`, cảnh báo; **không** tự tính giá trị thay thế.

1. Inbox khử trùng; ngoài ra unique `auction_id` bảo đảm **1 auction = 1 đơn**: gửi trùng thì trả lại cùng `orderId`, không tạo đơn thứ hai.
2. Tạo `auction_orders` theo ánh xạ:

| Field `AuctionSettled` | Cột `auction_orders` |
|---|---|
| `auctionId` | `auction_id` (unique) |
| `winningBidId` | `winning_bid_id` |
| `buyerId` | `buyer_id` |
| `sellerId` | `seller_id` |
| `productId`, `skuId` | `product_id`, `sku_id` (DB cho phép NULL, consumer yêu cầu khác NULL) |
| `productName` | `product_name_snapshot` |
| `finalPriceAmount` | `winning_amount` (`> 0`) |
| `paymentDeadlineAt` | `payment_deadline_at` (dùng **nguyên giá trị**) |
| `determinedAt` | Ghi vào `order_status_history`/log, không có cột riêng |

3. `status = PENDING_PAYMENT`, `payment_method = WALLET`. Ảnh và `sku_attributes_snapshot` lấy từ Catalog lúc tạo.
4. `shipping_address`, `contact_phone`, `shipping_fee_amount`, `total_amount` = NULL cho đến khi winner nhập địa chỉ.
5. `seller_receive_amount`/`platform_fee_amount` theo `AUCTION_PLATFORM_FEE_PERCENT` `[OPEN]` OQ-06.
6. Phát `OrderCreated` (`orderType = AUCTION`) để Auction nhận `orderId`.

---

### 2.6 Hợp đồng với Auction `[AGREED]` (trừ các mục `[OPEN]`)

**Auction phát, Commerce nghe:** `AuctionSettled` (tạo đơn). Commerce **không** nghe `AuctionWon`, `AuctionCreated`, `AuctionStarted`, `BidPlaced`, `Outbid`, `AuctionEnded`, `AuctionFailed`.

**Commerce phát, Auction nghe** (chỉ xử lý `orderType = AUCTION`):

| Event | Auction xử lý | Field Auction cần |
|---|---|---|
| `OrderCreated` | Lưu `orderId` theo `auctionId` | `auctionId`, `orderId`, `orderNo`, `orderType`, `paymentDeadlineAt`, `winningBidId` |
| `OrderPaid` | Ghi thời điểm thanh toán | `auctionId`, `orderId`, `orderType`, `paidAt`, `winningBidId` |
| `OrderCancelled` | `PAYMENT_TIMEOUT` → phát `AuctionPaymentTimedOut`. Lý do khác: Auction tự quyết cách xử lý, Commerce không cần làm gì thêm. `reasonCode` lạ: không phạt | `auctionId`, `orderId`, `orderType`, `buyerId`, `wasPaid`, `reasonCode`, `actorRole`, `cancelledAt`, `paymentDeadlineAt` |

**Quy tắc đã chốt:**
1. Auction tính `paymentDeadlineAt = determinedAt + 24h` và luôn gửi trong `AuctionSettled`; Commerce dùng nguyên giá trị.
2. **Commerce thực thi hạn thanh toán** bằng job quét (job AUC-PAY, mục 3.4) rồi phát `OrderCancelled(PAYMENT_TIMEOUT, SYSTEM, wasPaid=false)`. Auction **không** đặt timer để hủy.
3. Commerce **không** gọi `Auction.CheckPaymentDeadline`; chỉ phát `OrderPaid`. Auction bỏ API này khỏi hợp đồng.
4. `AuctionPaymentTimedOut` là event của Auction, **không dùng để trừ uy tín**.
5. Sau `AuctionSettled` phiên không bị hủy.
6. Auction chống trùng theo `eventId`; so `occurredAt` và trạng thái hiện tại.
7. `AUCTION_PAYMENT_DEADLINE_HOURS` do Auction sở hữu.

**Còn mở với Auction:**
- OQ-03: ai trừ điểm uy tín khi `PAYMENT_TIMEOUT`.
- OQ-22: `SETTLED` ở Flow4 chỉ là tên trạng thái nội bộ, không phải thời điểm phát `AuctionSettled`?
- OQ-23: Auction có giữ **cùng `eventId`** khi retry `AuctionSettled` không? (Inbox của Commerce khử trùng theo `eventId`; unique `auction_id` vẫn chặn đơn thứ hai.)

---

## 3. Giao tiếp bất đồng bộ, Saga và Job nền

### 3.1 CheckoutSaga `[SRS]`

Trạng thái: `INVENTORY_RESERVING → AWAITING_PAYMENT → PAYMENT_PENDING → COMPLETED`; lỗi → `COMPENSATED`. `correlation_id = checkout_group_id`.

```mermaid
sequenceDiagram
    autonumber
    actor Buyer
    participant GW as API Gateway
    participant COM as Commerce
    participant INV as Inventory
    participant PAY as Payment
    participant NOTI as Notification

    Buyer->>GW: POST /api/v1/checkout (Idempotency-Key)
    GW->>COM: CreateCheckout
    COM->>COM: TX: tạo CheckoutGroup + Orders + saga INVENTORY_RESERVING + outbox OrderCreated
    COM->>INV: Reserve inventory + voucher RESERVED (cơ chế sync/event: OQ-07)
    alt Reserve thất bại
        INV-->>COM: InventoryReservationFailed [PROPOSED]
        COM->>COM: Compensate: hủy order, release voucher, saga COMPENSATED
        COM-->>GW: 409 INSUFFICIENT_STOCK
    else Reserve thành công
        INV-->>COM: InventoryReserved
        COM->>COM: saga AWAITING_PAYMENT
        COM-->>GW: 201 Created (checkoutGroupId)
    end

    Buyer->>GW: POST /api/v1/checkout-groups/{id}/pay
    GW->>COM: InitiatePayment
    COM->>COM: Kiểm tra chưa quá hạn; TX: attempt+1, group PAYMENT_PROCESSING, saga PAYMENT_PENDING
    COM->>PAY: Yêu cầu thanh toán (reference=checkoutGroupId, amount, idempotency key)
    COM-->>GW: 200 status=PAYMENT_PROCESSING
    alt Thành công, đúng tiền, còn hạn
        PAY-->>COM: Kết quả thanh toán thành công
        COM->>COM: TX: group+orders PAID, voucher USED, saga COMPLETED, outbox OrderPaid
        COM-->>NOTI: OrderPaid (qua broker)
    else Thất bại
        PAY-->>COM: Kết quả thanh toán thất bại
        COM->>COM: group PENDING_PAYMENT (nếu còn lượt), outbox OrderPaymentFailed
    else Trễ hạn / lệch tiền
        PAY-->>COM: Kết quả thanh toán thành công
        COM->>COM: Không PAID; tạo order_refunds FULL; outbox OrderRefundRequested
    end
    Note over COM: Quá payment_deadline_at: job CHK-06 → EXPIRED, hủy order, release, saga COMPENSATED, outbox OrderCancelled
```

Commerce **không** gọi cổng thanh toán, không giữ ví/số dư, không xử lý callback gateway. Gateway không gọi thẳng Payment thay Commerce.

### 3.2 OrderCancellationSaga `[SRS]`
`CANCELLING → REFUND_PENDING → COMPLETED`; refund lỗi → `FAILED`. `correlation_id = order_id` hoặc `auction_order_id`. Đơn chưa trả: `CANCELLING → COMPLETED`, không refund.

### 3.3 State machine đơn hàng `[PROPOSED]` (DB chỉ enforce tập giá trị, code phải enforce)

```
 PENDING_PAYMENT ──(quá hạn / hủy tay, was_paid=false)──────────► CANCELLED
        │ (thanh toán đúng tiền)
        ▼
      PAID ──(seller từ chối / quá hạn phản hồi / buyer, admin hủy, was_paid=true)──► CANCELLED
        │ (seller xác nhận)                                              │ (refund COMPLETED)
        ▼                                                                ▼
   CONFIRMED ──(admin/support hủy)──► CANCELLED                      REFUNDED
        │ (ShipmentPickedUp)
        ▼
    SHIPPING ──(ShipmentFailed/Returned)──► [OPEN] xử lý thủ công (OQ-11)
        │ (ShipmentDelivered)
        ▼
    DELIVERED ──(job: delivered_at + ORDER_AUTO_COMPLETE_DAYS)──► COMPLETED
```

Refund thủ công `DELIVERED/COMPLETED → REFUNDED` bởi Admin/Support: `[OPEN]` OQ-11.
Mỗi transition: update với `row_version` + insert `order_status_history` + insert `outbox_messages`, **một transaction**. Transition ngoài bảng → `INVALID_TRANSITION`; xung đột `row_version` → `CONCURRENT_UPDATE`.

### 3.4 Job nền `[SRS 4.10]`

**Quyết định:** hạn thanh toán xử lý bằng **job quét**, không dùng timer trong bộ nhớ (mất khi restart, dễ hủy trùng khi nhiều instance) `[AGREED Auction cho đơn đấu giá]`. Hạn vẫn được chặn **chính xác từng giây** ở API `pay` (COM-PAY-001): quá `payment_deadline_at` thì trả `DEADLINE_EXPIRED` dù job chưa chạy; job chỉ dọn dẹp (hủy, nhả voucher/kho, phát event). Chu kỳ chưa được BA/DevOps chốt (OQ-19): cột "Chu kỳ đề xuất" là `[PROPOSED]`. Mọi job quét dùng `FOR UPDATE SKIP LOCKED` để chạy an toàn nhiều instance.

| Job | Điều kiện quét | Hành động | Index | Chu kỳ đề xuất |
|---|---|---|---|---|
| **CHK-06** Hết hạn checkout | `checkout_groups.status='PENDING_PAYMENT'` AND `payment_deadline_at < now` | group `EXPIRED`; order `CANCELLED` (`PAYMENT_TIMEOUT`, `SYSTEM`, `was_paid=false`); release voucher + kho; saga `COMPENSATED`; `OrderCancelled` | `idx_checkout_groups_expire` | **15 giây** (hạn ngắn, partial index nhẹ) |
| **AUC-PAY** Quá hạn thanh toán đấu giá | `auction_orders.status='PENDING_PAYMENT'` AND `payment_deadline_at < now` | `CANCELLED`, `PAYMENT_TIMEOUT`, `SYSTEM`, `was_paid=false`; `order_cancellations` + history; `OrderCancelled` | `idx_auction_orders_payment_deadline` | 1 phút |
| **ORD-09** Seller quá hạn phản hồi | `status='PAID'` AND `seller_respond_deadline_at < now`, **cả `orders` và `auction_orders`** | `CANCELLED`, `SELLER_NO_RESPONSE`, `SYSTEM`, `was_paid=true`; refund; `OrderCancelled` | `idx_orders_seller_respond`, `idx_auction_orders_seller_respond` | 1 phút |
| **ORD-08** Tự hoàn tất | `status='DELIVERED'` AND `delivered_at < now − ORDER_AUTO_COMPLETE_DAYS`, **cả hai bảng** | `COMPLETED`, set `completed_at`; `OrderCompleted` | `idx_orders_auto_complete`, `idx_auction_orders_auto_complete` | 1 giờ |
| **CART-09** Hết hạn giỏ | `carts.status='ACTIVE'` AND `deleted_at IS NULL` AND `last_activity_at < now − CART_EXPIRATION_DAYS` | `ABANDONED`; `CartExpired` | `idx_carts_abandon` | 1 giờ |
| **ORD-20** Outbox publisher | `outbox_messages.status='PENDING'` | Publish; `PUBLISHED`+`published_at`; lỗi: `retry_count+1`, `last_error`; vượt ngưỡng `FAILED`/`DEAD` (ngưỡng `[OPEN]`) | `idx_outbox_pending` | ~1 giây, batch 100 |
| Dọn outbox | `status='PUBLISHED'` cũ | Xóa/archive | `idx_outbox_cleanup` | `[OPEN]` |
| Retry inbox | `status IN ('RECEIVED','FAILED')` | Xử lý lại | `idx_inbox_retry` | `[OPEN]` |
| **ORD-21** Saga treo | `current_state` không thuộc (`COMPLETED`,`COMPENSATED`,`FAILED`) AND `updated_at` quá ngưỡng | Retry hoặc cảnh báo | `idx_saga_state_stuck` | 2 phút; ngưỡng treo đề xuất 5 phút `[OPEN]` |
| **ORD-19** Dọn idempotency | `idempotency_keys.expires_at < now` | Xóa | `idx_idempotency_expires` | `[OPEN]` (TTL key đề xuất 24 giờ, OQ-16) |
| Đối soát refund | `order_refunds.status IN ('REQUESTED','FAILED')` quá lâu | Báo Support | `idx_order_refunds_pending` | `[OPEN]` |
| Hết hạn voucher | `discount_codes.ends_at < now` | `EXPIRED` | `idx_discount_codes_available` | `[PROPOSED]` |

---

## 4. Hợp đồng API đồng bộ (Luồng F1)

> Đường dẫn REST **thuộc API spec** (`Functions.xlsx`), không nằm trong SRS. Các path dưới đây là hợp đồng mock hiện hành `[DERIVED]`; nếu lệch API spec thì API spec thắng.

Mọi response bọc `ApiResponse<T>`:
```json
{ "msg": "Mô tả bằng tiếng Anh, Sentence case", "data": { } }
```
Tiền: `numeric(18,2)`. Quyền theo SRS 2.4: Buyer thao tác giỏ/checkout/hủy; Seller chỉ thấy đơn đã thanh toán.

### 4.1 Giỏ hàng (Cart)

| # | Endpoint | Mô tả | Ghi chú |
|---|---|---|---|
| 1 | `GET /api/v1/carts/me` | Xem giỏ, nhóm theo seller | COM-CART-005; thuế: OQ-08 |
| 2 | `POST /api/v1/carts/me/items` | Thêm item (cộng dồn nếu trùng `skuId`) | COM-CART-002 |
| 3 | `PATCH /api/v1/carts/me/items/{cartItemId}` | Sửa số lượng (`> 0`) | COM-CART-003 |
| 4 | `DELETE /api/v1/carts/me/items/{cartItemId}` | Xóa item | COM-CART-004 |
| 5 | `POST /api/v1/carts/me/items:bulk-remove` | Xóa nhiều / toàn bộ | |
| 6 | `POST /api/v1/carts/me/validate` | Validate trước checkout | COM-CART-006 |

**API 1: `GET /api/v1/carts/me`** (200)
```json
{
  "msg": "Cart retrieved successfully",
  "data": {
    "cartId": "33333333-3333-3333-3333-000000000001",
    "status": "ACTIVE",
    "itemCount": 2,
    "subtotalAmount": 700000.00,
    "lastActivityAt": "2026-10-26T10:00:00Z",
    "groups": [
      {
        "sellerId": "22222222-2222-2222-2222-000000000001",
        "sellerName": "Nexus Fashion Shop",
        "items": [
          {
            "cartItemId": "44444444-4444-4444-4444-000000000001",
            "productId": "55555555-5555-5555-5555-000000000001",
            "skuId": "66666666-6666-6666-6666-000000000001",
            "productName": "Áo sơ mi Oxford",
            "skuAttributes": { "Color": "Trắng", "Size": "L" },
            "imageUrl": "https://cdn.nexus.local/p/oxford.jpg",
            "quantity": 1,
            "unitPriceSnapshot": 250000.00,
            "currentUnitPrice": 250000.00,
            "lineTotal": 250000.00,
            "priceChanged": false,
            "stockAvailable": 50,
            "itemStatus": "OK"
          }
        ]
      }
    ],
    "warnings": []
  }
}
```
`itemCount` = số **dòng** (không phải tổng quantity) `[DB]`. Giá chốt cuối luôn tính lại khi checkout.

**API 2: `POST /api/v1/carts/me/items`**
Request: `{ "productId": "...", "skuId": "...", "quantity": 2 }`
Response 200:
```json
{
  "msg": "Item added to cart successfully",
  "data": {
    "cartItemId": "44444444-4444-4444-4444-000000000001",
    "cartId": "33333333-3333-3333-3333-000000000001",
    "quantity": 2,
    "unitPriceSnapshot": 250000.00,
    "lineTotal": 500000.00,
    "cart": { "itemCount": 2, "subtotalAmount": 950000.00 }
  }
}
```
Validate: `1 ≤ quantity ≤ MAX_QTY_PER_ITEM`, số dòng ≤ `MAX_CART_ITEMS`, product/SKU ACTIVE. Yêu cầu đăng nhập (guest cart: OQ-04).

**API 3: `PATCH .../items/{cartItemId}`**: request `{ "quantity": 3 }`; response `{ cartItemId, quantity, unitPrice, lineTotal, cart: { itemCount, subtotalAmount } }`.

**API 4: `DELETE .../items/{cartItemId}`**: response `{ removedCartItemId, cart: { itemCount, subtotalAmount } }`.

**API 5: `POST .../items:bulk-remove`**: request `{ "all": false, "cartItemIds": ["..."] }`; response `{ removedCount, cart: { itemCount, subtotalAmount } }`.

**API 6: `POST .../validate`**: request `{ "cartItemIds": ["...", "..."] }`; response:
```json
{
  "msg": "Cart items validated successfully",
  "data": {
    "valid": true,
    "items": [
      {
        "cartItemId": "44444444-4444-4444-4444-000000000001",
        "skuId": "66666666-6666-6666-6666-000000000001",
        "currentUnitPrice": 250000.00,
        "availableQty": 50,
        "issues": []
      }
    ],
    "subtotalAmount": 700000.00
  }
}
```
Giá đổi + `CHECKOUT_PRICE_REVALIDATION = true`: trả `PRICE_CHANGED` kèm giá mới để buyer xác nhận `[PROPOSED]`.

### 4.2 Checkout & Thanh toán

| # | Endpoint | Mô tả |
|---|---|---|
| 7 | `POST /api/v1/checkout/preview` | Xem trước giá, ship, voucher |
| 8 | `POST /api/v1/checkout` | Tạo checkout group + orders (**idempotent**) |
| 9 | `POST /api/v1/checkout-groups/{checkoutGroupId}/pay` | Khởi tạo thanh toán (**idempotent**) |
| 10 | `GET /api/v1/checkout-groups/{checkoutGroupId}` | Xem trạng thái nhóm đơn (client poll) |

**API 7: `POST /api/v1/checkout/preview`**

Request:
```json
{
  "cartItemIds": ["44444444-4444-4444-4444-000000000001", "44444444-4444-4444-4444-000000000002"],
  "shippingAddress": {
    "receiverName": "Nguyễn Văn A",
    "street": "123 Đường Lê Lợi",
    "ward": "Bến Nghé",
    "district": "Quận 1",
    "province": "Hồ Chí Minh",
    "ghnDistrictId": 1442,
    "ghnWardCode": "20101"
  },
  "contactPhone": "0912345678",
  "paymentMethod": "WALLET",
  "carrierCode": "GHN",
  "vouchers": {
    "systemVoucherCode": "NEXUSNEW",
    "shopVouchers": [ { "sellerId": "22222222-2222-2222-2222-000000000001", "code": "FASHION10K" } ]
  }
}
```
Response 200:
```json
{
  "msg": "Checkout preview generated successfully",
  "data": {
    "quoteId": "quote-20261026-001",
    "orders": [
      {
        "sellerId": "22222222-2222-2222-2222-000000000001",
        "sellerName": "Nexus Fashion Shop",
        "itemsAmount": 250000.00,
        "productDiscountAmount": 0.00,
        "shopDiscountAmount": 10000.00,
        "systemDiscountAmount": 10000.00,
        "platformMarkupAmount": 0.00,
        "sellerReceiveAmount": 240000.00,
        "platformFeeAmount": -10000.00,
        "shippingFeeAmount": 25000.00,
        "totalAmount": 255000.00,
        "shippingOptions": [
          { "serviceType": "STANDARD", "carrierCode": "GHN", "fee": 25000.00, "estimatedDeliveryAt": "2026-10-29T18:00:00Z" }
        ]
      },
      {
        "sellerId": "22222222-2222-2222-2222-000000000002",
        "sellerName": "Nexus Denim Store",
        "itemsAmount": 450000.00,
        "productDiscountAmount": 0.00,
        "shopDiscountAmount": 0.00,
        "systemDiscountAmount": 10000.00,
        "platformMarkupAmount": 0.00,
        "sellerReceiveAmount": 450000.00,
        "platformFeeAmount": -10000.00,
        "shippingFeeAmount": 30000.00,
        "totalAmount": 470000.00,
        "shippingOptions": [
          { "serviceType": "STANDARD", "carrierCode": "GHN", "fee": 30000.00, "estimatedDeliveryAt": "2026-10-29T18:00:00Z" }
        ]
      }
    ],
    "grandTotal": 725000.00,
    "appliedVouchers": ["NEXUSNEW", "FASHION10K"],
    "invalidVouchers": [],
    "walletBalance": 1500000.00,
    "warnings": []
  }
}
```
Kiểm tra invariant (đơn 1): `255000 = 240000 + (−10000) + 25000` ✓; (đơn 2): `470000 = 450000 + (−10000) + 30000` ✓.
`platformMarkupAmount = 0.00` vì `PLATFORM_MARKUP_PERCENT` chưa chốt (OQ-06). Cách chia voucher hệ thống giữa các order `[OPEN]`, kèm quy tắc stacking OQ-13 (tối đa 1 mã PRODUCT/dòng, 1 mã SHOP/đơn, 1 mã SYSTEM/checkout `[PROPOSED]`). `walletBalance` do Payment cung cấp, `[OPEN]` OQ-17. Phí ship lấy từ Shipping; lỗi → `SHIPPING_QUOTE_UNAVAILABLE`.

**API 8: `POST /api/v1/checkout`**
Headers: `Authorization: Bearer <JWT>`, `Idempotency-Key: <uuid-v4>` (bắt buộc).
Request: như API 7 + `"expectedGrandTotal": 725000.00` (lệch → `PRICE_CHANGED`).
Response 201:
```json
{
  "msg": "Checkout group created successfully",
  "data": {
    "checkoutGroupId": "77777777-7777-7777-7777-000000000001",
    "status": "PENDING_PAYMENT",
    "totalAmount": 725000.00,
    "paymentMethod": "WALLET",
    "paymentDeadlineAt": "2026-10-26T14:21:00Z",
    "orders": [
      {
        "orderId": "88888888-8888-8888-8888-000000000001",
        "orderNo": "ORD-20261026-000001",
        "sellerId": "22222222-2222-2222-2222-000000000001",
        "sellerName": "Nexus Fashion Shop",
        "status": "PENDING_PAYMENT",
        "totalAmount": 255000.00,
        "itemCount": 1
      },
      {
        "orderId": "88888888-8888-8888-8888-000000000002",
        "orderNo": "ORD-20261026-000002",
        "sellerId": "22222222-2222-2222-2222-000000000002",
        "sellerName": "Nexus Denim Store",
        "status": "PENDING_PAYMENT",
        "totalAmount": 470000.00,
        "itemCount": 1
      }
    ]
  }
}
```
Cùng key + cùng request hash → trả lại response cũ; cùng key khác hash → 409 xung đột `[PROPOSED]`. Đã có checkout đang mở cho cart → `CHECKOUT_ALREADY_OPEN` kèm id checkout đang mở. `paymentMethod` hiện hỗ trợ `WALLET`, `VNPAY` (COD chưa hỗ trợ).

**API 9: `POST /api/v1/checkout-groups/{id}/pay`**
Headers: `Idempotency-Key` bắt buộc.
Request: `{ "paymentMethod": "WALLET", "returnUrl": "https://nexus.local/checkout/result" }`
Response 200:
```json
{
  "msg": "Payment initiated successfully",
  "data": {
    "checkoutGroupId": "77777777-7777-7777-7777-000000000001",
    "status": "PAYMENT_PROCESSING",
    "totalAmount": 725000.00,
    "paymentAttemptCount": 1,
    "paymentDeadlineAt": "2026-10-26T14:21:00Z",
    "paymentUrl": null
  }
}
```
Điều kiện: group `PENDING_PAYMENT`, **chưa quá `payment_deadline_at`** (quá hạn → `DEADLINE_EXPIRED`), `payment_attempt_count < PAYMENT_RETRY_LIMIT`. Hành động: attempt+1, `PAYMENT_PROCESSING`, saga `PAYMENT_PENDING`, gửi yêu cầu sang Payment. Với `VNPAY`, `paymentUrl` do Payment cung cấp `[OPEN]`. Kết quả cuối đến bất đồng bộ (2.5.1/2.5.2); client poll API 10.

**API 10: `GET /api/v1/checkout-groups/{id}`**
```json
{
  "msg": "Checkout group retrieved successfully",
  "data": {
    "checkoutGroupId": "77777777-7777-7777-7777-000000000001",
    "status": "PAID",
    "totalAmount": 725000.00,
    "paymentMethod": "WALLET",
    "paymentAttemptCount": 1,
    "paymentDeadlineAt": "2026-10-26T14:21:00Z",
    "walletTransactionId": "00000000-0000-0000-0000-000000000999"
  }
}
```
`status` ∈ `PENDING_PAYMENT`, `PAYMENT_PROCESSING`, `PAID`, `CANCELLED`, `EXPIRED`.

### 4.3 Vòng đời đơn hàng

| # | Endpoint | Mô tả | Quyền |
|---|---|---|---|
| 11 | `GET /api/v1/orders?page=1&size=20&status=PAID` | Danh sách đơn của buyer (gồm `orders` + `auction_orders`) | ORDER.LIST |
| 12 | `GET /api/v1/orders/{orderId}` | Chi tiết đơn + history | ORDER.VIEW |
| 13 | `POST /api/v1/orders/{orderId}/cancel` | Buyer hủy đơn | ORDER.CANCEL |
| 14 | `POST /api/v1/orders/{orderId}/confirm-received` | Buyer xác nhận nhận hàng | **`[OPEN]`** OQ-11, đặt sau feature flag |
| 15 | `POST /api/v1/seller/orders/{orderId}/accept` | Seller xác nhận | `ORDER.CONFIRM` `[OPEN]` OQ-12 |
| 16 | `POST /api/v1/seller/orders/{orderId}/reject` | Seller từ chối | `ORDER.REJECT` `[OPEN]` OQ-12 |

**API 11** response 200:
```json
{
  "msg": "My orders retrieved successfully",
  "data": {
    "items": [
      {
        "orderId": "88888888-8888-8888-8888-000000000001",
        "orderNo": "ORD-20261026-000001",
        "orderType": "REGULAR",
        "sellerId": "22222222-2222-2222-2222-000000000001",
        "sellerName": "Nexus Fashion Shop",
        "status": "PAID",
        "totalAmount": 255000.00,
        "itemCount": 1,
        "createdAt": "2026-10-26T14:06:00Z"
      }
    ],
    "page": 1, "size": 20, "totalItems": 1, "totalPages": 1
  }
}
```
`orderType` ∈ `REGULAR` | `AUCTION`. Sắp `created_at DESC`; chỉ đơn `deleted_at IS NULL`; rỗng → "No Data".

**API 12** response 200:
```json
{
  "msg": "Order details retrieved successfully",
  "data": {
    "orderId": "88888888-8888-8888-8888-000000000001",
    "orderNo": "ORD-20261026-000001",
    "orderType": "REGULAR",
    "status": "PAID",
    "sellerId": "22222222-2222-2222-2222-000000000001",
    "sellerName": "Nexus Fashion Shop",
    "buyerId": "11111111-1111-1111-1111-000000000004",
    "paymentMethod": "WALLET",
    "itemsAmount": 250000.00,
    "productDiscountAmount": 0.00,
    "shopDiscountAmount": 10000.00,
    "systemDiscountAmount": 10000.00,
    "shippingFeeAmount": 25000.00,
    "totalAmount": 255000.00,
    "shippingAddress": {
      "receiverName": "Nguyễn Văn A",
      "street": "123 Đường Lê Lợi",
      "ward": "Bến Nghé",
      "district": "Quận 1",
      "province": "Hồ Chí Minh"
    },
    "contactPhone": "0912345678",
    "paidAt": "2026-10-26T14:07:00Z",
    "sellerRespondDeadlineAt": "2026-10-27T14:07:00Z",
    "items": [
      {
        "orderItemId": "99999999-9999-9999-9999-000000000001",
        "productId": "55555555-5555-5555-5555-000000000001",
        "skuId": "66666666-6666-6666-6666-000000000001",
        "productName": "Áo sơ mi Oxford",
        "skuAttributesSnapshot": { "Color": "Trắng", "Size": "L" },
        "quantity": 1,
        "unitPrice": 250000.00,
        "unitPriceAfterDiscount": 250000.00,
        "lineTotal": 250000.00
      }
    ],
    "history": [
      { "fromStatus": null, "toStatus": "PENDING_PAYMENT", "actorRole": "BUYER", "createdAt": "2026-10-26T14:06:00Z" },
      { "fromStatus": "PENDING_PAYMENT", "toStatus": "PAID", "actorRole": "SYSTEM", "createdAt": "2026-10-26T14:07:00Z" }
    ]
  }
}
```
`history` đọc từ `order_status_history` theo `created_at`. Đơn `AUCTION` trả thêm `auctionId`, `winningBidId`, `winningAmount`.

**API 13: `POST .../cancel`**
Request (reasonCode theo danh sách đóng băng; lý do chi tiết vào `reasonNote`):
```json
{ "reasonCode": "BUYER_CANCELLED", "reasonNote": "Tôi muốn đổi địa chỉ giao hàng khác" }
```
Response 200:
```json
{
  "msg": "Order cancelled successfully",
  "data": {
    "orderId": "88888888-8888-8888-8888-000000000001",
    "orderNo": "ORD-20261026-000001",
    "status": "CANCELLED",
    "reasonCode": "BUYER_CANCELLED",
    "wasPaid": true,
    "refundStatus": "REQUESTED",
    "cancelledAt": "2026-10-26T14:15:00Z"
  }
}
```
Buyer chỉ hủy được khi `PENDING_PAYMENT`, hoặc `PAID` chưa `CONFIRMED` `[PROPOSED]`. Đơn chưa trả: `refundStatus = null`. Mỗi đơn chỉ hủy **một lần**. Idempotent (COM-PLT-001).

**API 14: `POST .../confirm-received`** `[OPEN]`
State machine hiện chỉ cho SYSTEM chuyển `DELIVERED → COMPLETED`. Nếu bật: `DELIVERED → COMPLETED`, phát `OrderCompleted`. Mặc định tắt (feature flag).
```json
{ "msg": "Order receipt confirmed successfully",
  "data": { "orderId": "...", "orderNo": "...", "status": "COMPLETED", "completedAt": "2026-10-26T16:00:00Z" } }
```

**API 15: `POST /api/v1/seller/orders/{orderId}/accept`**
Request: `{ "note": "Shop đã gói hàng xong" }`. Điều kiện: `PAID`, trong `seller_respond_deadline_at`; quá hạn → `DEADLINE_EXPIRED`.
Response 200: `{ orderId, orderNo, status: "CONFIRMED", sellerAcceptedAt }`. Phát `OrderConfirmed`.

**API 16: `POST /api/v1/seller/orders/{orderId}/reject`**
Request: `{ "reasonCode": "SELLER_REJECTED", "reasonNote": "Kho tạm thời hết size L" }`.
Response 200:
```json
{
  "msg": "Order rejected by seller",
  "data": {
    "orderId": "88888888-8888-8888-8888-000000000001",
    "orderNo": "ORD-20261026-000001",
    "status": "CANCELLED",
    "reasonCode": "SELLER_REJECTED",
    "refundStatus": "REQUESTED",
    "cancelledAt": "2026-10-26T15:05:00Z"
  }
}
```
Chuyển `PAID → CANCELLED`, `actor_role = SELLER`, `was_paid = true`, kích hoạt refund.

---

## 5. Mã lỗi chuẩn

Quy tắc: thông điệp 100% tiếng Anh, Sentence case; mã `SCREAMING_SNAKE_CASE`.

| HTTP | Error code | Thông điệp | Trường hợp |
|:---:|---|---|---|
| 400 | `VAL_INVALID_QUANTITY` | Quantity must be between 1 and 99. | Số lượng ngoài khoảng cho phép |
| 400 | `VAL_IDEMPOTENCY_KEY_REQUIRED` | Idempotency-Key header is mandatory for this request. | Thiếu header khi checkout/pay/refund |
| 400 | `VAL_INVALID_SHIPPING_ADDRESS` | Complete shipping address with district and ward is required. | Thiếu quận/phường |
| 400 | `VAL_EMPTY_SELECTION` | At least one cart item must be selected. | Không chọn item |
| 400 | `VAL_REASON_REQUIRED` | Reason note is required when reason code is not specific. | Thiếu `reasonNote` khi cần |
| 401 | `UNAUTHORIZED` | Missing or expired authentication token. | Thiếu/hết hạn JWT |
| 403 | `FORBIDDEN` | You do not have permission to perform this action. | Truy cập đơn người khác / thiếu privilege |
| 404 | `CART_NOT_FOUND` | Cart does not exist for the authenticated user. | |
| 404 | `CART_ITEM_NOT_FOUND` | Cart item was not found in your cart. | |
| 404 | `ORDER_NOT_FOUND` | Order does not exist in the system. | |
| 404 | `CHECKOUT_GROUP_NOT_FOUND` | Checkout group does not exist. | |
| 404 | `VOUCHER_NOT_FOUND` | Voucher code does not exist or has expired. | |
| 409 | `CART_ITEM_LIMIT_EXCEEDED` | Cart cannot exceed 50 distinct items. | |
| 409 | `INSUFFICIENT_STOCK` | Requested quantity exceeds current available stock. | |
| 409 | `PRICE_CHANGED` | Product price has changed since quote was generated. | Kèm giá mới |
| 409 | `CHECKOUT_ALREADY_OPEN` | An open checkout already exists for this cart. | Vi phạm `uq_checkout_groups_open_cart`; kèm id |
| 409 | `INVALID_TRANSITION` | Order cannot be transitioned in its current state. | Ngoài state machine |
| 409 | `CONCURRENT_UPDATE` | The resource was modified by another request. Please retry. | Xung đột `row_version` |
| 409 | `IDEMPOTENCY_KEY_CONFLICT` | Idempotency key was reused with a different request. | Cùng key, khác hash `[PROPOSED]` |
| 409 | `PAYMENT_ATTEMPT_LIMIT_REACHED` | Maximum payment attempts reached for this checkout. | `attempt = PAYMENT_RETRY_LIMIT` `[PROPOSED]` |
| 409 | `VOUCHER_USAGE_EXCEEDED` | Voucher has already reached its maximum usage limit. | |
| 410 | `DEADLINE_EXPIRED` | Payment deadline or response deadline has expired. | Quá hạn trả tiền / seller phản hồi |
| 503 | `SHIPPING_QUOTE_UNAVAILABLE` | Shipping carrier is currently unavailable. | Lỗi/timeout báo giá ship |

Lỗi nội bộ khi nhận event (không trả cho client): `AUCTION_SETTLED_INVALID_PAYLOAD` (thiếu `skuId`/`paymentDeadlineAt`...) ghi vào `inbox_messages.last_error`.

---

## 6. Nhật ký quyết định và câu hỏi mở

### 6.1 Quyết định đã chốt với Auction (2026-10-27)
| # | Nội dung | Kết luận |
|---|---|---|
| 1 | Event tạo đơn đấu giá | `AuctionSettled`, phát ngay khi chốt người thắng. `AuctionWon` chỉ để Notification |
| 2 | Hạn thanh toán | Auction tính `determinedAt + 24h`, luôn gửi `paymentDeadlineAt`; Commerce dùng nguyên giá trị, không dự phòng |
| 3 | Cơ chế hủy quá hạn | Job quét (Commerce); không timer |
| 4 | Hủy phiên sau `AuctionSettled` | Không xảy ra; nếu gặp chỉ ghi log + báo Support |
| 5 | `skuId` trong `AuctionSettled` | Bắt buộc |
| 6 | Event báo `orderId` | `OrderCreated` với `orderType=AUCTION`, kèm `auctionId` |
| 7 | `reasonCode` | 5 giá trị đóng băng (2.4.6) |
| 8 | `Auction.CheckPaymentDeadline` | Commerce không gọi |
| 9 | `OrderPaymentFailed` | Auction không cần nghe |
| 10 | Chủ sở hữu 24 giờ | Auction |

### 6.2 Câu hỏi mở (không tự quyết)

| ID | Vấn đề | Ảnh hưởng / Đề xuất |
|---|---|---|
| **OQ-03** | Ai trừ điểm uy tín khi `PAYMENT_TIMEOUT` | Đề xuất: User nghe thẳng `OrderCancelled(PAYMENT_TIMEOUT)`, lọc `orderType == AUCTION`, sở hữu `PAYMENT_TIMEOUT_PENALTY_SCORE`. Auction không trừ. Cần User xác nhận. SRS 3.5.3.5 đang ghi Auction áp phạt, cần sửa SRS |
| **OQ-22** | `SETTLED` ở Flow4 của Auction có phải chỉ là tên trạng thái nội bộ | Chờ Auction xác nhận |
| **OQ-23** | Auction giữ cùng `eventId` khi retry `AuctionSettled`? | Chờ Auction xác nhận |
| **OQ-21** | Hạn checkout 5 phút hay 15 phút | Hiện giữ 15 (đã seed). Nếu đổi 5 phút: xem lại `PAYMENT_RETRY_LIMIT = 3` và ân hạn kết quả đến muộn |
| OQ-05 | Refund nhiều đợt cho cùng order bị unique chặn | `OrderRefundRequested` |
| OQ-06 | `PLATFORM_MARKUP_PERCENT`, `AUCTION_PLATFORM_FEE_PERCENT` chưa seed | API 7 để markup `0.00` |
| OQ-07 | Cách gọi Inventory (sync/event); hạn giữ kho 30' ≠ 15' | CheckoutSaga |
| OQ-08 | Thuế | DB không có cột thuế |
| OQ-09 | Voucher của đơn đã trả rồi bị hủy | Xử lý nội bộ khi `OrderCancelled` |
| OQ-10 | Thời điểm cart → `CHECKED_OUT` | 2.5.1 |
| OQ-11 | `ShipmentFailed/Returned`, buyer hủy sau `CONFIRMED`, xác nhận nhận hàng sớm, refund sau `DELIVERED` | 2.5, API 14 |
| OQ-12 | Thiếu privilege `ORDER.CONFIRM/REJECT`, `DISCOUNT.*` | API 15, 16 |
| OQ-13 | Quy tắc stacking voucher | API 7 |
| OQ-14 | Đơn vị `FREE_SHIPPING_THRESHOLD` | Mục 1.2 |
| OQ-17 | Tên/schema event Payment; `STRIPE` (SRS) lệch `WALLET/VNPAY` (DB) | Toàn bộ inbox Payment, API 9 |
| OQ-18 | Ký đóng băng `reasonCode` trong contract chung | Mục 2.4.6 |
| OQ-19 | Ngưỡng retry/`DEAD` outbox, ngưỡng saga treo, chu kỳ job | Mục 3.4 |
| OQ-20 | Shipment tạo ở `OrderPaid` hay `OrderConfirmed` | `OrderConfirmed` |
| OQ-24 | Giải ngân cho seller khi `OrderCompleted` | Cần hợp đồng Commerce ↔ Payment |

---

## Phụ lục. Hướng dẫn nhanh khi hiện thực

1. Mọi thay đổi trạng thái = một transaction: update có `row_version` + insert `order_status_history` + insert `outbox_messages`.
2. Mọi consumer = kiểm tra `inbox_messages` trước, xử lý, đánh dấu `PROCESSED` cùng transaction.
3. Mọi API tạo/thanh toán/refund = kiểm tra `idempotency_keys` trước.
4. Event của Payment đi qua lớp adapter (OQ-17). Gặp `[OPEN]`: để TODO kèm `OQ-xx`, đặt sau interface hoặc feature flag.
5. Nhận `AuctionSettled`: validate đủ field bắt buộc trước khi tạo đơn; không tự tính `paymentDeadlineAt`.
6. Không join, không query bảng của service khác; dữ liệu ngoài lấy qua API/event và lưu snapshot.
