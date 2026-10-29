# Commerce Service Backlog (Project Nexus)

> **Nguồn:** Trích xuất từ `Commerce_Function_API_Backlog.xlsx` (Sheet `Backlog` & `Trace_Matrix`). Đã đối chiếu liên dịch vụ.

## 1. Danh sách Chức năng / Backlog Items

| ID | Module | Function name | Kind | Endpoint / trigger | Priority | Wave | Complexity |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CART-01 | Cart | Get my cart / Lấy giỏ hàng của tôi | Public API | GET /api/v1/carts/me | P0 | 1 | M |
| CART-02 | Cart | Add item to cart / Thêm sản phẩm vào giỏ | Public API | POST /api/v1/carts/me/items | P0 | 1 | L |
| CART-03 | Cart | Update cart item quantity / Cập nhật số lượng | Public API | PATCH /api/v1/carts/me/items/{cartItemId} | P0 | 1 | M |
| CART-04 | Cart | Remove cart item / Xoá một dòng khỏi giỏ | Public API | DELETE /api/v1/carts/me/items/{cartItemId} | P0 | 1 | S |
| CART-05 | Cart | Bulk remove / clear cart / Xoá nhiều dòng hoặc xoá hết | Public API | POST /api/v1/carts/me/items:bulk-remove | P1 | 1 | S |
| CART-06 | Cart | Validate cart / Kiểm tra giỏ hàng trước checkout | Public API | POST /api/v1/carts/me/validate (+ internal CartValidator dùng lại ở CHK-01/02) | P0 | 1 | M |
| CART-07 | Cart | Cart price calculation / Tính giá giỏ hàng | Internal component | PricingService.calculate(items, address?, vouchers?) | P0 | 1 | L |
| CART-08 | Cart | Cart summary (badge) / Số lượng giỏ hàng | Public API | GET /api/v1/carts/me/summary | P1 | 1 | S |
| CART-09 | Cart | Expire inactive carts / Job hết hạn giỏ hàng | Job | chạy mỗi giờ (@Scheduled + ShedLock) | P1 | 1 | S |
| CART-10 | Cart | Merge guest cart on login / Gộp giỏ khách (OPTIONAL) | Public API | POST /api/v1/carts/me/merge | P2 | 4 | S |
| CART-11 | Cart | Cleanup on Product/User events / Dọn giỏ theo sự kiện | Event consumer | topics: catalog.*, user.* | P1 | 1 | M |
| CHK-01 | Checkout | Checkout preview / Xem trước thanh toán | Public API | POST /api/v1/checkout/preview | P0 | 2 | L |
| CHK-02 | Checkout | Create checkout (group + orders) / Tạo checkout và đơn hàng | Public API | POST /api/v1/checkout | P0 | 2 | XL |
| CHK-03 | Checkout | Pay checkout group / Thanh toán nhóm đơn | Public API | POST /api/v1/checkout-groups/{checkoutGroupId}/pay | P0 | 2 | L |
| CHK-04 | Checkout | Get checkout group / Xem trạng thái checkout | Public API | GET /api/v1/checkout-groups/{checkoutGroupId} | P0 | 2 | S |
| CHK-05 | Checkout | Cancel checkout group / Huỷ checkout chưa thanh toán | Public API | POST /api/v1/checkout-groups/{checkoutGroupId}/cancel | P0 | 2 | M |
| CHK-06 | Checkout | Expire unpaid checkout / Job hết hạn thanh toán | Job | mỗi 1 phút (+ consumer InventoryReservationExpired làm lưới an toàn) | P0 | 2 | M |
| PAY-01 | Payment (Commerce side) | Handle payment result / Nhận kết quả thanh toán | Event consumer | PaymentSucceeded / PaymentFailed / PaymentExpired (event do Payment Service phát, cần chốt contract) | P0 | 2 | XL |
| DSC-01 | Discount | List available vouchers / Danh sách voucher khả dụng | Public API | GET /api/v1/vouchers/available?cartItemIds=&sellerId=&page=&size= | P1 | 2 | M |
| DSC-02 | Discount | Validate & calculate voucher / Kiểm tra và tính giảm giá | Internal component | VoucherService.validateAndCalculate(code, userId, scopeContext) | P0 | 1 | L |
| DSC-03 | Discount | Reserve vouchers / Giữ lượt voucher | Internal component | VoucherService.reserve(checkoutGroupId, orderId?, userId, codes) – gọi trong TX-1 của CHK-02 | P0 | 2 | M |
| DSC-04 | Discount | Confirm / Release voucher usage / Xác nhận hoặc trả lại lượt voucher | Internal component | VoucherService.confirm(groupId) / release(orderId/groupId, reason) | P0 | 2 | M |
| DSC-05 | Discount | Create voucher / Tạo voucher | Public API | POST /api/v1/vouchers | P1 | 4 | M |
| DSC-06 | Discount | Update / activate / delete voucher / Sửa, bật tắt, xoá voucher | Public API | PATCH /api/v1/vouchers/{voucherId} | P1 | 4 | M |
| DSC-07 | Discount | List vouchers for management / Quản lý danh sách voucher | Public API | GET /api/v1/vouchers | P2 | 4 | S |
| DSC-08 | Discount | Expire vouchers / Job hết hạn voucher | Job | Scheduled - hourly | P2 | 4 | S |
| ORD-01 | Order | List my orders / Lịch sử đơn hàng của tôi | Public API | GET /api/v1/orders | P0 | 3 | M |
| ORD-02 | Order | Get order detail / Chi tiết & theo dõi đơn hàng | Public API | GET /api/v1/orders/{orderId}?orderType=REGULAR/AUCTION | P0 | 3 | L |
| ORD-03 | Order | Seller order list / Danh sách đơn của seller | Public API | GET /api/v1/seller/orders | P0 | 3 | M |
| ORD-04 | Order | Seller accept order / Seller xác nhận đơn | Public API | POST /api/v1/seller/orders/{orderId}/accept?orderType= | P0 | 3 | M |
| ORD-05 | Order | Seller reject order / Seller từ chối đơn | Public API | POST /api/v1/seller/orders/{orderId}/reject?orderType= | P0 | 3 | L |
| ORD-06 | Order | Cancel order / Huỷ đơn (Buyer, Support, Admin) | Public API | POST /api/v1/orders/{orderId}/cancel?orderType= | P0 | 3 | XL |
| ORD-07 | Order | Confirm received / Buyer xác nhận đã nhận hàng | Public API | POST /api/v1/orders/{orderId}/confirm-received?orderType= | P2 | 4 | S |
| ORD-08 | Order | Auto-complete delivered orders / Job tự hoàn tất đơn | Job | mỗi giờ | P1 | 3 | S |
| ORD-09 | Order | Seller response timeout / Job quá hạn seller phản hồi | Job | mỗi 1 phút | P0 | 3 | M |
| ORD-10 | Order | Handle shipment events / Nhận sự kiện vận chuyển | Event consumer | shipment.* | P0 | 3 | L |
| ORD-11 | Order | Admin list orders / Quản trị xem tất cả đơn | Public API | GET /api/v1/admin/orders | P1 | 3 | M |
| ORD-12 | Order | Admin update order status / Admin chỉnh trạng thái ngoại lệ | Public API | PATCH /api/v1/admin/orders/{orderId}/status?orderType= | P1 | 3 | M |
| ORD-13 | Order | Admin/Support refund / Hoàn tiền thủ công | Public API | POST /api/v1/admin/orders/{orderId}/refund?orderType= | P1 | 3 | M |
| ORD-14 | Order | Handle refund result / Nhận kết quả hoàn tiền | Event consumer | payment.refund.* | P0 | 3 | M |
| ORD-15 | Invoice | Generate invoice / Sinh hoá đơn | Event consumer | Event consumer (OrderCompleted, nội bộ) + worker bất đồng bộ | P1 | 3 | M |
| ORD-16 | Invoice | Download invoice / Tải hoá đơn | Public API | GET /api/v1/orders/{orderId}/invoice?orderType= | P1 | 3 | S |
| ORD-17 | Platform | Generate order number / Sinh mã đơn | Internal component | SequenceService.next(seqType) | P0 | 0 | S |
| ORD-18 | Platform | Order state machine / Kiểm soát chuyển trạng thái | Internal component | OrderStateMachine.transition(order, toStatus, actor, reason) | P0 | 0 | L |
| ORD-19 | Platform | Idempotency handling / Xử lý idempotency | Internal component | IdempotencyFilter (header Idempotency-Key) | P0 | 0 | M |
| ORD-20 | Platform | Outbox publisher / Phát event từ outbox | Job | mỗi ~1s | P0 | 0 | M |
| ORD-21 | Platform | Saga recovery / Job phục hồi saga treo | Job | mỗi 2 phút | P0 | 2 | L |
| ORD-22 | Platform | Handle inventory events / Nhận sự kiện tồn kho | Event consumer | inventory.* | P1 | 2 | M |
| ORD-23 | Order | Rating eligibility check / Kiểm tra đủ điều kiện đánh giá | Internal API | GET /internal/v1/orders/{orderId}/rating-eligibility?userId=&productId= | P1 | 3 | S |
| ORD-24 | Platform | Inbox retry worker / Job thử lại inbox | Job | Scheduled - every 1 min | P1 | 0 | M |
| ORD-25 | Order | Refund reconciliation alert / Cảnh báo đối soát refund | Job | Scheduled - every 10 min | P2 | 4 | S |
| ORD-26 | Platform | Settings provider / Đọc cấu hình hệ thống | Internal component | SettingsProvider.Get<T>(key) | P1 | 0 | S |
| AO-01 | Auction Order | Create auction order from settlement / Tạo đơn đấu giá | Event consumer | Consumer - AuctionSettled (AuctionWon ignored) | P0 | 4 | L |
| AO-02 | Auction Order | Submit shipping info & get quote / Nhập địa chỉ và báo giá ship | Public API | PUT /api/v1/auction-orders/{auctionOrderId}/shipping | P0 | 4 | M |
| AO-03 | Auction Order | Pay auction order / Thanh toán đơn đấu giá | Public API | POST /api/v1/auction-orders/{auctionOrderId}/pay | P0 | 4 | M |
| AO-04 | Auction Order | Auction payment timeout job / Job quá hạn thanh toán đơn đấu giá | Job | mỗi 1 phút | P0 | 4 | M |
| AO-05 | Auction Order | Handle AuctionCancelled / Xử lý AuctionCancelled | Event consumer | Consumer - AuctionCancelled | P2 | 4 | S |
