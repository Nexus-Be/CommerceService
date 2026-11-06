# AI Agent Implementation Spec: Hybrid Payment Expiration & Asymmetric Price Drift Policy

| Mục | Giá trị |
|---|---|
| **Service** | Commerce Service (Project Nexus) |
| **Database** | PostgreSQL (`DB_Commerce_v2`) |
| **Tham chiếu SRS** | `COM-CART-006`, `COM-CHK-001`, `COM-CHK-008`, `COM-PAY-001`, `COM-AUC-006`, `COM-DSC-006`, `COM-PLT-002/004/008` |
| **Mục tiêu** | Đặc tả chi tiết cho AI Agent / Developer hiện thực hóa cơ chế xử lý hết hạn thanh toán lai (Lean 2 tầng) và chính sách biến động giá bất đối xứng khi Checkout. |

---

## 1. Ràng buộc Cứng của Hệ thống (System Context & Hard Constraints)

AI Agent bắt buộc phải tuân thủ các quy tắc bất biến sau khi sinh code:
1. **Kiểu dữ liệu Enum `[DB]`:** Database không dùng PostgreSQL ENUM. Mọi cột trạng thái (`status`, `actor_role`, `saga_type`, v.v.) lưu dạng `text` kèm `CHECK`. Trong code phải map dưới dạng `string`.
2. **Audit Timestamp `[DB]`:** Schema không có trigger tự động cập nhật `updated_at`. Mọi câu lệnh `UPDATE` trong code bắt buộc phải gán `updated_at = DateTime.UtcNow` (hoặc `NOW()` trong SQL).
3. **Optimistic Concurrency Control `[DB]`:** Mọi thao tác cập nhật trạng thái trên `carts`, `checkout_groups`, `orders`, `auction_orders`, `saga_state` bắt buộc phải kiểm tra `row_version` trong mệnh đề `WHERE` và tăng `row_version = row_version + 1`.
4. **Transactional Outbox & History `[SRS]`:** Mọi sự kiện chuyển trạng thái đơn hàng phải nằm trong **một Local Database Transaction duy nhất** bao gồm:
   - Cập nhật bảng chính (`checkout_groups` / `orders` / `auction_orders`).
   - Ghi nhận vào `order_status_history` (và `order_cancellations` nếu là hủy đơn).
   - Ghi event vào bảng `outbox_messages` (`status = 'PENDING'`).
5. **Ràng buộc XOR khóa ngoại `[DB]`:** Các bảng `order_status_history` và `order_cancellations` có constraint `CHECK (num_nonnulls(order_id, auction_order_id) = 1)`. Khi insert cho đơn thường thì `auction_order_id = NULL`; khi insert cho đơn đấu giá thì `order_id = NULL`.

---

## 2. Module 1: Chiến lược Hết hạn Thanh toán Lai (Hybrid Payment Expiration Strategy)

Áp dụng mô hình **Active Sweeper (Polling Job) + Passive Guard (Lazy API Check)** cho hai đối tượng:
- **Checkout Group & Regular Orders:** Hạn thanh toán 15 phút (`CHECKOUT_TIMEOUT_MINUTES`).
- **Auction Orders:** Hạn thanh toán 24 giờ (`AUCTION_PAYMENT_DEADLINE_HOURS`).

### 2.1. Shared Domain Service (`IPaymentExpirationService`)

Tạo service dùng chung để cả Background Worker và API Controller cùng gọi, đảm bảo tính Idempotent và chống Race Condition.

#### A. Hàm `ExpireCheckoutGroupAsync(Guid checkoutGroupId, CancellationToken ct)`
Thực thi tuần tự trong **1 Database Transaction**:

1. **Đọc và kiểm tra điều kiện:**
   - Truy vấn `checkout_groups` theo `checkoutGroupId`.
   - Nếu không tồn tại, hoặc `status != "PENDING_PAYMENT"`, hoặc `payment_deadline_at >= DateTime.UtcNow`: thoát hàm ngay và trả về `false`.
2. **Cập nhật `checkout_groups` (Optimistic Lock):**
   ```sql
   UPDATE checkout_groups
   SET status = 'EXPIRED',
       updated_at = NOW(),
       row_version = row_version + 1
   WHERE id = @CheckoutGroupId
     AND status = 'PENDING_PAYMENT'
     AND row_version = @ExpectedRowVersion;
   ```
   - Nếu `rowsAffected == 0`: Rollback transaction và trả về `false` (đã có luồng khác xử lý).
3. **Hủy tất cả `orders` thuộc group (`COM-CHK-008`):**
   ```sql
   UPDATE orders
   SET status = 'CANCELLED',
       cancelled_at = NOW(),
       updated_at = NOW(),
       row_version = row_version + 1
   WHERE checkout_group_id = @CheckoutGroupId
     AND status = 'PENDING_PAYMENT'
   RETURNING id, order_no, buyer_id, seller_id, total_amount, payment_deadline_at;
   ```
4. **Ghi `order_cancellations` và `order_status_history` cho từng order bị hủy:**
   - Insert `order_cancellations`:
     - `order_id = order.Id`, `auction_order_id = NULL`
     - `cancelled_by = NULL`, `actor_role = 'SYSTEM'`
     - `reason_code = 'PAYMENT_TIMEOUT'`, `was_paid = false`, `inventory_released = false`
   - Insert `order_status_history`:
     - `order_id = order.Id`, `auction_order_id = NULL`
     - `from_status = 'PENDING_PAYMENT'`, `to_status = 'CANCELLED'`
     - `changed_by = NULL`, `actor_role = 'SYSTEM'`, `reason = 'PAYMENT_TIMEOUT'`
5. **Giải phóng Voucher đã giữ chỗ (`COM-DSC-006`):**
   - Giảm `reserved_count` trên `discount_codes` (đảm bảo không âm):
     ```sql
     UPDATE discount_codes dc
     SET reserved_count = GREATEST(0, dc.reserved_count - sub.cnt),
         updated_at = NOW()
     FROM (
         SELECT discount_code_id, COUNT(*) AS cnt
         FROM discount_code_usages
         WHERE checkout_group_id = @CheckoutGroupId AND status = 'RESERVED'
         GROUP BY discount_code_id
     ) sub
     WHERE dc.id = sub.discount_code_id;
     ```
   - Chuyển trạng thái `discount_code_usages` sang `RELEASED`:
     ```sql
     UPDATE discount_code_usages
     SET status = 'RELEASED',
         released_at = NOW(),
         updated_at = NOW()
     WHERE checkout_group_id = @CheckoutGroupId AND status = 'RESERVED';
     ```
6. **Cập nhật trạng thái Saga (`COM-PLT-004`):**
   ```sql
   UPDATE saga_state
   SET current_state = 'COMPENSATED',
       updated_at = NOW(),
       row_version = row_version + 1
   WHERE correlation_id = @CheckoutGroupId
     AND saga_type = 'CheckoutSaga'
     AND current_state NOT IN ('COMPLETED', 'COMPENSATED', 'FAILED');
   ```
7. **Phát sự kiện `OrderCancelled` vào `outbox_messages` (`COM-PLT-002`):**
   - Với mỗi order trong group, insert 1 dòng vào `outbox_messages`:
     - `id = Guid.NewGuid()`, `aggregate_type = 'Order'`, `aggregate_id = order.Id`
     - `event_type = 'OrderCancelled'`, `status = 'PENDING'`, `occurred_at = DateTime.UtcNow`
     - `payload` (JSON):
       ```json
       {
         "orderType": "REGULAR",
         "orderId": "uuid",
         "orderNo": "ORD-YYYYMMDD-NNNNNN",
         "checkoutGroupId": "uuid",
         "buyerId": "uuid",
         "sellerId": "uuid",
         "totalAmount": 100000.00,
         "paymentDeadlineAt": "2026-06-04T10:15:00Z",
         "cancelledAt": "2026-06-04T10:15:05Z",
         "reasonCode": "PAYMENT_TIMEOUT",
         "actorRole": "SYSTEM",
         "wasPaid": false
       }
       ```
8. Commit Transaction và trả về `true`.

#### B. Hàm `ExpireAuctionOrderAsync(Guid auctionOrderId, CancellationToken ct)` (`COM-AUC-006`)
Thực thi tuần tự trong **1 Database Transaction**:

1. **Đọc và kiểm tra điều kiện:**
   - Truy vấn `auction_orders` theo `auctionOrderId`.
   - Nếu không tồn tại, hoặc `status != "PENDING_PAYMENT"`, hoặc `payment_deadline_at >= DateTime.UtcNow`: thoát hàm và trả về `false`.
2. **Cập nhật `auction_orders` (Optimistic Lock):**
   ```sql
   UPDATE auction_orders
   SET status = 'CANCELLED',
       cancelled_at = NOW(),
       updated_at = NOW(),
       row_version = row_version + 1
   WHERE id = @AuctionOrderId
     AND status = 'PENDING_PAYMENT'
     AND row_version = @ExpectedRowVersion;
   ```
   - Nếu `rowsAffected == 0`: Rollback và trả về `false`.
3. **Ghi `order_cancellations` và `order_status_history`:**
   - Insert `order_cancellations` với `order_id = NULL`, `auction_order_id = @AuctionOrderId`, `actor_role = 'SYSTEM'`, `reason_code = 'PAYMENT_TIMEOUT'`, `was_paid = false`.
   - Insert `order_status_history` với `order_id = NULL`, `auction_order_id = @AuctionOrderId`, `from_status = 'PENDING_PAYMENT'`, `to_status = 'CANCELLED'`, `actor_role = 'SYSTEM'`, `reason = 'PAYMENT_TIMEOUT'`.
4. **Phát sự kiện `OrderCancelled` vào `outbox_messages` (`COM-AUC-008`):**
   - `aggregate_type = 'AuctionOrder'`, `aggregate_id = auctionOrder.Id`, `event_type = 'OrderCancelled'`, `status = 'PENDING'`.
   - `payload` (JSON):
     ```json
     {
       "orderType": "AUCTION",
       "orderId": "uuid",
       "orderNo": "AOR-YYYYMMDD-NNNNNN",
       "auctionId": "uuid",
       "winningBidId": "uuid",
       "buyerId": "uuid",
       "sellerId": "uuid",
       "totalAmount": 500000.00,
       "paymentDeadlineAt": "2026-06-05T10:00:00Z",
       "cancelledAt": "2026-06-05T10:00:10Z",
       "reasonCode": "PAYMENT_TIMEOUT",
       "actorRole": "SYSTEM",
       "wasPaid": false
     }
     ```
5. Commit Transaction và trả về `true`.

---

### 2.2. Tầng 1: Passive Guard (Lazy API Check tại Controller / Command Handler)

1. **Tại API Khởi tạo Thanh toán (`COM-PAY-001` / `CHK-03`):**
   - Trước khi kiểm tra `payment_attempt_count` hoặc chuyển trạng thái sang `PAYMENT_PROCESSING`:
     - Nếu đối tượng đang có `status == "PENDING_PAYMENT"` và `DateTime.UtcNow > payment_deadline_at`:
       - Gọi ngay `await _expirationService.ExpireCheckoutGroupAsync(id, ct)` (hoặc `ExpireAuctionOrderAsync`).
       - Ném lỗi nghiệp vụ HTTP `409 Conflict` / `422 Unprocessable Entity` với mã `PAYMENT_DEADLINE_EXPIRED`.
2. **Tại API Xem chi tiết Checkout Group / Order / Auction Order (`COM-ORD-003`):**
   - Khi truy vấn chi tiết, nếu phát hiện bản ghi có `status == "PENDING_PAYMENT"` và `DateTime.UtcNow > payment_deadline_at`:
     - Gọi hàm `Expire...Async` để cập nhật trạng thái ngay lập tức trước khi trả dữ liệu về cho Client.

---

### 2.3. Tầng 2: Active Sweeper (Background Polling Worker)

Tạo `PaymentTimeoutSweeperWorker` (BackgroundService) chạy theo chu kỳ cấu hình (mặc định **30 giây/lần**):

1. **Quét Checkout Groups hết hạn (tận dụng partial index `idx_checkout_groups_expire`):**
   ```sql
   SELECT id
   FROM checkout_groups
   WHERE status = 'PENDING_PAYMENT'
     AND payment_deadline_at < NOW()
   ORDER BY payment_deadline_at ASC
   LIMIT 50
   FOR UPDATE SKIP LOCKED;
   ```
   - Lặp qua từng `id` và gọi `ExpireCheckoutGroupAsync(id, ct)`. Lỗi ở một group không được làm dừng vòng lặp của các group còn lại.

2. **Quét Auction Orders hết hạn (tận dụng partial index `idx_auction_orders_payment_deadline`):**
   ```sql
   SELECT id
   FROM auction_orders
   WHERE status = 'PENDING_PAYMENT'
     AND payment_deadline_at < NOW()
   ORDER BY payment_deadline_at ASC
   LIMIT 50
   FOR UPDATE SKIP LOCKED;
   ```
   - Lặp qua từng `id` và gọi `ExpireAuctionOrderAsync(id, ct)`.

---

## 3. Module 2: Chiến lược Xử lý Biến động Giá Bất đối xứng (Asymmetric Price Drift Policy)

Áp dụng tại bước **Validate Giỏ hàng & Bắt đầu Checkout (`COM-CART-006` & `COM-CHK-001`)** khi cờ `CHECKOUT_PRICE_REVALIDATION = true`.

### 3.1. Luồng xử lý chi tiết khi Buyer gọi API Checkout

1. **Bước 1: Lấy dữ liệu giá hiện tại từ Catalog Service**
   - Đọc giỏ hàng `ACTIVE` và toàn bộ `cart_items` của Buyer.
   - Gọi API đồng bộ sang Catalog Service để lấy trạng thái (`ACTIVE`) và giá bán hiện tại (`current_price`) của từng `sku_id`.

2. **Bước 2: So sánh `current_price` với `cart_items.unit_price_snapshot`**
   Phân loại từng item trong giỏ thành 3 nhóm:
   - **Nhóm không đổi:** `current_price == unit_price_snapshot`
   - **Nhóm giảm giá (`priceDrops`):** `current_price < unit_price_snapshot`
   - **Nhóm tăng giá (`priceIncreases`):** `current_price > unit_price_snapshot`

3. **Bước 3: Ra quyết định theo hướng biến động**

   - **Kịch bản A: Có ít nhất 1 sản phẩm TĂNG GIÁ (`priceIncreases.Count > 0`) — Hard Block & Snapshot Refresh**
     1. Mở một Transaction độc lập để làm mới giá snapshot trong giỏ hàng:
        - Cập nhật `cart_items.unit_price_snapshot = current_price`, `updated_at = DateTime.UtcNow` cho **tất cả** các item có thay đổi giá (cả tăng lẫn giảm).
        - Tính lại `carts.subtotal_amount = Σ (quantity * unit_price_snapshot)`, cập nhật `carts.last_activity_at = DateTime.UtcNow`, `carts.updated_at = DateTime.UtcNow`, `carts.row_version = row_version + 1`.
        - **Commit Transaction này ngay lập tức** (để lần bấm xác nhận tiếp theo của Buyer không bị chặn lại).
     2. **Dừng luồng Checkout:** Không tạo `checkout_groups`, không tạo `orders`, không reserve voucher hay tồn kho.
     3. Trả về HTTP `409 Conflict` với payload chuẩn:
        ```json
        {
          "errorCode": "PRICE_CHANGED",
          "message": "Giá của một số sản phẩm trong giỏ hàng đã tăng. Vui lòng kiểm tra và xác nhận lại giá mới trước khi đặt hàng.",
          "changedItems": [
            {
              "skuId": "uuid",
              "productId": "uuid",
              "productName": "Tên sản phẩm",
              "oldPrice": 150000.00,
              "newPrice": 180000.00,
              "changeType": "INCREASED"
            }
          ],
          "newCartSubtotal": 380000.00
        }
        ```

   - **Kịch bản B: KHÔNG có sản phẩm nào tăng giá, chỉ có sản phẩm GIẢM GIÁ hoặc GIỮ NGUYÊN (`priceIncreases.Count == 0`) — Silent Auto-Apply**
     1. Không ném lỗi `PRICE_CHANGED`.
     2. Nếu `priceDrops.Count > 0`:
        - Cập nhật `cart_items.unit_price_snapshot = current_price`, `updated_at = DateTime.UtcNow` cho các item giảm giá.
        - Tính lại `carts.subtotal_amount`, tăng `carts.row_version`.
     3. Tiếp tục luồng tạo `checkout_groups`, `orders`, và `order_items` (`COM-CHK-003`, `COM-CHK-005`) bằng mức giá `current_price` mới nhất.
     4. Trong response thành công của API Checkout, trả kèm mảng `appliedPriceDrops` để Frontend hiển thị thông báo: *"Bạn vừa tiết kiệm thêm X đồng nhờ sản phẩm giảm giá"*.

### 3.2. Quy tắc Khóa Giá sau khi Tạo Đơn (Post-Checkout Price Lock)
- Sau khi `checkout_groups` và `orders` đã được khởi tạo thành công ở trạng thái `PENDING_PAYMENT`, toàn bộ giá đã được đóng băng trong `order_items` (`unit_price`, `unit_price_after_discount`, `line_total`) và `orders.total_amount`.
- Tại API Khởi tạo Thanh toán (`COM-PAY-001`), hệ thống chỉ kiểm tra hạn thanh toán (`payment_deadline_at`) và số lần thử (`payment_attempt_count`), **tuyệt đối không** gọi lại Catalog để kiểm tra giá sản phẩm nữa.

---

## 4. Tiêu chí Nghiệm thu & Kiểm thử (Acceptance Criteria)

AI Agent cần sinh kèm Unit Tests / Integration Tests cho các kịch bản sau:

1. **`ExpireCheckoutGroup_ShouldBeIdempotent_WhenCalledConcurrently`**:
   - Khi Lazy API Check và Polling Job cùng gọi `ExpireCheckoutGroupAsync` cho 1 `checkout_group_id`, chỉ đúng 1 luồng trả về `true`, cập nhật trạng thái thành `EXPIRED`, giảm `reserved_count` của voucher đúng 1 lần và sinh đúng 1 bộ event `OrderCancelled` trong `outbox_messages`. Luồng còn lại trả về `false`.
2. **`Checkout_ShouldAutoApplyAndProceed_WhenItemPriceDecreases`**:
   - Khi giá Catalog thấp hơn `unit_price_snapshot`, API Checkout không báo lỗi, cập nhật lại `cart_items.unit_price_snapshot` và tạo `orders` với giá mới thấp hơn.
3. **`Checkout_ShouldBlockAndUpdateSnapshot_WhenAnyItemPriceIncreases`**:
   - Khi có 1 sản phẩm tăng giá và 1 sản phẩm giảm giá trong giỏ, API Checkout trả về lỗi `PRICE_CHANGED`, không tạo `checkout_groups`, nhưng `cart_items` trong DB đã được cập nhật giá mới cho cả 2 sản phẩm. Khi gọi lại API Checkout lần thứ 2 với cùng dữ liệu, đơn hàng được tạo thành công.
4. **`LatePayment_ShouldTriggerAutoRefund_WhenCheckoutGroupAlreadyExpired` (`COM-PAY-004`)**:
   - Nếu Payment Service gửi kết quả thanh toán thành công về sau khi `checkout_groups` đã chuyển sang `EXPIRED`, hệ thống giữ nguyên trạng thái `EXPIRED`/`CANCELLED`, tự động tạo bản ghi `order_refunds` (`reason_code = 'LATE_PAYMENT'`, `refund_type = 'FULL'`) và phát event `OrderRefundRequested` vào `outbox_messages`.